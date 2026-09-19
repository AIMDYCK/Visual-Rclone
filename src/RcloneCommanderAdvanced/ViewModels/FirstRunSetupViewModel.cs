using System;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using RcloneCommanderAdvanced.Services.Abstractions;

namespace RcloneCommanderAdvanced.ViewModels;

/// <summary>
/// ViewModel de la ventana de configuracion inicial ("First Run Setup").
///
/// Se muestra cuando falta alguna dependencia critica (rclone.exe o WinFsp) en
/// un entorno limpio. Ofrece una instalacion de 1 clic para rclone (descarga y
/// extraccion automaticas) y, para WinFsp, un enlace oficial de descarga con
/// verificacion de reintento (requiere instalador MSI con privilegios).
///
/// Estrictamente MVVM: no hay logica en el code-behind de la vista. Toda la
/// orquestacion (descarga, comprobacion, cierre) vive aqui.
/// </summary>
public sealed partial class FirstRunSetupViewModel : ObservableObject
{
    private readonly IDependencyManager _dependencyManager;
    private readonly ILocalizationService _localization;

    private CancellationTokenSource? _cts;

    public FirstRunSetupViewModel(
        IDependencyManager dependencyManager,
        ILocalizationService localization)
    {
        _dependencyManager = dependencyManager;
        _localization = localization;

        RefreshStatus();
    }

    // ---------------------------------------------------------------------
    // Estado de las dependencias
    // ---------------------------------------------------------------------

    /// <summary>True si rclone.exe esta disponible.</summary>
    [ObservableProperty]
    private bool _isRcloneInstalled;

    /// <summary>True si WinFsp esta instalado.</summary>
    [ObservableProperty]
    private bool _isWinFspInstalled;

    /// <summary>Texto de estado de rclone ("Instalado" / "Pendiente").</summary>
    [ObservableProperty]
    private string _rcloneStatusText = string.Empty;

    /// <summary>Texto de estado de WinFsp ("Instalado" / "Pendiente").</summary>
    [ObservableProperty]
    private string _winFspStatusText = string.Empty;

    /// <summary>True si TODAS las dependencias estan listas.</summary>
    [ObservableProperty]
    private bool _allDependenciesReady;

    // ---------------------------------------------------------------------
    // Estado de la operacion de descarga
    // ---------------------------------------------------------------------

    /// <summary>True mientras se esta descargando/instalando rclone.</summary>
    [ObservableProperty]
    private bool _isBusy;

    /// <summary>Progreso de la descarga (0-100).</summary>
    [ObservableProperty]
    private int _progressValue;

    /// <summary>Mensaje de estado de la operacion en curso.</summary>
    [ObservableProperty]
    private string _statusMessage = string.Empty;

    /// <summary>True si la ultima operacion termino con error.</summary>
    [ObservableProperty]
    private bool _hasError;

    /// <summary>
    /// Se dispara cuando el usuario confirma que todo esta listo y la ventana
    /// debe cerrarse con exito. La vista se suscribe para cerrar el dialogo.
    /// </summary>
    public event EventHandler? SetupCompleted;

    /// <summary>
    /// Se dispara cuando el usuario decide salir sin completar la instalacion.
    /// </summary>
    public event EventHandler? SetupCancelled;

    /// <summary>URL de descarga de WinFsp (para mostrar en la UI).</summary>
    public string WinFspDownloadUrl => _dependencyManager.WinFspDownloadUrl;

    /// <summary>Directorio interno donde se instala rclone.</summary>
    public string InternalBinDirectory => _dependencyManager.InternalBinDirectory;

