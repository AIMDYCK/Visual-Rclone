using System;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;
using Microsoft.Extensions.DependencyInjection;
using RcloneCommanderAdvanced.Services;
using RcloneCommanderAdvanced.Services.Abstractions;
using RcloneCommanderAdvanced.ViewModels;
using RcloneCommanderAdvanced.Views;

namespace RcloneCommanderAdvanced;

/// <summary>
/// Punto de entrada de la aplicacion.
///
/// Flujo de arranque:
///  1. Construir el contenedor de inyeccion de dependencias.
///  2. Cargar appsettings.json.
///  3. Si no hay PIN configurado -> SetupWindow (First-Run Setup).
///  4. Si hay PIN -> LockWindow (la app arranca bloqueada).
///  5. Tras desbloquear -> MainWindow (dashboard).
///
/// IMPORTANTE: la carga de configuracion y la apertura de la primera ventana
/// se difieren con Dispatcher.BeginInvoke. Mostrar un ShowDialog() dentro de
/// OnStartup (y mas aun despues de un await) deja el bucle de mensajes en un
/// estado fragil: la ventana se pinta pero los comandos no responden.
///
/// Al cerrar se garantiza la limpieza de todos los procesos de rclone.
/// </summary>
public partial class App : Application
{
    private ServiceProvider? _serviceProvider;
    private MainWindow? _mainWindow;
    private ITrayIconService? _trayIcon;

    /// <summary>
    /// FASE 2: true cuando la app se lanzo desde el acceso directo de arranque
    /// con el argumento --minimized. En ese caso arranca directamente en la
    /// bandeja del sistema, sin mostrar la ventana principal.
    /// </summary>
    private bool _startMinimized;

    /// <summary>
    /// FASE 5: evita que la limpieza global de cierre (desmontar todo + matar
    /// huerfanos) se ejecute mas de una vez si el usuario pulsa la X varias
    /// veces o si coincide con el cierre desde la bandeja.
    /// </summary>
    private bool _isExiting;

    /// <summary>
    /// Contenedor de dependencias de la aplicacion. Expuesto para que las
    /// vistas puedan resolver ventanas modales (p. ej. el editor de cuentas)
    /// sin acoplarse a la construccion manual de servicios.
    /// </summary>
    public IServiceProvider Services =>
        _serviceProvider ?? throw new InvalidOperationException("El contenedor de servicios no esta inicializado.");

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // FASE 2: detectamos el argumento --minimized que introduce el acceso
        // directo de arranque con Windows. En ese caso la app arranca en la
        // bandeja del sistema sin mostrar la ventana principal.
        _startMinimized = e.Args.Any(arg =>
            string.Equals(arg, "--minimized", StringComparison.OrdinalIgnoreCase));

        // Red de seguridad: cualquier excepcion no controlada se muestra en
        // lugar de morir en silencio (era la causa de que el boton "no hiciera
        // nada": la excepcion se tragaba y la ventana quedaba congelada).
        DispatcherUnhandledException += OnDispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += OnDomainUnhandledException;
        TaskScheduler.UnobservedTaskException += OnUnobservedTaskException;

        _serviceProvider = BuildServiceProvider();

