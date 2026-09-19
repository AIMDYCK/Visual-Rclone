using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using RcloneCommanderAdvanced.Models;
using RcloneCommanderAdvanced.Services.Abstractions;

namespace RcloneCommanderAdvanced.ViewModels;

/// <summary>
/// ViewModel de una tarjeta de remoto en el dashboard.
/// Se genera dinamicamente por cada remoto detectado en rclone.conf.
/// </summary>
public sealed partial class RemoteCardViewModel : ObservableObject, IDisposable
{
    /// <summary>Intervalo de sondeo de telemetria RC (ms).</summary>
    private const int TelemetryIntervalMs = 1000;

    /// <summary>Maximo de lineas de log conservadas por unidad.</summary>
    private const int MaxLogLines = 100;

    /// <summary>
    /// FEATURE Monitor de Trafico: numero maximo de muestras historicas
    /// conservadas en el buffer circular (60 muestras x 1 s = 1 minuto).
    /// </summary>
    public const int TrafficHistoryCapacity = 60;

    private readonly Action<RemoteCardViewModel>? _onChanged;

    /// <summary>
    /// BUGFIX Persistencia VFS (v2): callback de guardado INMEDIATO a disco.
    /// Se invoca directamente desde los setters de VfsCacheMode, BufferSize y
    /// DirCacheTime para que el ajuste quede escrito en appsettings.json en
    /// cuanto el usuario lo cambia, sin depender del debounce ni de que la app
    /// se cierre de forma ordenada.
    /// </summary>
    private readonly Action<RemoteCardViewModel>? _onPersistNow;

    private readonly IRcloneLocator? _rcloneLocator;
    private readonly IRcloneRcClient? _rcClient;
    private readonly ILocalizationService? _localization;

    /// <summary>Cancela el bucle de sondeo de telemetria al desmontar.</summary>
    private CancellationTokenSource? _telemetryCts;

    /// <summary>Evita arrancar dos bucles de sondeo simultaneos.</summary>
    private bool _telemetryRunning;

    // DIAGNOSTICO: contadores crudos incrementados desde el hilo de fondo.
    private long _telemetryTickCountRaw;
    private long _telemetrySuccessCountRaw;

    public RemoteCardViewModel(
        RemoteEntry model,
        Action<RemoteCardViewModel>? onChanged = null,
        IRcloneLocator? rcloneLocator = null,
        IRcloneRcClient? rcClient = null,
        ILocalizationService? localization = null,
        Action<RemoteCardViewModel>? onPersistNow = null)
    {
        Model = model ?? throw new ArgumentNullException(nameof(model));
        _onChanged = onChanged;
        _onPersistNow = onPersistNow;
        _rcloneLocator = rcloneLocator;
        _rcClient = rcClient;
        _localization = localization;

        _driveLetter = model.DriveLetter;
        _profileType = model.Profile;
        _isEnabled = model.IsEnabled;
        _isHiddenDrive = model.IsHiddenDrive;
        _mountMode = model.MountMode;
        _customArguments = model.CustomArguments;

        // FASE 1: precarga de los parametros VFS persistidos por remoto.
        _vfsCacheMode = model.VfsCacheMode;
        _bufferSize = model.BufferSize;
        _dirCacheTime = model.DirCacheTime;

        // Estado inicial localizado.
        _statusMessage = L("Str_Stopped", "Stopped");
    }

    /// <summary>
    /// Atajo de traduccion: resuelve una clave en el idioma activo o devuelve
    /// el fallback indicado si no hay servicio de localizacion disponible.
    /// </summary>
    private string L(string key, string fallback) =>
        _localization?.Get(key, fallback) ?? fallback;

    /// <summary>Modelo subyacente persistido.</summary>
    public RemoteEntry Model { get; }

    /// <summary>Nombre del remoto tal como aparece en rclone.conf.</summary>
    public string Name => Model.Name;

    /// <summary>Tipo de backend (drive, s3, onedrive...).</summary>
    public string BackendType => string.IsNullOrWhiteSpace(Model.Type)
        ? L("Str_Unknown", "unknown")
        : Model.Type;

    [ObservableProperty]
    private string _driveLetter;

    [ObservableProperty]
    private MountProfileType _profileType;

    [ObservableProperty]
    private bool _isEnabled;

    /// <summary>
    /// Indica si la unidad debe montarse como "oculta": la letra de unidad se
    /// oculta en el Explorador de Windows modificando la clave de registro
    /// <c>NoDrives</c> (HKCU\...\Policies\Explorer).
    /// </summary>
    [ObservableProperty]
    private bool _isHiddenDrive;

    /// <summary>
    /// Modo de montaje elegido por el usuario: unidad de red o disco fisico.
    /// </summary>
    [ObservableProperty]
    private MountMode _mountMode = MountMode.NetworkDrive;

    /// <summary>
    /// Atajo booleano para el CheckBox de la UI: true = disco fisico/local,
    /// false = unidad de red. Se sincroniza con <see cref="MountMode"/>.
    /// </summary>
    public bool IsPhysicalDisk
    {
        get => MountMode == MountMode.PhysicalDisk;
        set => MountMode = value ? MountMode.PhysicalDisk : MountMode.NetworkDrive;
    }

