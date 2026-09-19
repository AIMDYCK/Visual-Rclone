using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using RcloneCommanderAdvanced.Models;
using RcloneCommanderAdvanced.Services.Abstractions;

namespace RcloneCommanderAdvanced.Services;

/// <summary>
/// Gestor de subprocesos de rclone.
///
/// Responsabilidades:
///  - Lanzar una instancia independiente de rclone.exe por cada montaje.
///  - Asignar un puerto RC dinamico y libre a cada instancia para evitar
///    colisiones de red entre unidades.
///  - Inyectar dinamicamente los argumentos del perfil seleccionado.
///  - Capturar stdout/stderr para diagnostico.
///  - Matar el arbol de procesos completo (Kill Tree) al desmontar o cerrar.
///  - Limpiar procesos huerfanos de ejecuciones anteriores.
/// </summary>
public sealed class MountProcessManager : IMountProcessManager
{
    private readonly ISettingsService _settingsService;
    private readonly IRcloneLocator _rcloneLocator;
    private readonly IDriveLetterService _driveLetterService;
    private readonly IRcloneRcClient _rcClient;

    private readonly ConcurrentDictionary<string, MountRuntimeInfo> _mounts =
        new(StringComparer.OrdinalIgnoreCase);

    private readonly ConcurrentDictionary<string, Process> _processes =
        new(StringComparer.OrdinalIgnoreCase);

    private readonly ConcurrentDictionary<string, CancellationTokenSource> _logPumps =
        new(StringComparer.OrdinalIgnoreCase);

    private readonly SemaphoreSlim _portLock = new(1, 1);
    private readonly HashSet<int> _allocatedPorts = new();

    private bool _disposed;

    public MountProcessManager(
        ISettingsService settingsService,
        IRcloneLocator rcloneLocator,
        IDriveLetterService driveLetterService,
        IRcloneRcClient rcClient)
    {
        _settingsService = settingsService;
        _rcloneLocator = rcloneLocator;
        _driveLetterService = driveLetterService;
        _rcClient = rcClient;
    }

    /// <inheritdoc />
    public event EventHandler<MountStateChangedEventArgs>? StateChanged;

    /// <inheritdoc />
    public event EventHandler<MountLogEventArgs>? LogReceived;

    /// <inheritdoc />
    public IReadOnlyCollection<MountRuntimeInfo> GetSnapshot() => _mounts.Values.ToList();

    /// <inheritdoc />
    public MountRuntimeInfo? GetInfo(string remoteName) =>
        _mounts.TryGetValue(remoteName, out var info) ? info : null;

    /// <inheritdoc />
    public async Task<bool> MountAsync(RemoteEntry remote, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(remote);

        if (string.IsNullOrWhiteSpace(remote.Name))
        {
            return false;
        }

        if (string.IsNullOrWhiteSpace(remote.DriveLetter))
        {
            RaiseState(remote.Name, MountState.Error, "El remoto no tiene letra de unidad asignada.");
            return false;
        }

        // Si ya esta montado, no hacemos nada.
        if (_processes.TryGetValue(remote.Name, out var existing) && !existing.HasExited)
        {
            RaiseState(remote.Name, MountState.Mounted, "El remoto ya estaba montado.");
            return true;
        }

        var rclonePath = _rcloneLocator.ResolveRclonePath();
        if (rclonePath is null)
        {
            RaiseState(remote.Name, MountState.Error, "No se encontro rclone.exe. Configure la ruta en Ajustes.");
            return false;
        }

        var letter = remote.DriveLetter.Trim().TrimEnd(':').ToUpperInvariant();

        if (!_driveLetterService.IsDriveLetterAvailable(letter) &&
            !_driveLetterService.IsDriveReady(letter))
        {
            RaiseState(remote.Name, MountState.Error, $"La letra {letter}: no esta disponible.");
            return false;
        }

        var port = await AllocateFreePortAsync(cancellationToken).ConfigureAwait(false);
        if (port <= 0)
        {
            RaiseState(remote.Name, MountState.Error, "No se pudo asignar un puerto RC libre.");
            return false;
        }

        var arguments = BuildArguments(remote, letter, port);

        var info = new MountRuntimeInfo
        {
            RemoteName = remote.Name,
            DriveLetter = letter,
            Profile = remote.Profile,
            State = MountState.Starting,
            RcPort = port
        };

        _mounts[remote.Name] = info;
        RaiseState(remote.Name, MountState.Starting, $"Iniciando montaje en {letter}: (RC:{port})...");

        // VISIBILIDAD DEL COMANDO (LOG DE UI): se publica la linea de comandos
        // EXACTA que se va a ejecutar, con todos los flags resueltos, para que
        // el usuario pueda auditarla directamente en la interfaz sin abrir una
        // consola. Se emite ANTES de lanzar el proceso.
        RaiseLog(remote.Name, $"Ejecutando: {rclonePath} {arguments}", isError: false);

        try
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = rclonePath,
                Arguments = arguments,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8,
                WorkingDirectory = AppContext.BaseDirectory
            };