    /// <summary>
    /// Recalcula el estado de las dependencias y actualiza los textos.
    /// </summary>
    public void RefreshStatus()
    {
        IsRcloneInstalled = _dependencyManager.IsRcloneInstalled();
        IsWinFspInstalled = _dependencyManager.IsWinFspInstalled();
        AllDependenciesReady = IsRcloneInstalled && IsWinFspInstalled;

        RcloneStatusText = IsRcloneInstalled
            ? _localization.Get("Str_StatusInstalled", "Installed")
            : _localization.Get("Str_StatusPending", "Pending");

        WinFspStatusText = IsWinFspInstalled
            ? _localization.Get("Str_StatusInstalled", "Installed")
            : _localization.Get("Str_StatusPending", "Pending");
    }

    /// <summary>
    /// INSTALACION RAPIDA (1 CLIC): descarga y configura rclone.exe.
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanRunQuickInstall))]
    private async Task QuickInstallAsync()
    {
        if (IsBusy)
        {
            return;
        }

        IsBusy = true;
        HasError = false;
        ProgressValue = 0;
        StatusMessage = _localization.Get("Str_InstallingRclone", "Installing rclone...");

        _cts = new CancellationTokenSource();

        try
        {
            // El callback de progreso se invoca desde un hilo de fondo; marshalamos
            // al hilo de UI mediante Dispatcher para actualizar las propiedades
            // enlazadas sin cruzar hilos.
            var dispatcher = System.Windows.Application.Current?.Dispatcher;

            void OnProgress(int percent, string message)
            {
                if (dispatcher is not null && !dispatcher.CheckAccess())
                {
                    dispatcher.Invoke(() =>
                    {
                        ProgressValue = percent;
                        StatusMessage = message;
                    });
                }
                else
                {
                    ProgressValue = percent;
                    StatusMessage = message;
                }
            }

            var result = await _dependencyManager.DownloadAndSetupRcloneAsync(
                OnProgress,
                _cts.Token).ConfigureAwait(true);

            if (result.Success)
            {
                StatusMessage = _localization.Get(
                    "Str_RcloneInstalledOk", "rclone installed successfully.");
                ProgressValue = 100;
            }
            else
            {
                HasError = true;
                StatusMessage = result.Message;
            }
        }
        catch (Exception ex)
        {
            HasError = true;
            StatusMessage = ex.Message;
        }
        finally
        {
            IsBusy = false;
            _cts?.Dispose();
            _cts = null;

            // Recalcular el estado real tras la operacion.
            RefreshStatus();
            NotifyCommands();
        }
    }

    /// <summary>Permite ejecutar la instalacion rapida solo si no esta ocupado.</summary>
    private bool CanRunQuickInstall() => !IsBusy;

    /// <summary>
    /// Abre la pagina oficial de descarga de WinFsp en el navegador.
    /// </summary>
    [RelayCommand]
    private void OpenWinFspDownload() => _dependencyManager.OpenWinFspDownloadPage();

    /// <summary>
    /// Reintenta la deteccion de dependencias (util tras instalar WinFsp
    /// manualmente con su asistente externo).
    /// </summary>
    [RelayCommand]
    private void RetryDetection()
    {
        HasError = false;
        StatusMessage = string.Empty;
        RefreshStatus();
        NotifyCommands();
    }

    /// <summary>
    /// Confirma la finalizacion: cierra la ventana con exito si todo esta
    /// listo. Si aun falta algo, actualiza el estado y no cierra.
    /// </summary>
    [RelayCommand]
    private void Complete()
    {
        RefreshStatus();

        if (AllDependenciesReady)
        {
            SetupCompleted?.Invoke(this, EventArgs.Empty);
        }
        else
        {
            HasError = true;
            StatusMessage = _localization.Get(
                "Str_DependenciesStillMissing",
                "Some dependencies are still missing. Install them to continue.");
        }
    }

    /// <summary>Cancela la configuracion inicial y cierra la aplicacion.</summary>
    [RelayCommand]
    private void Cancel()
    {
        _cts?.Cancel();
        SetupCancelled?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Notifica a los comandos que reevalúen su CanExecute.</summary>
    private void NotifyCommands()
    {
        QuickInstallCommand.NotifyCanExecuteChanged();
    }
}