    /// <summary>
    /// BUGFIX UX: indica si la opcion "Montar como Oculto" es aplicable.
    ///
    /// La directiva NoDrives (HKCU\...\Policies\Explorer) solo oculta de forma
    /// fiable unidades montadas como disco fisico local. En modo unidad de red
    /// (<c>--network-mode</c>) el flag de oculto NO tiene efecto, por lo que
    /// esta propiedad devuelve <c>false</c> y la vista deshabilita el control
    /// para impedir combinaciones incompatibles.
    /// </summary>
    public bool CanMountHidden => MountMode == MountMode.PhysicalDisk;

    [ObservableProperty]
    private string _customArguments;

    // ---------------------------------------------------------------------
    // FASE 1: Optimizacion VFS (Virtual File System) por remoto.
    // Binding bidireccional con los controles de la tarjeta. Los valores se
    // inyectan en `rclone mount` solo si no estan vacios (ver BuildArguments).
    // ---------------------------------------------------------------------

    /// <summary>
    /// Modo de cache VFS (<c>--vfs-cache-mode</c>): off, minimal, writes, full.
    /// Vacio = "full" (estandar recomendado en Windows).
    /// </summary>
    [ObservableProperty]
    private string _vfsCacheMode;

    /// <summary>
    /// Tamano del buffer de lectura (<c>--buffer-size</c>), ej. "16M", "64M".
    /// Vacio = no se inyecta.
    /// </summary>
    [ObservableProperty]
    private string _bufferSize;

    /// <summary>
    /// Tiempo de cache del listado de directorios (<c>--dir-cache-time</c>),
    /// ej. "1h", "24h", "72h". Vacio = no se inyecta.
    /// </summary>
    [ObservableProperty]
    private string _dirCacheTime;

    [ObservableProperty]
    private MountState _state = MountState.Stopped;

    [ObservableProperty]
    private string _statusMessage = string.Empty;

    [ObservableProperty]
    private int _rcPort;

    // ---------------------------------------------------------------------
    // Feature 1: Telemetria en tiempo real (RC /core/stats)
    // ---------------------------------------------------------------------

    /// <summary>Velocidad de bajada formateada (ej: "1.2 MB/s").</summary>
    [ObservableProperty]
    private string _downloadSpeedText = "0 B/s";

    /// <summary>Velocidad de subida formateada (ej: "512 KB/s").</summary>
    [ObservableProperty]
    private string _uploadSpeedText = "0 B/s";

    /// <summary>Numero de transferencias activas reportadas por rclone.</summary>
    [ObservableProperty]
    private int _activeTransfers;

    /// <summary>Indica si hay actividad de red (para resaltar el indicador).</summary>
    [ObservableProperty]
    private bool _hasNetworkActivity;

    /// <summary>
    /// Indica si la telemetria esta disponible (unidad montada y RC
    /// respondiendo). Controla la visibilidad de los indicadores.
    /// </summary>
    [ObservableProperty]
    private bool _isTelemetryAvailable;

    // ---------------------------------------------------------------------
    // FEATURE Monitor de Trafico: historial circular de velocidades.
    // Se conservan las ultimas TrafficHistoryCapacity muestras (1 por
    // segundo). El Monitor de Trafico consume esta coleccion para dibujar
    // la grafica de lineas en tiempo real.
    // ---------------------------------------------------------------------

    /// <summary>
    /// Historial reciente de muestras de trafico (subida/bajada en bytes/s).
    /// Coleccion circular limitada a <see cref="TrafficHistoryCapacity"/>
    /// elementos. Todas las mutaciones se realizan en el hilo de UI.
    /// </summary>
    public ObservableCollection<TrafficSample> TrafficHistory { get; } = new();

    /// <summary>Pico maximo de bajada observado en el historial (bytes/s).</summary>
    [ObservableProperty]
    private double _peakDownloadBytesPerSecond;

    /// <summary>Pico maximo de subida observado en el historial (bytes/s).</summary>
    [ObservableProperty]
    private double _peakUploadBytesPerSecond;

    /// <summary>Velocidad de bajada instantanea en bytes/s (sin formatear).</summary>
    [ObservableProperty]
    private double _downloadBytesPerSecond;

    /// <summary>Velocidad de subida instantanea en bytes/s (sin formatear).</summary>
    [ObservableProperty]
    private double _uploadBytesPerSecond;

    // ---------------------------------------------------------------------
    // DIAGNOSTICO Monitor de Trafico: estado interno del bucle de sondeo.
    // Se exponen para que la ventana grafica pueda mostrar POR QUE no llegan
    // datos (puerto invalido, excepcion HTTP, JSON inesperado, etc.).
    // ---------------------------------------------------------------------

    /// <summary>Numero de ciclos de sondeo completados (exito o fallo).</summary>
    [ObservableProperty]
    private long _telemetryTickCount;

    /// <summary>Numero de respuestas validas recibidas de /core/stats.</summary>
    [ObservableProperty]
    private long _telemetrySuccessCount;

