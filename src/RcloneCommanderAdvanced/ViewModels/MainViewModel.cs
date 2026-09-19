using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using RcloneCommanderAdvanced.Helpers;
using RcloneCommanderAdvanced.Models;
using RcloneCommanderAdvanced.Services.Abstractions;

namespace RcloneCommanderAdvanced.ViewModels;

/// <summary>
/// ViewModel principal del dashboard.
///
/// Orquesta:
///  - La deteccion dinamica de remotos desde rclone.conf.
///  - El emparejamiento remoto -> letra de unidad -> perfil.
///  - Las acciones globales (Montar Todo / Desmontar Todo).
///  - El monitor de salud de los montajes.
///  - El modulo de mantenimiento del cache VFS.
/// </summary>
public sealed partial class MainViewModel : ObservableObject, IDisposable
{
    private readonly ISettingsService _settingsService;
    private readonly IRcloneConfigParser _configParser;
    private readonly IMountProcessManager _processManager;
    private readonly IDriveLetterService _driveLetterService;
    private readonly IVfsMaintenanceService _vfsMaintenance;
    private readonly IRcloneLocator _rcloneLocator;
    private readonly ISecurityService _securityService;
    private readonly IRcloneRcClient _rcClient;
    private readonly ILocalizationService _localization;
    private readonly IStartupShortcutService _startupShortcut;

    private readonly DispatcherTimer _healthTimer;

    /// <summary>
    /// Retardo (ms) antes de volcar los cambios de las tarjetas a disco.
    /// Evita escribir settings.json en cada pulsacion de tecla de los
    /// ComboBox editables (BufferSize / DirCacheTime).
    /// </summary>
    private const int PersistDebounceMs = 600;

    private readonly DispatcherTimer _persistDebounceTimer;
    private readonly SemaphoreSlim _operationLock = new(1, 1);

    private bool _disposed;

    public MainViewModel(
        ISettingsService settingsService,
        IRcloneConfigParser configParser,
        IMountProcessManager processManager,
        IDriveLetterService driveLetterService,
        IVfsMaintenanceService vfsMaintenance,
        IRcloneLocator rcloneLocator,
        ISecurityService securityService,
        IRcloneRcClient rcClient,
        ILocalizationService localization,
        IStartupShortcutService startupShortcut)
    {
        _settingsService = settingsService;
        _configParser = configParser;
        _processManager = processManager;
        _driveLetterService = driveLetterService;
        _vfsMaintenance = vfsMaintenance;
        _rcloneLocator = rcloneLocator;
        _securityService = securityService;
        _rcClient = rcClient;
        _localization = localization;
        _startupShortcut = startupShortcut;

        _processManager.StateChanged += OnMountStateChanged;
        _processManager.LogReceived += OnMountLogReceived;

        // Repintar los textos propios y de las tarjetas al cambiar de idioma.
        _localization.LanguageChanged += OnLanguageChanged;

        _healthTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(
                Math.Max(5, _settingsService.Current.HealthCheckIntervalSeconds))
        };
        _healthTimer.Tick += async (_, _) => await RefreshRuntimeStatesAsync().ConfigureAwait(true);