            var process = new Process
            {
                StartInfo = startInfo,
                EnableRaisingEvents = true
            };

            process.Exited += (_, _) => OnProcessExited(remote.Name);

            if (!process.Start())
            {
                ReleasePort(port);
                RaiseState(remote.Name, MountState.Error, "No se pudo iniciar el proceso de rclone.");
                return false;
            }

            _processes[remote.Name] = process;
            info.ProcessId = process.Id;
            info.StartedAtUtc = DateTime.UtcNow;

            StartLogPump(remote.Name, process);

            // Espera activa a que la unidad aparezca (hasta 20 segundos).
            var mounted = await WaitForDriveAsync(letter, TimeSpan.FromSeconds(20), cancellationToken)
                .ConfigureAwait(false);

            if (mounted && !process.HasExited)
            {
                info.State = MountState.Mounted;
                RaiseState(remote.Name, MountState.Mounted, $"Montado correctamente en {letter}:.");
                return true;
            }

            if (process.HasExited)
            {
                info.State = MountState.Error;
                info.LastError = $"rclone termino con codigo {process.ExitCode}.";
                RaiseState(remote.Name, MountState.Error, info.LastError);
                CleanupProcess(remote.Name);
                return false;
            }

            // El proceso vive pero la unidad aun no responde: lo damos por bueno
            // y dejamos que el monitor de salud lo confirme despues.
            info.State = MountState.Mounted;
            RaiseState(remote.Name, MountState.Mounted,
                $"Proceso activo en {letter}: (la unidad puede tardar en aparecer).");
            return true;
        }
        catch (Exception ex) when (ex is InvalidOperationException or IOException or System.ComponentModel.Win32Exception)
        {
            ReleasePort(port);
            _mounts.TryRemove(remote.Name, out _);
            RaiseState(remote.Name, MountState.Error, $"Error al montar: {ex.Message}");
            return false;
        }
    }

    /// <inheritdoc />
    public async Task<bool> UnmountAsync(string remoteName, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(remoteName))
        {
            return false;
        }

        if (!_processes.TryGetValue(remoteName, out var process))
        {
            _mounts.TryRemove(remoteName, out _);
            RaiseState(remoteName, MountState.Stopped, "El remoto no estaba montado.");
            return true;
        }

        RaiseState(remoteName, MountState.Stopping, "Desmontando...");

        // FASE 2: desmontaje LIMPIO via API RC (POST /mount/unmount).
        // Esto permite que rclone vacie la cache VFS en disco y cierre el
        // punto de montaje WinFsp de forma ordenada. Si el endpoint no
        // responde (proceso colgado, RC no disponible), caemos al metodo
        // clasico de matar el arbol de procesos como red de seguridad.
        var port = _mounts.TryGetValue(remoteName, out var info) ? info.RcPort : 0;
        var mountPoint = info?.DriveLetter;

        var cleanUnmount = false;
        if (port > 0)
        {
            cleanUnmount = await _rcClient
                .UnmountAsync(port, mountPoint, cancellationToken)
                .ConfigureAwait(false);
        }

        if (cleanUnmount)
        {
            // Damos un margen breve para que rclone cierre el montaje y el
            // proceso termine por si mismo antes de forzar la salida.
            var exited = await WaitForExitAsync(process, TimeSpan.FromSeconds(5), cancellationToken)
                .ConfigureAwait(false);

            if (!exited)
            {
                await Task.Run(() => KillProcessTree(process), cancellationToken)
                    .ConfigureAwait(false);
            }

            CleanupProcess(remoteName);
            RaiseState(remoteName, MountState.Stopped, "Desmontado limpiamente (RC).");
            return true;
        }

        // Fallback: el endpoint RC no respondio; matamos el arbol de procesos.
        var killed = await Task.Run(() => KillProcessTree(process), cancellationToken)
            .ConfigureAwait(false);

        CleanupProcess(remoteName);
        RaiseState(remoteName, MountState.Stopped, killed ? "Desmontado." : "Proceso ya finalizado.");

        return true;
    }

    /// <summary>
    /// Espera de forma asincrona a que el proceso termine, con un limite de
    /// tiempo. Devuelve <c>true</c> si salio dentro del plazo.
    /// </summary>
    private static async Task<bool> WaitForExitAsync(
        Process process,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        try
        {
            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeoutCts.CancelAfter(timeout);

            await process.WaitForExitAsync(timeoutCts.Token).ConfigureAwait(false);
            return true;
        }
        catch (OperationCanceledException)
        {
            return process.HasExited;
        }
        catch (InvalidOperationException)
        {
            // El proceso ya no existe.
            return true;
        }
    }

    /// <inheritdoc />
    public async Task<int> MountAllAsync(IEnumerable<RemoteEntry> remotes, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(remotes);

        var targets = remotes
            .Where(r => r.IsEnabled && !string.IsNullOrWhiteSpace(r.DriveLetter))
            .ToList();

        if (targets.Count == 0)
        {
            return 0;
        }

        // Montaje secuencial: rclone + WinFsp no tolera bien montajes
        // simultaneos masivos y el arranque escalonado es mas estable.
        var successCount = 0;

        foreach (var remote in targets)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var ok = await MountAsync(remote, cancellationToken).ConfigureAwait(false);
            if (ok)
            {
                successCount++;
            }

            // Pequena pausa para dar tiempo a WinFsp a registrar la unidad.
            await Task.Delay(400, cancellationToken).ConfigureAwait(false);
        }

        return successCount;
    }

    /// <inheritdoc />
    public async Task<int> UnmountAllAsync(CancellationToken cancellationToken = default)
    {
        var names = _processes.Keys.ToList();
        var count = 0;

        foreach (var name in names)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (await UnmountAsync(name, cancellationToken).ConfigureAwait(false))
            {
                count++;
            }
        }

        return count;
    }

    /// <inheritdoc />
    public async Task<int> KillOrphanRcloneProcessesAsync()
    {
        return await Task.Run(() =>
        {
            var killed = 0;

            try
            {
                var rclonePath = _rcloneLocator.ResolveRclonePath();
                var targetName = rclonePath is not null
                    ? Path.GetFileNameWithoutExtension(rclonePath)
                    : "rclone";

                foreach (var process in Process.GetProcessesByName(targetName))
                {
                    try
                    {
                        // Solo matamos procesos que sean realmente rclone mount.
                        KillProcessTree(process);
                        killed++;
                    }
                    catch (InvalidOperationException)
                    {
                        // El proceso ya termino.
                    }
                    finally
                    {
                        process.Dispose();
                    }
                }
            }
            catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception)
            {
                // Sin permisos o sin procesos: no es critico.
            }

            return killed;
        }).ConfigureAwait(false);
    }

    /// <summary>
    /// Construye la linea de comandos completa para un montaje.
    /// Aqui es donde se inyectan dinamicamente los argumentos del perfil.
    /// </summary>
    private string BuildArguments(RemoteEntry remote, string letter, int port)
    {
        var settings = _settingsService.Current;
        var builder = new StringBuilder();

        // Comando base: rclone mount <remoto>: <letra>: <flags>
        builder.Append("mount ");
        builder.Append(Quote(remote.Name + ":"));
        builder.Append(' ');
        builder.Append(Quote(letter + ":"));
        builder.Append(' ');

        // Flags obligatorios para el montaje en Windows.
        builder.Append("--volname ").Append(Quote($"RcloneCommander - {remote.Name}")).Append(' ');

        // Modo de montaje elegido por el usuario:
        //   NetworkDrive -> se anade --network-mode (unidad de red).
        //   PhysicalDisk -> se omite (Windows la trata como disco local).
        if (remote.MountMode == MountMode.NetworkDrive)
        {
            builder.Append("--network-mode ");
        }

        builder.Append("--no-console ");
        builder.Append("--log-level INFO ");

        // Servidor RC aislado por puerto dinamico.
        builder.Append("--rc ");
        builder.Append("--rc-addr ").Append(Quote($"localhost:{port}")).Append(' ');
        builder.Append("--rc-no-auth ");

        // BUGFIX Monitor de Trafico (VFS): habilitamos las metricas del VFS por
        // RC. Sin este flag, rclone NO expone las estadisticas de la cache VFS
        // (vaciado asincrono de subidas/bajadas) y /core/stats reporta 0 B/s
        // aunque el usuario este copiando archivos con --vfs-cache-mode full.
        builder.Append("--rc-enable-metrics ");

        // Credenciales RC opcionales (si el usuario las definio).
        if (!string.IsNullOrWhiteSpace(settings.RcUser))
        {
            builder.Append("--rc-user ").Append(Quote(settings.RcUser)).Append(' ');
        }

        if (!string.IsNullOrWhiteSpace(settings.RcPassword))
        {
            builder.Append("--rc-pass ").Append(Quote(settings.RcPassword)).Append(' ');
        }

        // Argumentos del perfil seleccionado.
        //
        // BUGFIX VFS: se filtran flags incompatibles con "rclone mount".
        //  - --fast-list NO tiene efecto en un mount (rclone emite
        //    "NOTICE: --fast-list does nothing on a mount") y solo ensucia el log.
        //  - --skip-links es incompatible con --links (mutuamente excluyentes);
        //    como --links es obligatorio en Windows, se descarta.
        // El filtro es defensivo: aunque un perfil o los argumentos
        // personalizados los incluyeran, nunca llegaran a la linea de comandos.
        var profile = MountProfile.FromType(remote.Profile);
        foreach (var arg in profile.Arguments)
        {
            if (IsIncompatibleMountFlag(arg))
            {
                continue;
            }

            builder.Append(arg).Append(' ');
        }

        // FASE 1: optimizacion VFS por remoto.
        //
        // Los valores configurados por el usuario en la tarjeta tienen
        // PRIORIDAD sobre los del perfil: se inyectan DESPUES para que rclone
        // aplique el ultimo valor de cada flag (rclone usa el ultimo
        // --vfs-cache-mode / --buffer-size / --dir-cache-time de la linea).
        //
        // Solo se inyectan si el usuario los definio (no vacios), de modo que
        // un remoto sin tocar conserva exactamente el comportamiento del
        // perfil. VfsCacheMode cae a "full" cuando el perfil usa cache en
        // disco y el usuario no ha elegido nada (estandar recomendado en
        // Windows para evitar corrupcion de archivos).
        AppendVfsOverrides(builder, remote, profile);

        // FASE 3: limite de cache VFS obligatorio para perfiles con cache en
        // disco. Se inyecta SIEMPRE (no es opcional) para que rclone se
        // autolimpie al alcanzar el tope y nunca llene el disco C: hasta
        // bloquear el equipo. El valor es configurable (50G por defecto).
        if (profile.UsesDiskCache)
        {
            builder.Append("--vfs-cache-max-size ")
                   .Append(Quote(ResolveVfsCacheMaxSize(settings)))
                   .Append(' ');
        }

        // Argumentos personalizados (solo perfil Custom).
        if (remote.Profile == MountProfileType.Custom &&
            !string.IsNullOrWhiteSpace(remote.CustomArguments))
        {
            var custom = SanitizeCustomArguments(remote.CustomArguments);
            if (!string.IsNullOrWhiteSpace(custom))
            {
                builder.Append(custom).Append(' ');
            }
        }

        // BUGFIX SYMLINKS: garantia absoluta de que --links viaja en la linea
        // de comandos. Sin este flag, rclone/WinFsp aborta al encontrar accesos
        // directos de Google Drive en Windows:
        //   "ERROR : symlinks not supported without the --links flag: /"
        // Se anade al final para que tenga prioridad sobre cualquier otro flag
        // de enlaces que pudiera venir en los argumentos personalizados.
        if (!ContainsFlag(builder.ToString(), "--links"))
        {
            builder.Append("--links ");
        }

        return builder.ToString().TrimEnd();
    }

    /// <summary>
    /// BUGFIX VFS: detecta flags que NO son validos (o no tienen efecto) en el
    /// subcomando <c>rclone mount</c>:
    ///  - <c>--fast-list</c>: rclone lo ignora en montajes y lo reporta como
    ///    NOTICE en el log.
    ///  - <c>--skip-links</c>: mutuamente excluyente con <c>--links</c>, que es
    ///    obligatorio en Windows para soportar accesos directos.
    /// </summary>
    private static bool IsIncompatibleMountFlag(string argument)
    {
        if (string.IsNullOrWhiteSpace(argument))
        {
            return false;
        }

        var flag = argument.Trim();

        // Soporta tanto "--fast-list" como "--fast-list=true".
        var separatorIndex = flag.IndexOf('=');
        if (separatorIndex >= 0)
        {
            flag = flag[..separatorIndex];
        }

        return flag.Equals("--fast-list", StringComparison.OrdinalIgnoreCase) ||
               flag.Equals("--skip-links", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// BUGFIX VFS: elimina de los argumentos personalizados cualquier flag
    /// incompatible con <c>rclone mount</c> (por ahora <c>--fast-list</c>),
    /// preservando el resto de la cadena tal cual la escribio el usuario.
    /// </summary>
    private static string SanitizeCustomArguments(string customArguments)
    {
        var tokens = customArguments.Split(
            ' ',
            StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        var kept = new List<string>(tokens.Length);
        foreach (var token in tokens)
        {
            if (!IsIncompatibleMountFlag(token))
            {
                kept.Add(token);
            }
        }

        return string.Join(' ', kept);
    }

    /// <summary>
    /// BUGFIX SYMLINKS: comprueba si una linea de comandos ya contiene un flag
    /// concreto, comparando por token completo (no por subcadena) para evitar
    /// falsos positivos.
    /// </summary>
    private static bool ContainsFlag(string commandLine, string flag)
    {
        var tokens = commandLine.Split(
            ' ',
            StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        foreach (var token in tokens)
        {
            var candidate = token;
            var separatorIndex = candidate.IndexOf('=');
            if (separatorIndex >= 0)
            {
                candidate = candidate[..separatorIndex];
            }

            if (candidate.Equals(flag, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// FASE 1: inyecta los flags de optimizacion VFS configurados por el
    /// usuario en la tarjeta del dashboard.
    ///
    /// Reglas:
    ///  - <c>--vfs-cache-mode</c>: se inyecta si el usuario eligio un modo
    ///    valido (off/minimal/writes/full). Si no eligio nada y el perfil usa
    ///    cache en disco, se fuerza "full" (estandar recomendado en Windows).
    ///  - <c>--buffer-size</c>: se inyecta solo si el usuario lo definio y
    ///    tiene el formato de tamano de rclone (ej. "64M").
    ///  - <c>--dir-cache-time</c>: se inyecta solo si el usuario lo definio y
    ///    tiene el formato de duracion de rclone (ej. "24h").
    ///
    /// Los valores invalidos se ignoran silenciosamente para no romper el
    /// montaje: rclone usaria su valor por defecto.
    /// </summary>
    private static void AppendVfsOverrides(
        StringBuilder builder,
        RemoteEntry remote,
        MountProfile profile)
    {
        // --vfs-cache-mode -------------------------------------------------
        var cacheMode = remote.VfsCacheMode?.Trim().ToLowerInvariant();

        if (string.IsNullOrWhiteSpace(cacheMode))
        {
            // Sin eleccion explicita: si el perfil usa cache en disco,
            // garantizamos "full" (el estandar de Windows). Si no, dejamos
            // que el perfil decida (ya trae su propio --vfs-cache-mode).
            if (profile.UsesDiskCache)
            {
                builder.Append("--vfs-cache-mode ").Append("full").Append(' ');
            }
        }
        else if (IsValidVfsCacheMode(cacheMode))
        {
            builder.Append("--vfs-cache-mode ").Append(cacheMode).Append(' ');
        }

        // --buffer-size ----------------------------------------------------
        var bufferSize = remote.BufferSize?.Trim();
        if (!string.IsNullOrWhiteSpace(bufferSize) && IsValidSize(bufferSize))
        {
            builder.Append("--buffer-size ").Append(bufferSize).Append(' ');
        }

        // --dir-cache-time -------------------------------------------------
        var dirCacheTime = remote.DirCacheTime?.Trim();
        if (!string.IsNullOrWhiteSpace(dirCacheTime) && IsValidDuration(dirCacheTime))
        {
            builder.Append("--dir-cache-time ").Append(dirCacheTime).Append(' ');
        }
    }

    /// <summary>
    /// Valida que el modo de cache VFS sea uno de los cuatro oficiales de
    /// rclone. Evita inyectar valores arbitrarios que romperian el montaje.
    /// </summary>
    private static bool IsValidVfsCacheMode(string mode) =>
        mode is "off" or "minimal" or "writes" or "full";

    /// <summary>
    /// Valida un tamano de rclone (numero opcional con decimales + sufijo
    /// K/M/G/T, p. ej. "64M", "1.5G"). Reutiliza el mismo patron que el
    /// limite de cache VFS.
    /// </summary>
    private static bool IsValidSize(string value) =>
        System.Text.RegularExpressions.Regex.IsMatch(
            value,
            @"^\d+(\.\d+)?[KMGTkmgt]$",
            System.Text.RegularExpressions.RegexOptions.CultureInvariant);

    /// <summary>
    /// Valida una duracion de rclone (numero opcional con decimales + unidad
    /// s/m/h/d/w, p. ej. "24h", "30m", "1.5h"). rclone tambien acepta "off".
    /// </summary>
    private static bool IsValidDuration(string value) =>
        string.Equals(value, "off", StringComparison.OrdinalIgnoreCase) ||
        System.Text.RegularExpressions.Regex.IsMatch(
            value,
            @"^\d+(\.\d+)?(ms|s|m|h|d|w)$",
            System.Text.RegularExpressions.RegexOptions.CultureInvariant |
            System.Text.RegularExpressions.RegexOptions.IgnoreCase);

    /// <summary>
    /// FASE 3: resuelve el limite de cache VFS a inyectar en la linea de
    /// comandos. Devuelve el valor configurado por el usuario si es valido
    /// (formato rclone: numero + sufijo K/M/G/T, p. ej. "50G") o "50G" como
    /// valor seguro por defecto. Nunca devuelve vacio para garantizar que
    /// rclone siempre tenga un tope de cache y no llene el disco.
    /// </summary>
    private static string ResolveVfsCacheMaxSize(AppSettings settings)
    {
        const string fallback = "50G";

        var configured = settings.VfsCacheMaxSize?.Trim();
        if (string.IsNullOrWhiteSpace(configured))
        {
            return fallback;
        }

        // Validacion defensiva: solo aceptamos el patron de tamano de rclone
        // (digitos opcionales con decimales + sufijo K/M/G/T, p. ej. "50G",
        // "1.5G", "512M"). Cualquier otra cosa cae al valor por defecto.
        if (!System.Text.RegularExpressions.Regex.IsMatch(
                configured,
                @"^\d+(\.\d+)?[KMGTkmgt]$",
                System.Text.RegularExpressions.RegexOptions.CultureInvariant))
        {
            return fallback;
        }

        return configured;
    }

    /// <summary>
    /// Reserva un puerto TCP libre dentro del rango configurado.
    /// Si el rango es 0, se deja que el SO elija un puerto efimero.
    /// </summary>
    private async Task<int> AllocateFreePortAsync(CancellationToken cancellationToken)
    {
        await _portLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var settings = _settingsService.Current;
            var start = settings.RcPortBase > 0 ? settings.RcPortBase : 5572;
            var end = settings.RcPortRangeEnd > start ? settings.RcPortRangeEnd : start + 200;

            for (var port = start; port <= end; port++)
            {
                if (_allocatedPorts.Contains(port))
                {
                    continue;
                }

                if (IsPortFree(port))
                {
                    _allocatedPorts.Add(port);
                    return port;
                }
            }

            // Fallback: puerto efimero asignado por el sistema operativo.
            var ephemeral = GetEphemeralPort();
            if (ephemeral > 0)
            {
                _allocatedPorts.Add(ephemeral);
            }

            return ephemeral;
        }
        finally
        {
            _portLock.Release();
        }
    }

    private void ReleasePort(int port)
    {
        if (port <= 0)
        {
            return;
        }

        _portLock.Wait();
        try
        {
            _allocatedPorts.Remove(port);
        }
        finally
        {
            _portLock.Release();
        }
    }

    private static bool IsPortFree(int port)
    {
        try
        {
            var listener = new TcpListener(IPAddress.Loopback, port);
            listener.Start();
            listener.Stop();
            return true;
        }
        catch (SocketException)
        {
            return false;
        }
    }

    private static int GetEphemeralPort()
    {
        try
        {
            var listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            var port = ((IPEndPoint)listener.LocalEndpoint).Port;
            listener.Stop();
            return port;
        }
        catch (SocketException)
        {
            return 0;
        }
    }

    private static async Task<bool> WaitForDriveAsync(
        string letter,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        var deadline = DateTime.UtcNow + timeout;

        while (DateTime.UtcNow < deadline)
        {
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                var drives = DriveInfo.GetDrives();
                foreach (var drive in drives)
                {
                    if (drive.Name.StartsWith(letter, StringComparison.OrdinalIgnoreCase) &&
                        drive.IsReady)
                    {
                        return true;
                    }
                }
            }
            catch (IOException)
            {
                // Reintentamos hasta agotar el timeout.
            }

            await Task.Delay(500, cancellationToken).ConfigureAwait(false);
        }

        return false;
    }

    private void StartLogPump(string remoteName, Process process)
    {
        var cts = new CancellationTokenSource();
        _logPumps[remoteName] = cts;

        _ = Task.Run(async () =>
        {
            try
            {
                while (!cts.IsCancellationRequested && !process.HasExited)
                {
                    var line = await process.StandardOutput.ReadLineAsync(cts.Token)
                        .ConfigureAwait(false);

                    if (line is null)
                    {
                        break;
                    }

                    LogReceived?.Invoke(this, new MountLogEventArgs
                    {
                        RemoteName = remoteName,
                        Line = line,
                        IsError = false
                    });
                }
            }
            catch (OperationCanceledException)
            {
                // Cancelacion esperada al desmontar.
            }
            catch (InvalidOperationException)
            {
                // El proceso ya no permite leer la salida.
            }
        }, cts.Token);

        _ = Task.Run(async () =>
        {
            try
            {
                while (!cts.IsCancellationRequested && !process.HasExited)
                {
                    var line = await process.StandardError.ReadLineAsync(cts.Token)
                        .ConfigureAwait(false);

                    if (line is null)
                    {
                        break;
                    }

                    LogReceived?.Invoke(this, new MountLogEventArgs
                    {
                        RemoteName = remoteName,
                        Line = line,
                        IsError = true
                    });
                }
            }
            catch (OperationCanceledException)
            {
                // Cancelacion esperada al desmontar.
            }
            catch (InvalidOperationException)
            {
                // El proceso ya no permite leer la salida.
            }
        }, cts.Token);
    }

    private void OnProcessExited(string remoteName)
    {
        if (_mounts.TryGetValue(remoteName, out var info))
        {
            if (info.State != MountState.Stopping)
            {
                info.State = MountState.Error;
                info.LastError = "El proceso de rclone termino inesperadamente.";
                RaiseState(remoteName, MountState.Error, info.LastError);
            }
        }

        CleanupProcess(remoteName);
    }

    private void CleanupProcess(string remoteName)
    {
        if (_logPumps.TryRemove(remoteName, out var cts))
        {
            try
            {
                cts.Cancel();
            }
            catch (ObjectDisposedException)
            {
                // Ya estaba liberado.
            }

            cts.Dispose();
        }

        if (_processes.TryRemove(remoteName, out var process))
        {
            var port = _mounts.TryGetValue(remoteName, out var info) ? info.RcPort : 0;
            ReleasePort(port);

            try
            {
                process.Dispose();
            }
            catch (InvalidOperationException)
            {
                // Ignorado.
            }
        }
    }

    /// <summary>
    /// Mata un proceso y TODA su descendencia. rclone mount lanza procesos
    /// hijos (WinFsp) que quedarian huerfanos si solo matamos el padre.
    /// </summary>
    private static bool KillProcessTree(Process process)
    {
        if (process is null)
        {
            return false;
        }

        try
        {
            if (process.HasExited)
            {
                return false;
            }

            // taskkill /T mata el arbol completo, /F fuerza la terminacion.
            using var killer = Process.Start(new ProcessStartInfo
            {
                FileName = "taskkill",
                Arguments = $"/PID {process.Id} /T /F",
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            });

            killer?.WaitForExit(8000);

            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
                process.WaitForExit(5000);
            }

            return true;
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception or NotSupportedException)
        {
            return false;
        }
    }

    /// <summary>
    /// Publica una linea en el log de la UI asociada a un remoto.
    /// Se usa para exponer la linea de comandos exacta del montaje.
    /// </summary>
    private void RaiseLog(string remoteName, string line, bool isError)
    {
        LogReceived?.Invoke(this, new MountLogEventArgs
        {
            RemoteName = remoteName,
            Line = line,
            IsError = isError
        });
    }

    private void RaiseState(string remoteName, MountState state, string? message)
    {
        if (_mounts.TryGetValue(remoteName, out var info))
        {
            info.State = state;
            if (state == MountState.Error && message is not null)
            {
                info.LastError = message;
            }
        }

        StateChanged?.Invoke(this, new MountStateChangedEventArgs
        {
            RemoteName = remoteName,
            State = state,
            Message = message
        });
    }

    private static string Quote(string value) =>
        value.Contains(' ') ? $"\"{value}\"" : value;

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        // Garantia de limpieza: matamos todos los montajes gestionados.
        try
        {
            await UnmountAllAsync().ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is InvalidOperationException or ObjectDisposedException)
        {
            // La limpieza es best-effort durante el cierre.
        }

        foreach (var cts in _logPumps.Values)
        {
            try
            {
                cts.Cancel();
                cts.Dispose();
            }
            catch (ObjectDisposedException)
            {
                // Ignorado.
            }
        }

        _logPumps.Clear();
        _processes.Clear();
        _mounts.Clear();
        _portLock.Dispose();
    }
}