    /// <summary>Ultimo error capturado por el bucle de telemetria (vacio = sin error).</summary>
    [ObservableProperty]
    private string _lastTelemetryError = string.Empty;

    /// <summary>Indica si el bucle de sondeo esta activo en este momento.</summary>
    [ObservableProperty]
    private bool _isTelemetryRunning;

    // ---------------------------------------------------------------------
    // Feature 3: Buffer de logs por unidad
    // ---------------------------------------------------------------------

    /// <summary>
    /// Ultimas lineas de stdout/stderr de esta unidad (maximo
    /// <see cref="MaxLogLines"/>). Alimentado desde el evento LogReceived
    /// del MountProcessManager.
    /// </summary>
    public ObservableCollection<string> LogLines { get; } = new();

    /// <summary>Indica si hay al menos una linea de log capturada.</summary>
    public bool HasLogs => LogLines.Count > 0;

    /// <summary>Perfiles disponibles para el ComboBox de la tarjeta.</summary>
    public IReadOnlyList<MountProfile> AvailableProfiles => MountProfile.Defaults;

    /// <summary>
    /// FASE 1: modos de cache VFS ofrecidos en el ComboBox de la tarjeta.
    /// Son los cuatro valores oficiales de <c>--vfs-cache-mode</c> de rclone.
    /// </summary>
    public IReadOnlyList<string> VfsCacheModes { get; } = new List<string>
    {
        "off",
        "minimal",
        "writes",
        "full"
    };

    /// <summary>
    /// FASE 1: tamanos de buffer sugeridos para el ComboBox editable de
    /// <c>--buffer-size</c>. El usuario puede escribir cualquier otro valor.
    /// </summary>
    public IReadOnlyList<string> BufferSizeOptions { get; } = new List<string>
    {
        "16M",
        "32M",
        "64M",
        "128M",
        "256M"
    };

    /// <summary>
    /// FASE 1: tiempos de cache de directorio sugeridos para el ComboBox
    /// editable de <c>--dir-cache-time</c>. El usuario puede escribir otro.
    /// </summary>
    public IReadOnlyList<string> DirCacheTimeOptions { get; } = new List<string>
    {
        "1h",
        "6h",
        "24h",
        "72h",
        "168h"
    };

    /// <summary>Nombre del perfil activo (localizado).</summary>
    public string ProfileDisplayName => _localization is null
        ? MountProfile.FromType(ProfileType).DisplayNameKey
        : MountProfile.FromType(ProfileType).GetDisplayName(_localization);

    /// <summary>Descripcion del perfil activo (tooltip, localizada).</summary>
    public string ProfileDescription => _localization is null
        ? MountProfile.FromType(ProfileType).DescriptionKey
        : MountProfile.FromType(ProfileType).GetDescription(_localization);

    /// <summary>Ventajas del perfil activo (texto de ayuda, localizado).</summary>
    public string ProfilePros => _localization is null
        ? MountProfile.FromType(ProfileType).ProsKey
        : MountProfile.FromType(ProfileType).GetPros(_localization);

    /// <summary>Inconvenientes del perfil activo (texto de ayuda, localizado).</summary>
    public string ProfileCons => _localization is null
        ? MountProfile.FromType(ProfileType).ConsKey
        : MountProfile.FromType(ProfileType).GetCons(_localization);

    /// <summary>Texto combinado pros/contras del perfil activo (localizado).</summary>
    public string ProfileProsConsText => _localization is null
        ? string.Empty
        : MountProfile.FromType(ProfileType).GetProsConsText(_localization);

    /// <summary>
    /// Explicacion de pros y contras del modo de montaje elegido
    /// (unidad de red vs disco fisico).
    /// </summary>
    public string MountModeProsConsText
    {
        get
        {
            var pros = L("Str_Pros", "Pros");
            var cons = L("Str_Cons", "Cons");

            return MountMode == MountMode.PhysicalDisk
                ? $"\u2714 {pros}: {L("Str_ModePhysicalPros", string.Empty)}\n" +
                  $"\u2716 {cons}: {L("Str_ModePhysicalCons", string.Empty)}"
                : $"\u2714 {pros}: {L("Str_ModeNetworkPros", string.Empty)}\n" +
                  $"\u2716 {cons}: {L("Str_ModeNetworkCons", string.Empty)}";
        }
    }

    /// <summary>Etiqueta corta del modo de montaje activo (localizada).</summary>
    public string MountModeLabel => MountMode == MountMode.PhysicalDisk
        ? L("Str_ModePhysicalDisk", "PHYSICAL DISK")
        : L("Str_ModeNetworkDrive", "NETWORK DRIVE");

    /// <summary>Indica si el perfil activo es de solo lectura.</summary>
    public bool IsReadOnlyProfile => MountProfile.FromType(ProfileType).IsReadOnly;

    /// <summary>Indica si el perfil activo escribe cache en disco.</summary>
    public bool UsesDiskCache => MountProfile.FromType(ProfileType).UsesDiskCache;