        // BUGFIX Persistencia VFS: temporizador de rebote para guardar los
        // ajustes de las tarjetas (VfsCacheMode / BufferSize / DirCacheTime)
        // poco despues de la ultima modificacion del usuario.
        _persistDebounceTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(PersistDebounceMs)
        };
        _persistDebounceTimer.Tick += OnPersistDebounceElapsed;

        // Feature 2: por defecto, sin limite de ancho de banda.
        SelectedBandwidthLimit = BandwidthLimits[0];
    }

    /// <summary>
    /// Atajo de traduccion: resuelve una clave en el idioma activo o devuelve
    /// el fallback indicado si no existe la clave.
    /// </summary>
    private string L(string key, string fallback) =>
        _localization.Get(key, fallback);

    /// <summary>
    /// Reacciona a un cambio de idioma: re-notifica los textos calculados del
    /// dashboard y propaga el refresco a cada tarjeta de remoto.
    /// </summary>
    private void OnLanguageChanged(object? sender, EventArgs e)
    {
        OnPropertyChanged(nameof(BandwidthLimits));
        OnPropertyChanged(nameof(StatusText));
        OnPropertyChanged(nameof(RcloneVersion));

        foreach (var card in Remotes)
        {
            card.RefreshLocalization();
        }

        // Re-localizar el mensaje de estado vacio (si esta visible).
        UpdateEmptyState();
    }

    /// <summary>Tarjetas de remotos renderizadas dinamicamente en el dashboard.</summary>
    public ObservableCollection<RemoteCardViewModel> Remotes { get; } = new();

    /// <summary>Letras de unidad disponibles para asignar.</summary>
    public ObservableCollection<string> AvailableDriveLetters { get; } = new();

    /// <summary>Perfiles de montaje disponibles.</summary>
    public IReadOnlyList<MountProfile> AvailableProfiles => MountProfile.Defaults;

    /// <summary>Log de actividad mostrado en el panel inferior.</summary>
    public ObservableCollection<string> ActivityLog { get; } = new();

    [ObservableProperty]
    private bool _isBusy;

    [ObservableProperty]
    private string _statusText = string.Empty;

    [ObservableProperty]
    private string _rcloneVersion = string.Empty;

    [ObservableProperty]
    private bool _isRcloneAvailable;

    [ObservableProperty]
    private bool _isWinFspInstalled;

    [ObservableProperty]
    private string _vfsCacheSize = "0 MB";

    [ObservableProperty]
    private string _vfsMetaSize = "0 MB";

    [ObservableProperty]
    private string _vfsTotalSize = "0 MB";

    [ObservableProperty]
    private int _mountedCount;

    [ObservableProperty]
    private int _totalEnabledCount;

    // ---------------------------------------------------------------------
    // Estado vacio (zero-leaks): se muestra cuando no hay remotos que listar,
    // ya sea porque rclone.conf no existe o porque no contiene remotos.
    // ---------------------------------------------------------------------

    /// <summary>True cuando el dashboard no tiene ninguna tarjeta que mostrar.</summary>
    [ObservableProperty]
    private bool _isEmptyState;

    /// <summary>Titulo del mensaje de estado vacio (resuelto contra el idioma activo).</summary>
    [ObservableProperty]
    private string _emptyStateTitle = string.Empty;

    /// <summary>Cuerpo del mensaje de estado vacio (resuelto contra el idioma activo).</summary>
    [ObservableProperty]
    private string _emptyStateMessage = string.Empty;

    // ---------------------------------------------------------------------
    // Feature 2: Limitador de ancho de banda en caliente (RC /core/bwlimit)
    // ---------------------------------------------------------------------

    /// <summary>
    /// Opciones del limitador de ancho de banda global. La etiqueta se resuelve
    /// dinamicamente contra el idioma activo (la tasa es un valor tecnico).
    /// </summary>
    public IReadOnlyList<BandwidthLimitOption> BandwidthLimits => new List<BandwidthLimitOption>
    {
        new() { Label = L("Str_BandwidthUnlimited", "Unlimited"), Rate = "off" },
        new() { Label = "25 MB/s", Rate = "25M" },
        new() { Label = "10 MB/s", Rate = "10M" },
        new() { Label = "5 MB/s",  Rate = "5M"  },
        new() { Label = "1 MB/s",  Rate = "1M"  },
    };

    /// <summary>Opcion de limite seleccionada en la barra superior.</summary>
    [ObservableProperty]
    private BandwidthLimitOption? _selectedBandwidthLimit;

    /// <summary>
    /// Texto editable del ComboBox de ancho de banda. Permite al usuario
    /// escribir un valor personalizado (ej. "500K", "1.5M", "2G") ademas de
    /// elegir una de las opciones predefinidas.
    /// </summary>
    [ObservableProperty]
    private string _bandwidthText = string.Empty;

    /// <summary>
    /// Valida el formato de una tasa de ancho de banda de rclone:
    /// numero (entero o decimal) seguido opcionalmente de K, M o G
    /// (ej. "500K", "1.5M", "2G", "10M"). Tambien acepta "off"/"unlimited".
    /// </summary>
    private static readonly Regex BandwidthRateRegex =
        new(@"^\s*(?:(?<num>\d+(?:[.,]\d+)?)\s*(?<unit>[KMGkmg])?|off|unlimited)\s*$",
            RegexOptions.Compiled);

    /// <summary>
    /// Traduce el texto mostrado en la UI (etiquetas amigables como
    /// "5 MB/s", "500 KB/s", "1 GB/s" o "Unlimited") a la sintaxis tecnica
    /// que espera rclone ("5M", "500K", "1G", "off"). Tambien acepta ya la
    /// sintaxis tecnica y numeros sueltos (que se asumen en MB/s).
    /// </summary>
    private string SanitizeBandwidthInput(string? input)
    {
        if (string.IsNullOrWhiteSpace(input))
        {
            return string.Empty;
        }

        var value = input.Trim();

        // Etiqueta localizada de "sin limite" -> "off".
        var unlimitedLabel = L("Str_BandwidthUnlimited", "Unlimited");
        if (value.Equals(unlimitedLabel, StringComparison.OrdinalIgnoreCase) ||
            value.Equals("Unlimited", StringComparison.OrdinalIgnoreCase) ||
            value.Equals("off", StringComparison.OrdinalIgnoreCase))
        {
            return "off";
        }

        // Etiquetas amigables -> sufijo tecnico de rclone.
        value = value
            .Replace(" GB/s", "G", StringComparison.OrdinalIgnoreCase)
            .Replace(" MB/s", "M", StringComparison.OrdinalIgnoreCase)
            .Replace(" KB/s", "K", StringComparison.OrdinalIgnoreCase)
            .Replace("GB/s", "G", StringComparison.OrdinalIgnoreCase)
            .Replace("MB/s", "M", StringComparison.OrdinalIgnoreCase)
            .Replace("KB/s", "K", StringComparison.OrdinalIgnoreCase)
            .Replace(" ", string.Empty);

        return value;
    }

    /// <summary>
    /// Normaliza y valida el texto introducido por el usuario en el ComboBox
    /// editable. Devuelve la tasa lista para <c>/core/bwlimit</c> o
    /// <c>null</c> si el formato es invalido.
    /// </summary>
    private string? NormalizeBandwidthRate(string? input)
    {
        if (string.IsNullOrWhiteSpace(input))
        {
            return null;
        }

        // Traducir etiquetas amigables ("5 MB/s") a sintaxis rclone ("5M")
        // antes de validar, para que la UI pueda mostrar texto estetico sin
        // romper el validador ni el payload HTTP.
        input = SanitizeBandwidthInput(input);

        var match = BandwidthRateRegex.Match(input);
        if (!match.Success)
        {
            return null;
        }

        // "off" / "unlimited" -> sin limite.
        var raw = input.Trim();
        if (raw.Equals("off", StringComparison.OrdinalIgnoreCase) ||
            raw.Equals("unlimited", StringComparison.OrdinalIgnoreCase))
        {
            return "off";
        }

        var number = match.Groups["num"].Value.Replace(',', '.');
        var unit = match.Groups["unit"].Value.ToUpperInvariant();

        // Sin unidad explicita asumimos MB/s (comportamiento esperado por el
        // usuario): "8" -> "8M". Si el usuario escribe la unidad (K/M/G) se
        // respeta su eleccion.
        return string.IsNullOrEmpty(unit) ? number + "M" : number + unit;
    }

    /// <summary>
    /// Construye la etiqueta estetica que se muestra en el ComboBox editable a
    /// partir de una tasa ya normalizada en sintaxis rclone. Ejemplos:
    /// "8M" -> "8 MB/s", "500K" -> "500 KB/s", "1G" -> "1 GB/s",
    /// "off" -> etiqueta de "sin limite" localizada.
    /// </summary>
    private string BuildBandwidthDisplayLabel(string normalizedRate)
    {
        if (string.IsNullOrWhiteSpace(normalizedRate))
        {
            return string.Empty;
        }

        if (normalizedRate.Equals("off", StringComparison.OrdinalIgnoreCase))
        {
            return L("Str_BandwidthUnlimited", "Unlimited");
        }

        var match = BandwidthRateRegex.Match(normalizedRate);
        if (!match.Success)
        {
            return normalizedRate;
        }

        var number = match.Groups["num"].Value.Replace(',', '.');
        var unit = match.Groups["unit"].Value.ToUpperInvariant();

        return unit switch
        {
            "K" => $"{number} KB/s",
            "M" => $"{number} MB/s",
            "G" => $"{number} GB/s",
            _ => $"{number} MB/s",
        };
    }

    /// <summary>
    /// Se dispara al cambiar el texto del ComboBox editable: si el valor es
    /// valido, se aplica inmediatamente (equivale a pulsar Enter).
    /// </summary>
    partial void OnBandwidthTextChanged(string value)
    {
        // Evitar reentrada: ApplyCustomBandwidthAsync y la sincronizacion de
        // presets reescriben BandwidthText, lo que volveria a disparar este
        // manejador en cascada.
        if (_suppressBandwidthTextSync)
        {
            return;
        }

        // Solo aplicamos automaticamente si el texto coincide con una opcion
        // predefinida o es una tasa valida; en caso contrario esperamos a que
        // el usuario confirme (Enter / perder foco) para no inundar de errores.
        var normalized = NormalizeBandwidthRate(value);
        if (normalized is null)
        {
            return;
        }

        // Sincronizar la seleccion con la opcion predefinida si coincide.
        var preset = BandwidthLimits.FirstOrDefault(o =>
            string.Equals(o.Rate, normalized, StringComparison.OrdinalIgnoreCase));
        if (preset is not null && !ReferenceEquals(preset, SelectedBandwidthLimit))
        {
            _suppressBandwidthTextSync = true;
            try
            {
                SelectedBandwidthLimit = preset;
            }
            finally
            {
                _suppressBandwidthTextSync = false;
            }
        }
    }

    /// <summary>
    /// Bandera que evita la reentrada y que la sincronizacion de la opcion
    /// predefinida sobrescriba el texto libre escrito por el usuario.
    /// </summary>
    private bool _suppressBandwidthTextSync;

    /// <summary>
    /// Se dispara al cambiar la opcion predefinida seleccionada. Solo refleja
    /// la tasa en el texto editable cuando el cambio proviene de una seleccion
    /// real del desplegable (no de la sincronizacion interna), de modo que un
    /// valor personalizado escrito a mano (ej. "2.5M") permanece visible.
    /// </summary>
    partial void OnSelectedBandwidthLimitChanged(BandwidthLimitOption? value)
    {
        if (_suppressBandwidthTextSync || value is null)
        {
            return;
        }

        // Reflejar la etiqueta legible de la opcion elegida en el texto
        // editable (ej. "10 MB/s") en lugar de la tasa tecnica ("10M").
        BandwidthText = BuildBandwidthDisplayLabel(value.Rate);
    }

    /// <summary>
    /// Aplica el texto personalizado escrito en el ComboBox editable.
    /// Se invoca al pulsar Enter o al perder el foco.
    /// </summary>
    [RelayCommand]
    private async Task ApplyCustomBandwidthAsync()
    {
        var normalized = NormalizeBandwidthRate(BandwidthText);

        if (normalized is null)
        {
            StatusText = L("Str_StatusBandwidthInvalid",
                "Invalid bandwidth format. Use e.g. 500K, 1.5M, 2G.");
            AppendLog(L("Str_LogBandwidthInvalid",
                "Invalid bandwidth value. Use a number followed by K, M or G."));
            return;
        }

        // Etiqueta estetica para la UI (ej. "8M" -> "8 MB/s") y valor tecnico
        // para rclone (ej. "8M"). Un numero sin unidad se asume en MB/s.
        var displayLabel = BuildBandwidthDisplayLabel(normalized);

        // Reflejar la etiqueta legible en el texto del ComboBox sin re-disparar
        // la sincronizacion de presets (evita cascadas de reentrada).
        _suppressBandwidthTextSync = true;
        try
        {
            BandwidthText = displayLabel;
        }
        finally
        {
            _suppressBandwidthTextSync = false;
        }

        await ApplyBandwidthRateAsync(normalized, displayLabel).ConfigureAwait(true);
    }

    // ---------------------------------------------------------------------
    // Selector de idioma (i18n)
    // ---------------------------------------------------------------------

    /// <summary>Idiomas disponibles para el selector de la barra superior.</summary>
    public IReadOnlyList<LanguageOption> AvailableLanguages => _localization.AvailableLanguages;

    /// <summary>Idioma actualmente seleccionado en el ComboBox.</summary>
    [ObservableProperty]
    private LanguageOption? _selectedLanguage;

    /// <summary>
    /// Cambia el idioma de la interfaz en caliente y lo persiste en
    /// appsettings.json para que se restaure en el proximo arranque.
    /// </summary>
    partial void OnSelectedLanguageChanged(LanguageOption? value)
    {
        if (value is null || string.IsNullOrWhiteSpace(value.Code))
        {
            return;
        }

        if (string.Equals(
                value.Code,
                _localization.CurrentLanguageCode,
                StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        _localization.SetLanguage(value.Code);

        var settings = _settingsService.Current;
        settings.Language = value.Code;
        _ = _settingsService.SaveAsync(settings);
    }

    /// <summary>
    /// Sincroniza la seleccion del ComboBox con el idioma activo del servicio.
    /// Se invoca al inicializar el dashboard.
    /// </summary>
    private void SyncSelectedLanguage()
    {
        var current = _localization.CurrentLanguageCode;

        SelectedLanguage = AvailableLanguages.FirstOrDefault(o =>
            string.Equals(o.Code, current, StringComparison.OrdinalIgnoreCase))
            ?? AvailableLanguages.FirstOrDefault();
    }

    // ---------------------------------------------------------------------
    // FASE 2: arranque con Windows (modo segundo plano)
    // ---------------------------------------------------------------------

    /// <summary>
    /// FASE 2: refleja si la aplicacion arranca con Windows (minimizada en la
    /// bandeja). Al cambiarse, crea o elimina el acceso directo en la carpeta
    /// Startup del usuario y persiste la preferencia.
    /// </summary>
    [ObservableProperty]
    private bool _startWithWindows;

    partial void OnStartWithWindowsChanged(bool value)
    {
        // Evitar reaccionar durante la sincronizacion inicial del estado.
        if (_isSyncingStartWithWindows)
        {
            return;
        }

        var ok = value ? _startupShortcut.Enable() : _startupShortcut.Disable();

        if (!ok)
        {
            // Revertir el toggle si la operacion sobre el acceso directo fallo.
            _isSyncingStartWithWindows = true;
            StartWithWindows = !value;
            _isSyncingStartWithWindows = false;

            StatusText = L("Str_StatusStartupShortcutFailed",
                "Could not update the Windows startup shortcut.");
            return;
        }

        var settings = _settingsService.Current;
        settings.StartWithWindows = value;
        _ = _settingsService.SaveAsync(settings);

        StatusText = value
            ? L("Str_StatusStartupEnabled", "The app will start with Windows (minimized).")
            : L("Str_StatusStartupDisabled", "The app will no longer start with Windows.");
    }

    /// <summary>Guarda contra reentrada al sincronizar el estado inicial.</summary>
    private bool _isSyncingStartWithWindows;

    /// <summary>
    /// FASE 2: sincroniza el toggle con el estado real del acceso directo de
    /// arranque (fuente de verdad: el sistema de archivos, no la config).
    /// </summary>
    private void SyncStartWithWindows()
    {
        _isSyncingStartWithWindows = true;
        StartWithWindows = _startupShortcut.IsEnabled;
        _isSyncingStartWithWindows = false;
    }

    /// <summary>
    /// Aplica el limite de ancho de banda seleccionado a todas las unidades
    /// montadas mediante <c>POST /core/bwlimit</c>. No reinicia procesos.
    /// </summary>
    [RelayCommand]
    private async Task ApplyBandwidthLimitAsync()
    {
        var option = SelectedBandwidthLimit;
        if (option is null)
        {
            return;
        }

        // Mantener sincronizado el texto editable con la opcion elegida.
        BandwidthText = option.Rate;

        await ApplyBandwidthRateAsync(option.Rate, option.Label).ConfigureAwait(true);
    }

    /// <summary>
    /// Nucleo compartido del limitador: aplica una tasa concreta (predefinida o
    /// personalizada) a todas las unidades montadas via <c>POST /core/bwlimit</c>.
    /// </summary>
    /// <param name="rate">Tasa en sintaxis rclone (ej. "10M", "500K", "off").</param>
    /// <param name="displayLabel">Etiqueta legible para los mensajes de estado.</param>
    private async Task ApplyBandwidthRateAsync(string rate, string displayLabel)
    {
        var mounted = Remotes
            .Where(c => c.State == MountState.Mounted && c.RcPort > 0)
            .ToList();

        if (mounted.Count == 0)
        {
            StatusText = string.Format(
                L("Str_StatusBandwidthSaved", "Limit \"{0}\" saved (no mounted drives)."),
                displayLabel);
            AppendLog(string.Format(
                L("Str_LogBandwidthNoMounts", "Bandwidth -> {0} (no mounted drives)."),
                displayLabel));
            return;
        }

        var applied = 0;

        foreach (var card in mounted)
        {
            var ok = await _rcClient
                .SetBandwidthLimitAsync(card.RcPort, rate)
                .ConfigureAwait(true);

            if (ok)
            {
                applied++;
            }
        }

        StatusText = string.Format(
            L("Str_StatusBandwidthApplied", "Bandwidth limit: {0} ({1}/{2})."),
            displayLabel, applied, mounted.Count);
        AppendLog(string.Format(
            L("Str_LogBandwidthApplied", "Bandwidth -> {0} applied to {1}/{2} drive(s)."),
            displayLabel, applied, mounted.Count));
    }

    /// <summary>Porcentaje de uso del cache VFS respecto al limite de 5 GB.</summary>
    public double VfsUsagePercent
    {
        get
        {
            const double limitBytes = 5d * 1024d * 1024d * 1024d;
            var total = _vfsTotalBytes;
            return Math.Min(100d, (total / limitBytes) * 100d);
        }
    }

    private long _vfsTotalBytes;

    /// <summary>Se dispara cuando el usuario solicita cerrar la sesion (bloquear).</summary>
    public event EventHandler? LockRequested;

    /// <summary>
    /// Inicializacion completa del dashboard: detecta rclone, WinFsp,
    /// remotos y refresca el estado del cache.
    /// </summary>
    public async Task InitializeAsync()
    {
        IsBusy = true;
        StatusText = L("Str_StatusInitializing", "Initializing...");

        // Reflejar el idioma activo en el selector de la barra superior.
        SyncSelectedLanguage();

        // FASE 2: reflejar el estado real del arranque con Windows.
        SyncStartWithWindows();

        try
        {
            // 1. Limpiar procesos huerfanos de ejecuciones anteriores.
            var orphans = await _processManager.KillOrphanRcloneProcessesAsync().ConfigureAwait(true);
            if (orphans > 0)
            {
                AppendLog(string.Format(
                    L("Str_LogOrphansCleaned", "Cleaned {0} orphan rclone process(es)."),
                    orphans));
            }

            // 2. Detectar entorno.
            IsRcloneAvailable = _rcloneLocator.IsRcloneAvailable();
            IsWinFspInstalled = _rcloneLocator.IsWinFspInstalled();
            RcloneVersion = IsRcloneAvailable
                ? _rcloneLocator.GetRcloneVersion()
                : L("Str_NotDetected", "Not detected");

            if (!IsRcloneAvailable)
            {
                AppendLog(L("Str_LogWarnRcloneMissing",
                    "WARNING: rclone.exe not found. Configure the path in Settings."));
            }

            if (!IsWinFspInstalled)
            {
                AppendLog(L("Str_LogWarnWinFspMissing",
                    "WARNING: WinFsp not detected. Drive mounting will fail."));
            }

            // 3. Sincronizar remotos desde rclone.conf.
            await SyncRemotesAsync().ConfigureAwait(true);

            // 3b. Evaluar el estado vacio (rclone.conf ausente o sin remotos).
            UpdateEmptyState();

            // 4. Refrescar cache VFS.
            await RefreshVfsSizesAsync().ConfigureAwait(true);

            // 5. Arrancar el monitor de salud.
            _healthTimer.Start();

            StatusText = string.Format(
                L("Str_StatusReadyRemotes", "Ready. {0} remote(s) detected."),
                Remotes.Count);

            // 6. Auto-montaje si el usuario lo configuro.
            if (_settingsService.Current.AutoMountOnStartup)
            {
                await MountAllAsync().ConfigureAwait(true);
            }
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>
    /// Lee rclone.conf y fusiona los remotos detectados con la configuracion
    /// previa del usuario (letras, perfiles, flags de habilitado/ignorado).
    /// </summary>
    [RelayCommand]
    public async Task SyncRemotesAsync()
    {
        var detected = _configParser.Parse();
        var settings = _settingsService.Current;

        // Mapa de configuracion previa por nombre de remoto.
        var previous = settings.Remotes
            .ToDictionary(r => r.Name, StringComparer.OrdinalIgnoreCase);

        var merged = new List<RemoteEntry>();
        var availableLetters = new Queue<string>(_driveLetterService.GetAvailableDriveLetters());

        foreach (var remote in detected)
        {
            if (previous.TryGetValue(remote.Name, out var saved))
            {
                // Conservar la configuracion del usuario.
                remote.DriveLetter = saved.DriveLetter;
                remote.Profile = saved.Profile;
                remote.IsEnabled = saved.IsEnabled;
                remote.IsHiddenDrive = saved.IsHiddenDrive;
                remote.CustomArguments = saved.CustomArguments;

                // BUGFIX Persistencia VFS (v2): estos tres parametros tambien
                // deben restaurarse desde appsettings.json. Antes se omitian,
                // por lo que SyncRemotesAsync los sobrescribia con los valores
                // vacios que devuelve el parser de rclone.conf en cada arranque
                // y los ajustes VFS del usuario se perdian al reiniciar.
                remote.VfsCacheMode = saved.VfsCacheMode;
                remote.BufferSize = saved.BufferSize;
                remote.DirCacheTime = saved.DirCacheTime;
            }
            else
            {
                // Remoto nuevo: auto-asignar una letra libre si hay.
                remote.DriveLetter = availableLetters.Count > 0
                    ? availableLetters.Dequeue()
                    : string.Empty;

                remote.Profile = MountProfileType.SafeRead;
                remote.IsEnabled = false;
                remote.IsHiddenDrive = false;
            }

            merged.Add(remote);
        }

        settings.Remotes = merged;
        await _settingsService.SaveAsync(settings).ConfigureAwait(true);

        RebuildCards(merged);
        RefreshAvailableLetters();
        UpdateEmptyState();

        // PRIVACIDAD: nunca exponemos la ruta absoluta del disco en la UI.
        // Mostramos solo el nombre logico del archivo ("rclone.conf") para no
        // filtrar el nombre de usuario del sistema operativo.
        AppendLog(string.Format(
            L("Str_LogSyncedRemotes", "Synced {0} remote(s) from {1}."),
            merged.Count, "rclone.conf"));
    }

    /// <summary>
    /// Calcula el mensaje de estado vacio segun la situacion real:
    ///  - rclone.conf no existe  -> guia para crearlo con 'rclone config'.
    ///  - rclone.conf sin remotos -> aviso de que el archivo esta vacio.
    /// Se invoca tras sincronizar y al reconstruir las tarjetas.
    /// </summary>
    private void UpdateEmptyState()
    {
        if (Remotes.Count > 0)
        {
            IsEmptyState = false;
            EmptyStateTitle = string.Empty;
            EmptyStateMessage = string.Empty;
            return;
        }

        IsEmptyState = true;

        if (!_configParser.ConfigFileExists())
        {
            EmptyStateTitle = L("Str_EmptyNoConfigTitle", "rclone.conf not found");
            EmptyStateMessage = L(
                "Str_EmptyNoConfigMessage",
                "No rclone configuration file was found. Create your remotes with 'rclone config' and then press SYNC REMOTES.");
        }
        else
        {
            EmptyStateTitle = L("Str_EmptyNoRemotesTitle", "No remotes detected");
            EmptyStateMessage = L(
                "Str_EmptyNoRemotesMessage",
                "The rclone.conf file exists but contains no remotes. Add one with 'rclone config' and press SYNC REMOTES.");
        }
    }

    /// <summary>
    /// Permite al usuario seleccionar manualmente su archivo rclone.conf
    /// (util para instalaciones portables donde el archivo no esta en
    /// %APPDATA%\rclone). Guarda la ruta elegida en appsettings.json para
    /// recordarla en futuros inicios y fuerza una recarga inmediata.
    /// </summary>
    [RelayCommand]
    private async Task LocateConfigFileAsync()
    {
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Title = L("Str_LocateConfigDialogTitle", "Select your rclone.conf file"),
            Filter = L("Str_LocateConfigFilter",
                "Config files (*.conf)|*.conf|All files (*.*)|*.*"),
            CheckFileExists = true,
            Multiselect = false
        };

        // Sugerir la carpeta de la ruta actual si existe.
        try
        {
            var current = _configParser.ResolvedConfigPath;
            var currentDir = Path.GetDirectoryName(current);
            if (!string.IsNullOrWhiteSpace(currentDir) && Directory.Exists(currentDir))
            {
                dialog.InitialDirectory = currentDir;
            }
        }
        catch
        {
            // La sugerencia de carpeta es best-effort: nunca debe romper el flujo.
        }

        // El OpenFileDialog debe abrirse en el hilo de UI.
        var accepted = dialog.ShowDialog() == true;
        if (!accepted || string.IsNullOrWhiteSpace(dialog.FileName))
        {
            return;
        }

        var selectedPath = dialog.FileName;

        // 1. Persistir la ruta personalizada para futuros inicios.
        var settings = _settingsService.Current;
        settings.RcloneConfigPath = selectedPath;
        await _settingsService.SaveAsync(settings).ConfigureAwait(true);

        // PRIVACIDAD: no registramos la ruta absoluta elegida por el usuario.
        // El log solo confirma que se guardo una ruta personalizada.
        AppendLog(L("Str_LogConfigPathSaved",
            "Custom rclone.conf path saved."));

        // 2. Forzar una recarga inmediata de los remotos y las tarjetas.
        await SyncRemotesAsync().ConfigureAwait(true);

        // 3. Ocultar el estado vacio si la lectura fue exitosa.
        UpdateEmptyState();

        if (Remotes.Count > 0)
        {
            StatusText = string.Format(
                L("Str_StatusReadyRemotes", "Ready. {0} remote(s) detected."),
                Remotes.Count);
        }
        else
        {
            AppendLog(L("Str_LogConfigNoRemotes",
                "The selected file does not contain any remotes."));
        }
    }

    /// <summary>Reconstruye la coleccion observable de tarjetas.</summary>
    private void RebuildCards(IEnumerable<RemoteEntry> entries)
    {
        Remotes.Clear();

        foreach (var entry in entries)
        {
            var card = new RemoteCardViewModel(
                entry, OnCardChanged, _rcloneLocator, _rcClient, _localization,
                OnCardPersistNow);

            // FEATURE Monitor de Trafico: la tarjeta solo notifica; la ventana
            // grafica se crea aqui, en la capa de vista del dashboard.
            card.TrafficMonitorRequested += OnTrafficMonitorRequested;

            var info = _processManager.GetInfo(entry.Name);
            if (info is not null)
            {
                card.UpdateRuntime(info);
            }

            Remotes.Add(card);
        }

        UpdateCounters();
    }

    /// <summary>Refresca las letras de unidad disponibles en el ComboBox.</summary>
    public void RefreshAvailableLetters()
    {
        AvailableDriveLetters.Clear();

        foreach (var letter in _driveLetterService.GetAvailableDriveLetters())
        {
            AvailableDriveLetters.Add(letter);
        }

        // Incluir tambien las letras ya asignadas para que no desaparezcan del combo.
        foreach (var card in Remotes)
        {
            if (!string.IsNullOrWhiteSpace(card.DriveLetter) &&
                !AvailableDriveLetters.Contains(card.DriveLetter))
            {
                AvailableDriveLetters.Add(card.DriveLetter);
            }
        }
    }

    /// <summary>Monta todos los remotos habilitados.</summary>
    [RelayCommand]
    private async Task MountAllAsync()
    {
        if (!await _operationLock.WaitAsync(0).ConfigureAwait(true))
        {
            return;
        }

        IsBusy = true;
        StatusText = L("Str_StatusMounting", "Mounting drives...");

        try
        {
            PersistCardChanges();

            var targets = Remotes
                .Where(c => c.IsEnabled)
                .Select(c => c.Model)
                .ToList();

            if (targets.Count == 0)
            {
                StatusText = L("Str_StatusNoEnabledRemotes",
                    "There are no enabled remotes to mount.");
                AppendLog(L("Str_LogMountAllNoRemotes",
                    "Mount All: no enabled remotes."));
                return;
            }

            var mounted = await _processManager.MountAllAsync(targets).ConfigureAwait(true);

            // Aplicar/actualizar la ocultacion de letras de unidad solicitada.
            ApplyHiddenDrivePolicy(targets);

            StatusText = string.Format(
                L("Str_StatusMountCompleted", "Mount completed: {0}/{1} drive(s)."),
                mounted, targets.Count);
            AppendLog(string.Format(
                L("Str_LogMountAllResult", "Mount All -> {0}/{1} drive(s) mounted."),
                mounted, targets.Count));

            await RefreshRuntimeStatesAsync().ConfigureAwait(true);
        }
        finally
        {
            IsBusy = false;
            _operationLock.Release();
        }
    }

    /// <summary>Desmonta todos los montajes activos (Kill Tree global).</summary>
    [RelayCommand]
    private async Task UnmountAllAsync()
    {
        if (!await _operationLock.WaitAsync(0).ConfigureAwait(true))
        {
            return;
        }

        IsBusy = true;
        StatusText = L("Str_StatusUnmounting", "Unmounting drives...");

        try
        {
            // Restaurar la visibilidad de todas las letras ocultas antes de desmontar.
            foreach (var card in Remotes.Where(c => c.IsHiddenDrive))
            {
                RegistryHelper.ShowDrive(card.DriveLetter);
            }

            var count = await _processManager.UnmountAllAsync().ConfigureAwait(true);

            StatusText = string.Format(
                L("Str_StatusUnmountCompleted", "Unmount completed: {0} drive(s)."),
                count);
            AppendLog(string.Format(
                L("Str_LogUnmountAllResult", "Unmount All -> {0} drive(s) unmounted."),
                count));

            await RefreshRuntimeStatesAsync().ConfigureAwait(true);
        }
        finally
        {
            IsBusy = false;
            _operationLock.Release();
        }
    }

    /// <summary>Monta o desmonta una tarjeta concreta.</summary>
    [RelayCommand]
    private async Task ToggleMountAsync(RemoteCardViewModel? card)
    {
        if (card is null)
        {
            return;
        }

        if (card.State is MountState.Mounted or MountState.Starting)
        {
            await _processManager.UnmountAsync(card.Name).ConfigureAwait(true);

            // Al desmontar, restaurar la visibilidad de la letra si estaba oculta.
            if (card.IsHiddenDrive)
            {
                RegistryHelper.ShowDrive(card.DriveLetter);
                AppendLog(string.Format(
                    L("Str_LogLetterRestored", "[{0}] Letter {1}: restored in Explorer."),
                    card.Name, card.DriveLetter));
            }
        }
        else
        {
            card.ApplyToModel();
            await _settingsService.SaveAsync().ConfigureAwait(true);

            var ok = await _processManager.MountAsync(card.Model).ConfigureAwait(true);

            // Solo ocultamos la letra si el montaje fue exitoso.
            if (ok && card.IsHiddenDrive)
            {
                RegistryHelper.HideDrive(card.DriveLetter);
                AppendLog(string.Format(
                    L("Str_LogLetterHidden", "[{0}] Letter {1}: hidden in Explorer."),
                    card.Name, card.DriveLetter));
            }
        }

        await RefreshRuntimeStatesAsync().ConfigureAwait(true);
    }

    /// <summary>
    /// Abre el Explorador de Windows en la carpeta de cache de rclone
    /// (%LOCALAPPDATA%\rclone). La crea si no existe para evitar excepciones.
    /// </summary>
    [RelayCommand]
    private void OpenCacheFolder()
    {
        try
        {
            var cachePath = Environment.ExpandEnvironmentVariables(@"%LOCALAPPDATA%\rclone");

            if (!Directory.Exists(cachePath))
            {
                Directory.CreateDirectory(cachePath);
                AppendLog(string.Format(
                    L("Str_LogCacheFolderCreated", "Cache folder created: {0}"),
                    cachePath));
            }

            Process.Start(new ProcessStartInfo("explorer.exe", cachePath)
            {
                UseShellExecute = true
            });

            StatusText = L("Str_StatusCacheFolderOpened",
                "Cache folder opened in Explorer.");
        }
        catch (Exception ex)
        {
            StatusText = string.Format(
                L("Str_ErrorOpenCacheFolder", "Could not open the cache folder: {0}"),
                ex.Message);
            AppendLog(string.Format(
                L("Str_LogErrorOpenCacheFolder", "ERROR opening the cache folder: {0}"),
                ex.Message));
        }
    }

    /// <summary>
    /// Aplica la politica de unidad oculta (NoDrives) a los remotos indicados
    /// que tengan la opcion "Montar como Oculto" activada.
    /// </summary>
    private void ApplyHiddenDrivePolicy(IEnumerable<RemoteEntry> targets)
    {
        foreach (var entry in targets.Where(e => e.IsHiddenDrive))
        {
            RegistryHelper.HideDrive(entry.DriveLetter);
        }
    }

    /// <summary>
    /// Feature 3: abre la consola de diagnostico de una unidad concreta,
    /// mostrando su salida de rclone (stdout/stderr) en tiempo real.
    /// </summary>
    [RelayCommand]
    private void OpenLogs(RemoteCardViewModel? card)
    {
        if (card is null)
        {
            return;
        }

        var window = new Views.LogViewerWindow(card)
        {
            Owner = System.Windows.Application.Current?.MainWindow
        };

        window.Show();
    }

    /// <summary>
    /// FEATURE Monitor de Trafico: abre la ventana grafica de velocidad en
    /// tiempo real para la unidad que lo solicito. La tarjeta solo dispara el
    /// evento; la creacion de la ventana vive aqui (capa de vista).
    /// </summary>
    private void OnTrafficMonitorRequested(object? sender, EventArgs e)
    {
        if (sender is not RemoteCardViewModel card)
        {
            return;
        }

        // BUGFIX Monitor de Trafico: re-armamos el sondeo por si el bucle no
        // llego a arrancar (p. ej. el puerto RC se asigno despues del cambio
        // de estado). EnsureTelemetry es idempotente y no hace nada si ya
        // esta corriendo.
        card.EnsureTelemetry();

        var viewModel = new TrafficMonitorViewModel(card, _localization);

        var window = new Views.TrafficMonitorWindow(viewModel)
        {
            Owner = System.Windows.Application.Current?.MainWindow
        };

        window.Show();
    }

    /// <summary>Purga fisicamente el cache VFS del SSD.</summary>
    [RelayCommand]
    private async Task PurgeVfsAsync()
    {
        IsBusy = true;
        StatusText = L("Str_StatusPurgingVfs", "Purging VFS cache...");

        try
        {
            var result = await _vfsMaintenance.PurgeAsync().ConfigureAwait(true);

            AppendLog(result.Message);
            StatusText = result.Message;

            await RefreshVfsSizesAsync().ConfigureAwait(true);
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>Recalcula los tamanos del cache VFS.</summary>
    [RelayCommand]
    private async Task RefreshVfsSizesAsync()
    {
        var cacheBytes = await _vfsMaintenance.GetVfsCacheSizeBytesAsync().ConfigureAwait(true);
        var metaBytes = await _vfsMaintenance.GetVfsMetaSizeBytesAsync().ConfigureAwait(true);

        _vfsTotalBytes = cacheBytes + metaBytes;

        VfsCacheSize = _vfsMaintenance.FormatSize(cacheBytes);
        VfsMetaSize = _vfsMaintenance.FormatSize(metaBytes);
        VfsTotalSize = _vfsMaintenance.FormatSize(_vfsTotalBytes);

        OnPropertyChanged(nameof(VfsUsagePercent));
    }

    /// <summary>Guarda los cambios de las tarjetas en appsettings.json.</summary>
    [RelayCommand]
    private async Task SaveConfigurationAsync()
    {
        PersistCardChanges();
        await _settingsService.SaveAsync().ConfigureAwait(true);

        StatusText = L("Str_StatusConfigSaved", "Configuration saved.");
        AppendLog(L("Str_LogConfigSaved",
            "Configuration saved to appsettings.json."));
    }

    /// <summary>Bloquea la sesion actual.</summary>
    [RelayCommand]
    private void LockSession()
    {
        _securityService.Lock();
        _healthTimer.Stop();
        LockRequested?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Reanuda el monitor de salud tras desbloquear.</summary>
    public void ResumeAfterUnlock()
    {
        if (!_healthTimer.IsEnabled)
        {
            _healthTimer.Start();
        }
    }

    private void PersistCardChanges()
    {
        var settings = _settingsService.Current;

        foreach (var card in Remotes)
        {
            card.ApplyToModel();
        }

        settings.Remotes = Remotes.Select(c => c.Model).ToList();
    }

    /// <summary>
    /// BUGFIX Persistencia VFS (v2): guardado INMEDIATO solicitado por la
    /// tarjeta cuando el usuario cambia VfsCacheMode, BufferSize o
    /// DirCacheTime. Copia el estado de todas las tarjetas al modelo y escribe
    /// appsettings.json en disco de forma sincrona (fire-and-forget seguro).
    /// </summary>
    private void OnCardPersistNow(RemoteCardViewModel card)
    {
        try
        {
            PersistCardChanges();
            _ = _settingsService.SaveAsync();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine(
                $"OnCardPersistNow failed for '{card.Name}': {ex.Message}");
        }
    }

    private void OnCardChanged(RemoteCardViewModel card)
    {
        UpdateCounters();

        // BUGFIX Persistencia VFS: ApplyToModel() copia VfsCacheMode,
        // BufferSize y DirCacheTime desde el ViewModel hacia el modelo
        // RemoteEntry, pero hasta ahora NADIE escribia ese modelo en disco.
        // OnCardChanged solo refrescaba los contadores, por lo que al cerrar
        // y reabrir la app los ajustes VFS volvian a sus valores por defecto.
        // Persistimos de forma diferida (debounce) para no saturar el disco
        // mientras el usuario teclea en los ComboBox editables.
        SchedulePersist();
    }

    /// <summary>
    /// Programa una escritura de ajustes con retardo. Cada llamada reinicia
    /// el temporizador, de modo que solo se guarda una vez que el usuario
    /// deja de modificar controles durante <see cref="PersistDebounceMs"/>.
    /// </summary>
    private void SchedulePersist()
    {
        _persistDebounceTimer.Stop();
        _persistDebounceTimer.Start();
    }

    private async void OnPersistDebounceElapsed(object? sender, EventArgs e)
    {
        _persistDebounceTimer.Stop();

        try
        {
            PersistCardChanges();
            await _settingsService.SaveAsync().ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            // La persistencia nunca debe tumbar la UI.
            System.Diagnostics.Debug.WriteLine($"PersistCardChanges failed: {ex.Message}");
        }
    }

    private async Task RefreshRuntimeStatesAsync()
    {
        foreach (var card in Remotes)
        {
            var info = _processManager.GetInfo(card.Name);

            if (info is not null)
            {
                card.UpdateRuntime(info);

                // BUGFIX Monitor de Trafico: re-armamos el sondeo en cada
                // refresco. Si la unidad ya estaba montada al arrancar la app
                // (auto-montaje), UpdateRuntime vuelve a asignar State = Mounted
                // con el MISMO valor, y SetProperty cortocircuita la asignacion
                // sin disparar OnStateChanged; por tanto StartTelemetry() nunca
                // se invocaba y la grafica quedaba congelada en "0 B/s".
                // EnsureTelemetry es idempotente: no hace nada si el bucle ya
                // esta corriendo.
                card.EnsureTelemetry();
            }
            else if (card.State is MountState.Mounted or MountState.Starting)
            {
                // El proceso desaparecio: la unidad se cayo.
                card.State = MountState.Stopped;
                card.StatusMessage = L("Str_Stopped", "Stopped");
            }
        }

        UpdateCounters();
        await Task.CompletedTask.ConfigureAwait(true);
    }

    private void UpdateCounters()
    {
        MountedCount = Remotes.Count(c => c.State == MountState.Mounted);
        TotalEnabledCount = Remotes.Count(c => c.IsEnabled);
    }

    private void OnMountStateChanged(object? sender, MountStateChangedEventArgs e)
    {
        // El evento puede llegar desde un hilo de fondo: marshalizamos a la UI.
        var dispatcher = System.Windows.Application.Current?.Dispatcher;

        if (dispatcher is null || dispatcher.CheckAccess())
        {
            ApplyStateChange(e);
        }
        else
        {
            dispatcher.Invoke(() => ApplyStateChange(e));
        }
    }

    private void ApplyStateChange(MountStateChangedEventArgs e)
    {
        var card = Remotes.FirstOrDefault(c =>
            string.Equals(c.Name, e.RemoteName, StringComparison.OrdinalIgnoreCase));

        if (card is null)
        {
            return;
        }

        card.State = e.State;

        if (!string.IsNullOrWhiteSpace(e.Message))
        {
            card.StatusMessage = e.Message;
            AppendLog($"[{e.RemoteName}] {e.Message}");
        }

        UpdateCounters();
    }

    private void OnMountLogReceived(object? sender, MountLogEventArgs e)
    {
        var dispatcher = System.Windows.Application.Current?.Dispatcher;

        if (dispatcher is null || dispatcher.CheckAccess())
        {
            RouteLog(e);
        }
        else
        {
            dispatcher.Invoke(() => RouteLog(e));
        }
    }

    /// <summary>
    /// Enruta una linea de log al buffer de la tarjeta correspondiente
    /// (Feature 3) y, ademas, al log de actividad global.
    /// </summary>
    private void RouteLog(MountLogEventArgs e)
    {
        var card = Remotes.FirstOrDefault(c =>
            string.Equals(c.Name, e.RemoteName, StringComparison.OrdinalIgnoreCase));

        card?.AppendLog(e.Line);

        AppendLog($"[{e.RemoteName}] {e.Line}");
    }

    private void AppendLog(string message)
    {
        var entry = $"{DateTime.Now:HH:mm:ss}  {message}";
        ActivityLog.Insert(0, entry);

        // Limitar el log en memoria a 300 entradas.
        while (ActivityLog.Count > 300)
        {
            ActivityLog.RemoveAt(ActivityLog.Count - 1);
        }
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        _healthTimer.Stop();
        _processManager.StateChanged -= OnMountStateChanged;
        _processManager.LogReceived -= OnMountLogReceived;

        // Detener los bucles de telemetria de cada tarjeta (libera red).
        foreach (var card in Remotes)
        {
            card.Dispose();
        }

        _operationLock.Dispose();
    }
}

/// <summary>
/// Opcion del limitador de ancho de banda global. <see cref="Rate"/> usa la
/// sintaxis de rclone ("off", "1M", "10M"...).
/// </summary>
public sealed class BandwidthLimitOption
{
    /// <summary>Texto mostrado en el ComboBox.</summary>
    public string Label { get; init; } = string.Empty;

    /// <summary>Valor enviado a <c>/core/bwlimit</c>.</summary>
    public string Rate { get; init; } = string.Empty;

    /// <summary>
    /// Devuelve la etiqueta legible. Es imprescindible para que un ComboBox
    /// editable (IsEditable=True) muestre el texto correcto al seleccionar un
    /// objeto complejo: WPF usa ToString() para rellenar el TextBox interno
    /// cuando no hay DisplayMemberPath ni TextSearch.TextPath. Sin esta
    /// sobrecarga el control imprimiria el nombre de la clase.
    /// </summary>
    public override string ToString() => Label;
}