        // Diferimos el arranque hasta que el bucle de mensajes este activo.
        // Sin esto, ShowDialog() dentro de OnStartup no bombea mensajes
        // correctamente y los botones de la ventana no responden.
        Dispatcher.BeginInvoke(
            new Action(async () => await StartAsync().ConfigureAwait(true)),
            DispatcherPriority.ApplicationIdle);
    }

    /// <summary>
    /// Carga la configuracion y decide la pantalla inicial. Se ejecuta ya con
    /// el bucle de mensajes de WPF en marcha.
    /// </summary>
    private async Task StartAsync()
    {
        if (_serviceProvider is null)
        {
            Shutdown();
            return;
        }

        try
        {
            var settingsService = _serviceProvider.GetRequiredService<ISettingsService>();
            await settingsService.LoadAsync().ConfigureAwait(true);

            // Aplicar el idioma guardado (o el fallback en-US) antes de abrir
            // cualquier ventana, para que todos los textos ya salgan traducidos.
            var localization = _serviceProvider.GetRequiredService<ILocalizationService>();
            localization.SetLanguage(settingsService.Current.Language);

            // BOOTSTRAP DE PRIMER ARRANQUE: en un entorno limpio (Windows
            // Sandbox, PC nuevo) la carpeta %APPDATA%\rclone y el archivo
            // rclone.conf NO existen. Los garantizamos ANTES de que cualquier
            // servicio intente leerlos, para que el arranque nunca falle por
            // ausencia de configuracion. Es idempotente y no destructivo.
            var bootstrapper = _serviceProvider.GetRequiredService<IRcloneConfigBootstrapper>();
            var bootstrap = bootstrapper.EnsureInitialized();

            if (!bootstrap.Success)
            {
                // No abortamos: la app puede seguir en modo degradado (estado
                // vacio). Registramos el motivo para diagnostico.
                Debug.WriteLine(
                    $"[Bootstrap] No se pudo preparar el entorno de rclone: {bootstrap.ErrorMessage}");
            }
            else if (!bootstrap.AlreadyInitialized)
            {
                // PRIVACIDAD: no volcamos la ruta absoluta en el log de
                // diagnostico; basta con saber si el directorio y el archivo
                // se crearon en este arranque.
                Debug.WriteLine(
                    $"[Bootstrap] Entorno preparado. Dir creado: {bootstrap.DirectoryCreated}, " +
                    $"Archivo creado: {bootstrap.FileCreated}");
            }

            // FASE 1: comprobacion de dependencias (pre-flight). Si falta
            // rclone.exe o WinFsp, detenemos el arranque normal y mostramos la
            // ventana de aviso. El usuario puede reintentar tras instalarlas.
            if (!await EnsureDependenciesAsync().ConfigureAwait(true))
            {
                Shutdown();
                return;
            }

            // FASE 2: inicializamos el icono de la bandeja del sistema. La app
            // vive en segundo plano: cerrar la ventana la oculta, no la mata.
            InitializeTrayIcon();

            // FASE 4: si el rclone.conf esta cifrado, solicitamos la contrasena
            // antes de cargar los remotos. Sin ella, rclone no puede leer la
            // configuracion y la app arrancaria sin unidades.
            if (!EnsureConfigUnlocked())
            {
                Shutdown();
                return;
            }

            var securityService = _serviceProvider.GetRequiredService<ISecurityService>();

            if (!securityService.IsPinConfigured)
            {
                ShowSetupScreen();
            }
            else if (securityService.IsDeviceTrusted)
            {
                // El usuario marco "confiar en este dispositivo" y el periodo
                // sigue vigente: arrancamos directamente en el dashboard.
                securityService.UnlockTrustedDevice();
                ShowMainWindow();
            }
            else if (_startMinimized)
            {
                // FASE 2: arranque silencioso con Windows. No mostramos la
                // pantalla de bloqueo (seria intrusivo al iniciar sesion);
                // la app queda en la bandeja hasta que el usuario la abra.
                ShowMainWindowMinimized();
            }
            else
            {
                ShowLockScreen();
            }
        }
        catch (Exception ex)
        {
            ShowFatalError("Str_FatalErrorStartup", "Error starting the application", ex);
        }
    }

    /// <summary>
    /// FASE 1: verifica que rclone.exe y WinFsp esten disponibles antes de
    /// continuar con el arranque normal. Si falta alguno, interrumpe la carga
    /// del dashboard y muestra la ventana modal <see cref="FirstRunSetupWindow"/>,
    /// que ofrece una instalacion asistida de 1 clic (descarga el zip oficial
    /// de rclone y lo coloca en el directorio interno de la app) ademas del
    /// enlace oficial de WinFsp.
    /// </summary>
    /// <returns>true si todas las dependencias estan presentes.</returns>
    private async Task<bool> EnsureDependenciesAsync()
    {
        if (_serviceProvider is null)
        {
            return false;
        }

        var locator = _serviceProvider.GetRequiredService<IRcloneLocator>();

        while (true)
        {
            var rcloneMissing = !locator.IsRcloneAvailable();
            var winFspMissing = !locator.IsWinFspInstalled();

            if (!rcloneMissing && !winFspMissing)
            {
                return true;
            }

            // Interrumpimos el arranque del dashboard: mostramos la ventana de
            // configuracion inicial, que permite descargar rclone con 1 clic y
            // abrir el instalador oficial de WinFsp.
            var viewModel = _serviceProvider.GetRequiredService<FirstRunSetupViewModel>();
            viewModel.RefreshStatus();

            var window = new FirstRunSetupWindow(viewModel)
            {
                Owner = _mainWindow
            };

            var completed = window.ShowDialog();

            if (completed != true)
            {
                // El usuario decidio salir sin completar la instalacion.
                return false;
            }

            // Reintento: el bucle vuelve a comprobar el estado real. Si el
            // usuario pulso "Continuar" sin tener todo listo, el ViewModel ya
            // lo habra avisado y volveremos a mostrar la ventana.
            await Task.Yield();
        }
    }

    /// <summary>
    /// FASE 4: si el rclone.conf esta cifrado, muestra la ventana modal de
    /// contrasena y valida la clave contra rclone. Repite la solicitud hasta
    /// que la contrasena sea correcta o el usuario cancele (en cuyo caso se
    /// cierra la aplicacion).
    /// </summary>
    /// <returns>true si la configuracion esta accesible (sin cifrar o desbloqueada).</returns>
    private bool EnsureConfigUnlocked()
    {
        if (_serviceProvider is null)
        {
            return false;
        }

        var encryptionService = _serviceProvider.GetRequiredService<IConfigEncryptionService>();

        // Si el archivo no esta cifrado, no hay nada que hacer.
        if (!encryptionService.IsConfigEncrypted())
        {
            return true;
        }

        // Si ya disponemos de una contrasena valida (p. ej. tras un re-bloqueo
        // de sesion), la reaplicamos al entorno de proceso y continuamos.
        if (encryptionService.HasPassword)
        {
            encryptionService.ApplyPasswordToEnvironment();
            return true;
        }

        var viewModel = _serviceProvider.GetRequiredService<PasswordPromptViewModel>();
        viewModel.Reset();

        var window = new PasswordPromptWindow(viewModel)
        {
            Owner = _mainWindow
        };

        var unlocked = window.ShowDialog();

        if (unlocked == true)
        {
            // La contrasena ya quedo aplicada al entorno de proceso por el
            // propio servicio durante la validacion.
            return true;
        }

        // El usuario cancelo: limpiamos cualquier resto y abortamos el arranque.
        encryptionService.ClearPassword();
        return false;
    }

    /// <summary>
    /// FASE 2: crea el icono de la bandeja del sistema y conecta sus acciones
    /// (abrir panel, desmontar todo, salir completamente).
    /// </summary>
    private void InitializeTrayIcon()
    {
        if (_serviceProvider is null || _trayIcon is not null)
        {
            return;
        }

        _trayIcon = _serviceProvider.GetRequiredService<ITrayIconService>();
        _trayIcon.OpenRequested += (_, _) => ShowMainWindowFromTray();
        _trayIcon.UnmountAllRequested += async (_, _) => await UnmountAllFromTrayAsync().ConfigureAwait(true);
        _trayIcon.ExitRequested += (_, _) => ExitCompletely();
        _trayIcon.Show();
    }

    /// <summary>
    /// FASE 2: restaura el dashboard desde la bandeja. Si aun no existe la
    /// ventana (arranque minimizado sin desbloqueo previo), se crea.
    /// </summary>
    private void ShowMainWindowFromTray()
    {
        if (_serviceProvider is null)
        {
            return;
        }

        if (_mainWindow is null)
        {
            ShowMainWindow();
            return;
        }

        _mainWindow.ShowFromTray();
    }

    /// <summary>
    /// FASE 2: desmonta todas las unidades desde el menu de la bandeja,
    /// reutilizando el comando del ViewModel principal.
    /// </summary>
    private async Task UnmountAllFromTrayAsync()
    {
        if (_serviceProvider is null)
        {
            return;
        }

        var mainViewModel = _serviceProvider.GetRequiredService<MainViewModel>();
        if (mainViewModel.UnmountAllCommand.CanExecute(null))
        {
            await mainViewModel.UnmountAllCommand.ExecuteAsync(null).ConfigureAwait(true);
        }

        _trayIcon?.ShowNotification(
            "Visual Rclone",
            _serviceProvider.GetRequiredService<ILocalizationService>()
                .Get("Str_TrayUnmountDone", "All drives have been unmounted."));
    }

    /// <summary>
    /// FASE 2: cierre real de la aplicacion solicitado desde la bandeja.
    /// Autoriza a la ventana principal a cerrarse y apaga la app (lo que
    /// dispara la limpieza de procesos de OnExit).
    /// </summary>
    private void ExitCompletely()
    {
        _mainWindow?.AllowRealClose();
        _trayIcon?.Hide();
        Shutdown();
    }

    /// <summary>
    /// FASE 5: cierre completo solicitado por el usuario desde la X de la
    /// ventana principal (eligiendo "No" en el dialogo de confirmacion).
    ///
    /// Antes de apagar la aplicacion se ejecuta la limpieza global de montajes
    /// (desmontaje ordenado + eliminacion de procesos rclone huerfanos) para no
    /// dejar unidades colgadas. Despues se libera el icono de la bandeja y se
    /// apaga la app.
    ///
    /// Es idempotente: si ya se estaba cerrando, no repite la limpieza.
    /// </summary>
    public async Task RequestExitFromUserAsync()
    {
        if (_isExiting)
        {
            return;
        }

        _isExiting = true;

        try
        {
            // 1) Limpieza global de montajes: desmontar todo y matar huerfanos.
            if (_serviceProvider is not null)
            {
                var processManager = _serviceProvider.GetService<IMountProcessManager>();
                if (processManager is not null)
                {
                    try
                    {
                        await processManager.UnmountAllAsync().ConfigureAwait(true);
                        await processManager.KillOrphanRcloneProcessesAsync().ConfigureAwait(true);
                    }
                    catch (Exception ex) when (ex is InvalidOperationException or ObjectDisposedException)
                    {
                        // La limpieza es best-effort: nunca debe impedir el cierre.
                    }
                }
            }
        }
        finally
        {
            // 2) Liberar el icono de la bandeja y apagar la aplicacion.
            _trayIcon?.Dispose();
            _trayIcon = null;

            _mainWindow?.AllowRealClose();
            Shutdown();
        }
    }

    /// <summary>
    /// Configura el contenedor de dependencias.
    /// Todos los servicios se registran como singleton porque mantienen
    /// estado compartido (procesos, configuracion, cache).
    /// </summary>
    private static ServiceProvider BuildServiceProvider()
    {
        var services = new ServiceCollection();

        // --- Servicios de infraestructura ---
        services.AddSingleton<ILocalizationService, LocalizationService>();
        services.AddSingleton<ISettingsService, SettingsService>();
        services.AddSingleton<ISecurityService, SecurityService>();
        services.AddSingleton<IRcloneLocator, RcloneLocator>();
        services.AddSingleton<IDependencyManager, DependencyManager>();
        services.AddSingleton<IRcloneConfigParser, RcloneConfigParser>();
        services.AddSingleton<IRcloneConfigBootstrapper, RcloneConfigBootstrapper>();
        services.AddSingleton<IRcloneConfigManager, RcloneConfigManager>();
        services.AddSingleton<IDriveLetterService, DriveLetterService>();
        services.AddSingleton<IMountProcessManager, MountProcessManager>();
        services.AddSingleton<IVfsMaintenanceService, VfsMaintenanceService>();
        services.AddSingleton<IRcloneRcClient, RcloneRcClient>();

        // --- FASE 2: modo segundo plano (bandeja + arranque con Windows) ---
        services.AddSingleton<IStartupShortcutService, StartupShortcutService>();
        services.AddSingleton<ITrayIconService, TrayIconService>();

        // --- FASE 4: soporte de rclone.conf cifrado ---
        services.AddSingleton<IConfigEncryptionService, ConfigEncryptionService>();

        // --- ViewModels ---
        services.AddTransient<SetupViewModel>();
        services.AddTransient<LockViewModel>();
        services.AddTransient<ConfigManagerViewModel>();
        services.AddTransient<DependencyWarningViewModel>();
        services.AddTransient<FirstRunSetupViewModel>();
        services.AddTransient<PasswordPromptViewModel>();
        services.AddSingleton<MainViewModel>();

        // --- Views ---
        services.AddTransient<SetupWindow>();
        services.AddTransient<LockWindow>();
        services.AddTransient<ConfigManagerWindow>();
        services.AddTransient<FirstRunSetupWindow>();
        services.AddSingleton<MainWindow>();

        return services.BuildServiceProvider();
    }

    /// <summary>Muestra el asistente de primer arranque.</summary>
    private void ShowSetupScreen()
    {
        if (_serviceProvider is null)
        {
            return;
        }

        var setupWindow = _serviceProvider.GetRequiredService<SetupWindow>();
        var result = setupWindow.ShowDialog();

        if (result == true)
        {
            // PIN creado: pasamos directamente al dashboard.
            ShowMainWindow();
        }
        else
        {
            // El usuario cerro el asistente: no hay nada que hacer.
            Shutdown();
        }
    }

    /// <summary>Muestra la pantalla de bloqueo.</summary>
    public void ShowLockScreen()
    {
        if (_serviceProvider is null)
        {
            return;
        }

        var lockWindow = _serviceProvider.GetRequiredService<LockWindow>();
        var result = lockWindow.ShowDialog();

        if (result == true)
        {
            ShowMainWindow();
        }
        else
        {
            Shutdown();
        }
    }

    /// <summary>Muestra el dashboard principal.</summary>
    private void ShowMainWindow()
    {
        if (_serviceProvider is null)
        {
            return;
        }

        if (_mainWindow is null)
        {
            _mainWindow = _serviceProvider.GetRequiredService<MainWindow>();
            MainWindow = _mainWindow;
        }

        _mainWindow.Show();
        _mainWindow.WindowState = WindowState.Normal;
        _mainWindow.Activate();

        // Reanudar el monitor de salud tras un desbloqueo.
        var mainViewModel = _serviceProvider.GetRequiredService<MainViewModel>();
        mainViewModel.ResumeAfterUnlock();
    }

    /// <summary>
    /// FASE 2: arranque silencioso con Windows. Crea el dashboard pero lo deja
    /// oculto en la bandeja del sistema, sin robar el foco al iniciar sesion.
    /// </summary>
    private void ShowMainWindowMinimized()
    {
        if (_serviceProvider is null)
        {
            return;
        }

        if (_mainWindow is null)
        {
            _mainWindow = _serviceProvider.GetRequiredService<MainWindow>();
            MainWindow = _mainWindow;
        }

        // Se inicializa el ViewModel (montaje automatico, monitor de salud)
        // pero la ventana permanece oculta hasta que el usuario la abra.
        _mainWindow.Show();
        _mainWindow.Hide();

        var mainViewModel = _serviceProvider.GetRequiredService<MainViewModel>();
        mainViewModel.ResumeAfterUnlock();
    }

    /// <summary>
    /// Limpieza garantizada al cerrar la aplicacion: mata todos los
    /// procesos de rclone para no dejar unidades huerfanas montadas.
    /// </summary>
    protected override async void OnExit(ExitEventArgs e)
    {
        try
        {
            // FASE 2: liberar el icono de la bandeja antes de apagar.
            _trayIcon?.Dispose();
            _trayIcon = null;

            if (_serviceProvider is not null)
            {
                // FASE 4: eliminar la contrasena de la configuracion del
                // entorno de proceso para no dejarla accesible tras el cierre.
                _serviceProvider.GetService<IConfigEncryptionService>()?.ClearPassword();

                var processManager = _serviceProvider.GetRequiredService<IMountProcessManager>();

                // Desmontaje ordenado + limpieza de huerfanos.
                await processManager.UnmountAllAsync().ConfigureAwait(false);
                await processManager.KillOrphanRcloneProcessesAsync().ConfigureAwait(false);

                await processManager.DisposeAsync().ConfigureAwait(false);

                var mainViewModel = _serviceProvider.GetService<MainViewModel>();
                mainViewModel?.Dispose();

                _serviceProvider.Dispose();
            }
        }
        catch (Exception ex) when (ex is InvalidOperationException or ObjectDisposedException)
        {
            // La limpieza durante el cierre es best-effort.
        }
        finally
        {
            base.OnExit(e);
        }
    }

    // ================================================================
    //  MANEJO GLOBAL DE EXCEPCIONES
    // ================================================================

    private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        // Evitamos que un fallo puntual tumbe toda la aplicacion y, sobre
        // todo, lo hacemos visible para poder diagnosticarlo.
        ShowFatalError("Str_FatalErrorUnhandled", "Unhandled error", e.Exception);
        e.Handled = true;
    }

    private void OnDomainUnhandledException(object sender, UnhandledExceptionEventArgs e)
    {
        if (e.ExceptionObject is Exception ex)
        {
            ShowFatalError("Str_FatalErrorCritical", "Critical unhandled error", ex);
        }
    }

    private void OnUnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs e)
    {
        e.SetObserved();
    }

    private static void ShowFatalError(string titleKey, string titleFallback, Exception ex)
    {
        try
        {
            // El titulo se resuelve desde el diccionario del idioma activo para
            // que el dialogo fatal respete el idioma seleccionado por el usuario.
            // Si el diccionario aun no esta cargado (fallo muy temprano), se usa
            // el respaldo en ingles.
            var localizedTitle = Application.Current?.TryFindResource(titleKey) as string;
            if (string.IsNullOrWhiteSpace(localizedTitle))
            {
                localizedTitle = titleFallback;
            }

            var dialogTitle = Application.Current?.TryFindResource("Str_FatalErrorTitle") as string;
            if (string.IsNullOrWhiteSpace(dialogTitle))
            {
                dialogTitle = "Visual Rclone - Unexpected error";
            }

            MessageBox.Show(
                $"{ex.GetType().Name}: {ex.Message}\n\n{ex.StackTrace}",
                $"{dialogTitle} ({localizedTitle})",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
        catch
        {
            // Si ni siquiera podemos mostrar el mensaje, no hay nada mas que hacer.
        }
    }
}