    /// <summary>
    /// Texto del badge informativo del perfil activo (dinamico).
    /// SafeRead -> "SOLO LECTURA"; HighPerformance -> "CACHE SSD (5GB)"; BulkTransfer -> "STREAMING RAM".
    /// </summary>
    public string ProfileBadgeText => ProfileType switch
    {
        MountProfileType.SafeRead => L("Str_BadgeReadOnly", "READ ONLY"),
        MountProfileType.HighPerformance => L("Str_BadgeSsdCache", "SSD CACHE (5GB)"),
        MountProfileType.BulkTransfer => L("Str_BadgeStreamingRam", "RAM STREAMING"),
        _ => L("Str_BadgeProfile", "PROFILE")
    };

    /// <summary>Color de fondo del badge del perfil activo (dinamico).</summary>
    public string ProfileBadgeBackground => ProfileType switch
    {
        MountProfileType.SafeRead => "#064E3B",
        MountProfileType.HighPerformance => "#78350F",
        MountProfileType.BulkTransfer => "#164E63",
        _ => "#1F2937"
    };

    /// <summary>Color del borde del badge del perfil activo (dinamico).</summary>
    public string ProfileBadgeBorder => ProfileType switch
    {
        MountProfileType.SafeRead => "#10B981",
        MountProfileType.HighPerformance => "#F59E0B",
        MountProfileType.BulkTransfer => "#06B6D4",
        _ => "#374151"
    };

    /// <summary>Color del texto del badge del perfil activo (dinamico).</summary>
    public string ProfileBadgeForeground => ProfileType switch
    {
        MountProfileType.SafeRead => "#10B981",
        MountProfileType.HighPerformance => "#F59E0B",
        MountProfileType.BulkTransfer => "#06B6D4",
        _ => "#9CA3AF"
    };

    /// <summary>Color del indicador de estado (verde/rojo/ambar).</summary>
    public string StatusColor => State switch
    {
        MountState.Mounted => "#3FB950",
        MountState.Starting => "#D29922",
        MountState.Stopping => "#D29922",
        MountState.Error => "#F85149",
        _ => "#6E7681"
    };

    /// <summary>Texto corto del estado para la tarjeta.</summary>
    public string StatusText => State switch
    {
        MountState.Mounted => L("Str_StatusMounted", "MOUNTED"),
        MountState.Starting => L("Str_StatusStarting", "STARTING"),
        MountState.Stopping => L("Str_StatusStopping", "UNMOUNTING"),
        MountState.Error => L("Str_StatusError", "ERROR"),
        _ => L("Str_StatusStopped", "STOPPED")
    };

    /// <summary>Etiqueta de la letra de unidad (ej: "X:").</summary>
    public string DriveLabel =>
        string.IsNullOrWhiteSpace(DriveLetter)
            ? L("Str_Unassigned", "UNASSIGNED")
            : $"{DriveLetter.TrimEnd(':')}:";

    /// <summary>Indica si el remoto esta listo para montarse.</summary>
    public bool CanMount =>
        IsEnabled && !string.IsNullOrWhiteSpace(DriveLetter) &&
        State is MountState.Stopped or MountState.Error;

    /// <summary>Indica si el remoto puede desmontarse.</summary>
    public bool CanUnmount => State is MountState.Mounted or MountState.Starting;

    /// <summary>Indica si el montaje esta activo (montado o arrancando).</summary>
    public bool IsMounted => State is MountState.Mounted or MountState.Starting;

    /// <summary>
    /// Indica si la unidad esta efectivamente montada y por tanto se puede
    /// abrir su raiz en el Explorador de Windows.
    /// </summary>
    public bool CanOpenInExplorer =>
        State == MountState.Mounted && !string.IsNullOrWhiteSpace(DriveLetter);

    /// <summary>Letra de unidad normalizada sin dos puntos (ej: "X").</summary>
    private string NormalizedLetter => (DriveLetter ?? string.Empty).Trim().TrimEnd(':');

    /// <summary>Texto dinamico del boton de accion de la tarjeta (localizado).</summary>
    public string MountButtonText => IsMounted
        ? L("Str_Unmount", "UNMOUNT")
        : L("Str_Mount", "MOUNT");

    /// <summary>Color de fondo dinamico del boton de accion (verde/rojo).</summary>
    public string MountButtonBackground => IsMounted ? "#EF4444" : "#10B981";

    /// <summary>Color de fondo del boton al pasar el raton (hover).</summary>
    public string MountButtonHoverBackground => IsMounted ? "#DC2626" : "#059669";

    /// <summary>Color del borde del boton de accion.</summary>
    public string MountButtonBorder => IsMounted ? "#EF4444" : "#10B981";

    /// <summary>Sincroniza los cambios de la tarjeta hacia el modelo persistible.</summary>
    public void ApplyToModel()
    {
        Model.DriveLetter = DriveLetter ?? string.Empty;
        Model.Profile = ProfileType;
        Model.IsEnabled = IsEnabled;
        Model.IsHiddenDrive = IsHiddenDrive;
        Model.MountMode = MountMode;
        Model.CustomArguments = CustomArguments ?? string.Empty;

        // FASE 1: persistir los parametros VFS configurados por el usuario.
        Model.VfsCacheMode = VfsCacheMode ?? string.Empty;
        Model.BufferSize = BufferSize ?? string.Empty;
        Model.DirCacheTime = DirCacheTime ?? string.Empty;
    }

    /// <summary>Actualiza el estado en runtime desde el ProcessManager.</summary>
    public void UpdateRuntime(MountRuntimeInfo info)
    {
        // BUGFIX Monitor de Trafico: el puerto RC DEBE asignarse ANTES que el
        // estado. Al fijar State = Mounted se dispara OnStateChanged, que llama
        // a StartTelemetry(); si RcPort aun valiera 0, el guard `RcPort <= 0`
        // abortaria el arranque del bucle de sondeo y la grafica se quedaria
        // congelada en "0 B/s" para siempre.
        RcPort = info.RcPort;
        State = info.State;
        StatusMessage = string.IsNullOrWhiteSpace(info.LastError)
            ? StatusText
            : info.LastError;
    }

    // ---------------------------------------------------------------------
    // Feature 1: bucle de sondeo de telemetria
    // ---------------------------------------------------------------------

    /// <summary>
    /// Arranca el sondeo de telemetria si la unidad esta montada y aun no
    /// hay un bucle activo. Es idempotente.
    /// </summary>
    public void StartTelemetry()
    {
        if (_rcClient is null || _telemetryRunning || RcPort <= 0)
        {
            // DIAGNOSTICO: dejamos rastro de por que NO arranco el sondeo.
            if (_rcClient is null)
            {
                LastTelemetryError = "DIAG: _rcClient es null (no se inyecto el cliente RC).";
            }
            else if (RcPort <= 0)
            {
                LastTelemetryError = $"DIAG: RcPort invalido ({RcPort}); el montaje no expuso puerto RC.";
            }

            return;
        }

        _telemetryRunning = true;
        IsTelemetryRunning = true;
        LastTelemetryError = string.Empty;
        TelemetryTickCount = 0;
        TelemetrySuccessCount = 0;
        _telemetryCts = new CancellationTokenSource();
        _ = RunTelemetryLoopAsync(_telemetryCts.Token);
    }

    /// <summary>
    /// BUGFIX Monitor de Trafico: fuerza el arranque del sondeo cuando la
    /// ventana grafica se abre. Si la unidad esta montada pero el bucle no
    /// arranco (p. ej. porque el puerto RC se asigno tarde), lo re-arma.
    /// Es seguro llamarlo varias veces: <see cref="StartTelemetry"/> es
    /// idempotente.
    /// </summary>
    public void EnsureTelemetry()
    {
        if (State == MountState.Mounted && RcPort > 0)
        {
            StartTelemetry();
        }
    }

    /// <summary>
    /// Detiene el sondeo de telemetria y limpia los indicadores. Se invoca
    /// al desmontar la unidad o al cerrar la aplicacion.
    /// </summary>
    public void StopTelemetry()
    {
        if (!_telemetryRunning)
        {
            return;
        }

        _telemetryRunning = false;

        try
        {
            _telemetryCts?.Cancel();
        }
        catch (ObjectDisposedException)
        {
            // Ignorado: el CTS ya fue liberado.
        }

        _telemetryCts?.Dispose();
        _telemetryCts = null;

        // Reset visual: sin unidad montada no hay trafico.
        DownloadSpeedText = "0 B/s";
        UploadSpeedText = "0 B/s";
        ActiveTransfers = 0;
        HasNetworkActivity = false;
        IsTelemetryAvailable = false;

        // FEATURE Monitor de Trafico: limpiamos el historial para que una
        // nueva sesion de montaje arranque con la grafica vacia.
        DownloadBytesPerSecond = 0;
        UploadBytesPerSecond = 0;
        PeakDownloadBytesPerSecond = 0;
        PeakUploadBytesPerSecond = 0;
        TrafficHistory.Clear();
    }

    /// <summary>
    /// Bucle asincrono que consulta <c>/core/stats</c> cada
    /// <see cref="TelemetryIntervalMs"/> ms hasta que se cancele. Se ejecuta
    /// en segundo plano; las actualizaciones de propiedades se marshalean al
    /// hilo de UI mediante el SynchronizationContext capturado.
    /// </summary>
    private async Task RunTelemetryLoopAsync(CancellationToken cancellationToken)
    {
        // BUGFIX Monitor de Trafico: resolvemos el dispatcher de UI de forma
        // robusta. Si Application.Current no estuviera disponible (caso raro),
        // caemos al SynchronizationContext capturado al arrancar el bucle, que
        // en WPF es el dispatcher del hilo de UI. Asi garantizamos que las
        // mutaciones de TrafficHistory (ObservableCollection) y de las
        // propiedades observadas ocurran SIEMPRE en el hilo de UI.
        var dispatcher = System.Windows.Application.Current?.Dispatcher;
        var syncContext = SynchronizationContext.Current;

        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                // DIAGNOSTICO: contamos cada ciclo para saber si el bucle gira.
                var tick = Interlocked.Increment(ref _telemetryTickCountRaw);

                // DIAGNOSTICO: GetStatsAsync ya NO devuelve null generico; el
                // resultado lleva el error EXACTO (codigo HTTP, excepcion o
                // fallo de JSON) para exponerlo en la UI.
                RcStatsResult result;
                try
                {
                    result = await _rcClient!
                        .GetStatsAsync(RcPort, cancellationToken)
                        .ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    // Salvaguarda: si el propio cliente lanzara, lo exponemos.
                    result = RcStatsResult.Fail($"{ex.GetType().Name}: {ex.Message}");
                }

                if (cancellationToken.IsCancellationRequested)
                {
                    break;
                }

                // DIAGNOSTICO: publicamos el progreso del bucle en la UI.
                var tickSnapshot = tick;
                var errorSnapshot = result.Error;
                PostToUi(() =>
                {
                    TelemetryTickCount = tickSnapshot;

                    if (!string.IsNullOrEmpty(errorSnapshot))
                    {
                        // DIAGNOSTICO: mostramos el error EXACTO, sin genericos.
                        LastTelemetryError = $"tick {tickSnapshot}: {errorSnapshot}";
                    }
                });

                var stats = result.Stats;

                if (stats is not null)
                {
                    var successSnapshot = Interlocked.Increment(ref _telemetrySuccessCountRaw);
                    PostToUi(() =>
                    {
                        TelemetrySuccessCount = successSnapshot;
                        LastTelemetryError = string.Empty;
                    });

                    var downBytes = stats.DownloadSpeedBytesPerSecond;
                    var upBytes = stats.UploadSpeedBytesPerSecond;
                    var down = FormatSpeed(downBytes);
                    var up = FormatSpeed(upBytes);
                    var transfers = stats.ActiveTransfers;
                    var active = downBytes > 0 || upBytes > 0 || transfers > 0;

                    void Apply()
                    {
                        DownloadSpeedText = down;
                        UploadSpeedText = up;
                        ActiveTransfers = transfers;
                        HasNetworkActivity = active;
                        IsTelemetryAvailable = true;

                        // FEATURE Monitor de Trafico: exponemos los valores
                        // crudos y alimentamos el buffer circular de historial.
                        // Se ejecuta ya en el hilo de UI (ver marshalling mas
                        // abajo), por lo que mutar la ObservableCollection es
                        // seguro y no provoca excepciones de concurrencia.
                        DownloadBytesPerSecond = downBytes;
                        UploadBytesPerSecond = upBytes;

                        if (downBytes > PeakDownloadBytesPerSecond)
                        {
                            PeakDownloadBytesPerSecond = downBytes;
                        }

                        if (upBytes > PeakUploadBytesPerSecond)
                        {
                            PeakUploadBytesPerSecond = upBytes;
                        }

                        TrafficHistory.Add(new TrafficSample(
                            DateTime.UtcNow,
                            downBytes,
                            upBytes,
                            transfers));

                        while (TrafficHistory.Count > TrafficHistoryCapacity)
                        {
                            TrafficHistory.RemoveAt(0);
                        }
                    }

                    // Marshalling al hilo de UI: preferimos el Dispatcher de
                    // WPF; si no esta disponible, usamos el SynchronizationContext.
                    if (dispatcher is not null)
                    {
                        if (dispatcher.CheckAccess())
                        {
                            Apply();
                        }
                        else
                        {
                            dispatcher.Invoke(Apply);
                        }
                    }
                    else if (syncContext is not null)
                    {
                        syncContext.Post(_ => Apply(), null);
                    }
                    else
                    {
                        // Ultimo recurso: sin contexto de UI no podemos mutar
                        // colecciones enlazadas; evitamos la excepcion cruzada.
                        Apply();
                    }
                }

                await Task.Delay(TelemetryIntervalMs, cancellationToken).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException)
        {
            // Cancelacion esperada al desmontar.
        }
        catch (Exception ex)
        {
            // DIAGNOSTICO: exponemos el fallo fatal del bucle en la UI.
            var message = $"DIAG bucle abortado: {ex.GetType().Name}: {ex.Message}";
            PostToUi(() => LastTelemetryError = message);
        }
        finally
        {
            _telemetryRunning = false;
            PostToUi(() => IsTelemetryRunning = false);
        }
    }

    /// <summary>
    /// DIAGNOSTICO: ejecuta una accion en el hilo de UI de forma segura. Si ya
    /// estamos en el hilo de UI, la ejecuta directamente; si no, la despacha.
    /// </summary>
    private static void PostToUi(Action action)
    {
        var dispatcher = System.Windows.Application.Current?.Dispatcher;

        if (dispatcher is null || dispatcher.CheckAccess())
        {
            action();
        }
        else
        {
            dispatcher.BeginInvoke(action);
        }
    }

    /// <summary>Formatea bytes/segundo a una cadena legible (B/s, KB/s, MB/s, GB/s).</summary>
    private static string FormatSpeed(double bytesPerSecond)
    {
        if (bytesPerSecond <= 0)
        {
            return "0 B/s";
        }

        string[] units = { "B/s", "KB/s", "MB/s", "GB/s", "TB/s" };
        var value = bytesPerSecond;
        var unit = 0;

        while (value >= 1024 && unit < units.Length - 1)
        {
            value /= 1024;
            unit++;
        }

        return unit == 0
            ? $"{value:0} {units[unit]}"
            : $"{value:0.#} {units[unit]}";
    }

    // ---------------------------------------------------------------------
    // Feature 3: buffer de logs por unidad
    // ---------------------------------------------------------------------

    /// <summary>
    /// Anade una linea al buffer de logs de la unidad, recortando el exceso
    /// para no crecer sin limite. Debe invocarse en el hilo de UI.
    /// </summary>
    public void AppendLog(string line)
    {
        if (string.IsNullOrWhiteSpace(line))
        {
            return;
        }

        LogLines.Add(line);

        while (LogLines.Count > MaxLogLines)
        {
            LogLines.RemoveAt(0);
        }

        OnPropertyChanged(nameof(HasLogs));
    }

    /// <summary>Vacia el buffer de logs de la unidad.</summary>
    [RelayCommand]
    private void ClearLogs()
    {
        LogLines.Clear();
        OnPropertyChanged(nameof(HasLogs));
    }

    // ---------------------------------------------------------------------
    // FEATURE Monitor de Trafico: apertura de la ventana grafica
    // ---------------------------------------------------------------------

    /// <summary>
    /// Se dispara cuando el usuario pide abrir el monitor de trafico grafico
    /// de esta unidad. El dashboard (MainViewModel) escucha este evento y
    /// abre la ventana, pasandole el buffer historico de esta tarjeta.
    /// </summary>
    public event EventHandler? TrafficMonitorRequested;

    /// <summary>
    /// Comando enlazado al boton "Ver Monitor de Trafico" de la tarjeta.
    /// Solo notifica al dashboard; la creacion de la ventana vive en la capa
    /// de vista para mantener el ViewModel libre de dependencias de UI.
    /// </summary>
    [RelayCommand]
    private void OpenTrafficMonitor() => TrafficMonitorRequested?.Invoke(this, EventArgs.Empty);

    /// <summary>Libera el bucle de telemetria y sus recursos de red.</summary>
    public void Dispose() => StopTelemetry();

    /// <summary>
    /// Re-notifica todas las propiedades calculadas que dependen del idioma
    /// activo. Se invoca desde el dashboard cuando el usuario cambia de idioma
    /// para que la tarjeta se repinte sin recrear el ViewModel.
    /// </summary>
    public void RefreshLocalization()
    {
        OnPropertyChanged(nameof(BackendType));
        OnPropertyChanged(nameof(ProfileDisplayName));
        OnPropertyChanged(nameof(ProfileDescription));
        OnPropertyChanged(nameof(ProfilePros));
        OnPropertyChanged(nameof(ProfileCons));
        OnPropertyChanged(nameof(ProfileProsConsText));
        OnPropertyChanged(nameof(MountModeProsConsText));
        OnPropertyChanged(nameof(MountModeLabel));
        OnPropertyChanged(nameof(ProfileBadgeText));
        OnPropertyChanged(nameof(StatusText));
        OnPropertyChanged(nameof(DriveLabel));
        OnPropertyChanged(nameof(MountButtonText));
    }

    partial void OnDriveLetterChanged(string value)
    {
        OnPropertyChanged(nameof(DriveLabel));
        OnPropertyChanged(nameof(CanMount));
        NotifyChanged();
    }

    partial void OnIsHiddenDriveChanged(bool value) => NotifyChanged();

    partial void OnMountModeChanged(MountMode value)
    {
        OnPropertyChanged(nameof(IsPhysicalDisk));
        OnPropertyChanged(nameof(CanMountHidden));
        OnPropertyChanged(nameof(MountModeProsConsText));
        OnPropertyChanged(nameof(MountModeLabel));

        // La directiva NoDrives solo oculta de forma fiable unidades montadas
        // como disco fisico local. Si el usuario cambia a unidad de red,
        // desmarcamos "Montar como Oculto" para no dejar un estado
        // inconsistente (el check quedara deshabilitado en la vista).
        if (value == MountMode.NetworkDrive && IsHiddenDrive)
        {
            IsHiddenDrive = false;
        }

        NotifyChanged();
    }

    partial void OnProfileTypeChanged(MountProfileType value)
    {
        OnPropertyChanged(nameof(ProfileDisplayName));
        OnPropertyChanged(nameof(ProfileDescription));
        OnPropertyChanged(nameof(ProfilePros));
        OnPropertyChanged(nameof(ProfileCons));
        OnPropertyChanged(nameof(ProfileProsConsText));
        OnPropertyChanged(nameof(IsReadOnlyProfile));
        OnPropertyChanged(nameof(UsesDiskCache));
        OnPropertyChanged(nameof(ProfileBadgeText));
        OnPropertyChanged(nameof(ProfileBadgeBackground));
        OnPropertyChanged(nameof(ProfileBadgeBorder));
        OnPropertyChanged(nameof(ProfileBadgeForeground));
        NotifyChanged();
    }

    partial void OnIsEnabledChanged(bool value)
    {
        OnPropertyChanged(nameof(CanMount));
        NotifyChanged();
    }

    partial void OnCustomArgumentsChanged(string value) => NotifyChanged();

    // FASE 1: cualquier cambio en los parametros VFS debe persistirse.
    //
    // BUGFIX Persistencia VFS (v2): ademas de notificar al dashboard (que
    // refresca contadores y programa un guardado diferido), estos tres setters
    // disparan un guardado INMEDIATO a disco. Asi el valor queda escrito en
    // appsettings.json en el mismo instante en que el usuario lo cambia, sin
    // depender del debounce ni de un cierre ordenado de la aplicacion.
    partial void OnVfsCacheModeChanged(string value)
    {
        NotifyChanged();
        PersistNow();
    }

    partial void OnBufferSizeChanged(string value)
    {
        NotifyChanged();
        PersistNow();
    }

    partial void OnDirCacheTimeChanged(string value)
    {
        NotifyChanged();
        PersistNow();
    }

    /// <summary>
    /// BUGFIX Persistencia VFS (v2): copia el estado actual de la tarjeta al
    /// modelo y solicita al dashboard una escritura inmediata en disco.
    /// Cualquier fallo de E/S se captura y se registra en el log de depuracion
    /// para que nunca tumbe la UI.
    /// </summary>
    private void PersistNow()
    {
        try
        {
            ApplyToModel();
            _onPersistNow?.Invoke(this);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine(
                $"RemoteCardViewModel.PersistNow failed for '{Name}': {ex.Message}");
        }
    }

    /// <summary>
    /// BUGFIX Monitor de Trafico: si el puerto RC se asigna (o cambia) DESPUES
    /// de que la unidad ya este montada, re-armamos el bucle de telemetria.
    /// Cubre el caso de montajes restaurados al arrancar la app, donde el
    /// estado puede llegar antes que el puerto.
    /// </summary>
    partial void OnRcPortChanged(int value)
    {
        if (value > 0 && State == MountState.Mounted)
        {
            StartTelemetry();
        }
    }

    partial void OnStateChanged(MountState value)
    {
        OnPropertyChanged(nameof(StatusColor));
        OnPropertyChanged(nameof(StatusText));
        OnPropertyChanged(nameof(CanMount));
        OnPropertyChanged(nameof(CanUnmount));
        OnPropertyChanged(nameof(IsMounted));
        OnPropertyChanged(nameof(CanOpenInExplorer));
        OnPropertyChanged(nameof(MountButtonText));
        OnPropertyChanged(nameof(MountButtonBackground));
        OnPropertyChanged(nameof(MountButtonHoverBackground));
        OnPropertyChanged(nameof(MountButtonBorder));

        // Feature 1: la telemetria solo tiene sentido con la unidad montada.
        if (value == MountState.Mounted)
        {
            StartTelemetry();
        }
        else if (value is MountState.Stopped or MountState.Error)
        {
            StopTelemetry();
        }
    }

    /// <summary>
    /// Abre la raiz de la unidad montada en el Explorador de Windows.
    /// Solo tiene efecto cuando el estado es MONTADO.
    /// </summary>
    [RelayCommand]
    private void OpenInExplorer()
    {
        if (!CanOpenInExplorer)
        {
            return;
        }

        try
        {
            var letter = NormalizedLetter;
            Process.Start(new ProcessStartInfo("explorer.exe", $"{letter}:\\")
            {
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            StatusMessage = ex.Message;
        }
    }

    /// <summary>
    /// Obtiene la URL publica del remoto mediante <c>rclone link {remote}:</c>
    /// y la abre en el navegador predeterminado. Si el comando falla o no
    /// devuelve URL, abre la pagina principal de Google Drive como respaldo.
    /// </summary>
    [RelayCommand]
    private async Task OpenInBrowserAsync()
    {
        var url = await ResolveRemoteLinkAsync().ConfigureAwait(true);

        if (string.IsNullOrWhiteSpace(url))
        {
            // Respaldo: pagina principal de Google Drive.
            url = "https://drive.google.com/drive/u/0/my-drive";
        }

        try
        {
            Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            StatusMessage = ex.Message;
        }
    }

    /// <summary>
    /// Ejecuta <c>rclone link</c> para el remoto y devuelve la primera URL
    /// encontrada en la salida estandar, o null si no es posible.
    /// </summary>
    private async Task<string?> ResolveRemoteLinkAsync()
    {
        var rclonePath = _rcloneLocator?.ResolveRclonePath();

        if (string.IsNullOrWhiteSpace(rclonePath))
        {
            return null;
        }

        try
        {
            using var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = rclonePath,
                    Arguments = $"link \"{Name}:\"",
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                }
            };

            process.Start();

            var output = await process.StandardOutput.ReadToEndAsync().ConfigureAwait(true);
            await process.WaitForExitAsync().ConfigureAwait(true);

            return output
                .Split('\n', StringSplitOptions.RemoveEmptyEntries)
                .Select(line => line.Trim())
                .FirstOrDefault(line =>
                    line.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
                    line.StartsWith("https://", StringComparison.OrdinalIgnoreCase));
        }
        catch
        {
            return null;
        }
    }

    private void NotifyChanged() => _onChanged?.Invoke(this);
}
