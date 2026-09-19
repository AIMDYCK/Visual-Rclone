using System;
using System.ComponentModel;
using System.Windows;
using System.Windows.Media;
using Microsoft.Extensions.DependencyInjection;
using RcloneCommanderAdvanced.Helpers;
using RcloneCommanderAdvanced.Services.Abstractions;
using RcloneCommanderAdvanced.ViewModels;

namespace RcloneCommanderAdvanced.Views;

/// <summary>
/// Ventana principal del dashboard.
/// </summary>
public partial class MainWindow : Window
{
    private readonly MainViewModel _viewModel;

    /// <summary>
    /// FASE 2: cuando es true, cerrar la ventana NO termina la aplicacion:
    /// se oculta y permanece viva en la bandeja del sistema. Solo se pone a
    /// false cuando el usuario elige "Salir completamente" desde la bandeja.
    /// </summary>
    private bool _hideOnClose = true;

    public MainWindow(MainViewModel viewModel)
    {
        InitializeComponent();

        _viewModel = viewModel;
        DataContext = viewModel;

        _viewModel.LockRequested += OnLockRequested;
        Loaded += OnLoaded;

        // Barra de titulo oscura nativa (Windows 10/11).
        // Fondo #13171F y texto claro para integrarse con la estetica del panel.
        WindowThemeHelper.ApplyDarkTitleBar(
            this,
            captionColor: Color.FromRgb(0x13, 0x17, 0x1F),
            textColor: Color.FromRgb(0xE6, 0xED, 0xF3));
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        Loaded -= OnLoaded;
        await _viewModel.InitializeAsync();
    }

    /// <summary>
    /// Al perder el foco el ComboBox editable de ancho de banda, se valida
    /// el texto introducido y se aplica en caliente si es una tasa valida.
    /// Se ignora cuando el foco pasa a un elemento interno del propio
    /// ComboBox (p. ej. el cuadro de texto de edicion) para no disparar
    /// la aplicacion mientras el usuario sigue escribiendo.
    /// </summary>
    private void BandwidthCombo_LostFocus(object sender, RoutedEventArgs e)
    {
        if (sender is not System.Windows.Controls.ComboBox combo) return;

        // Si el nuevo foco sigue dentro del ComboBox, no aplicamos todavia.
        if (combo.IsKeyboardFocusWithin) return;

        if (_viewModel.ApplyCustomBandwidthCommand.CanExecute(null))
        {
            _viewModel.ApplyCustomBandwidthCommand.Execute(null);
        }
    }

    /// <summary>
    /// Fase 4: abre el editor interactivo de rclone.conf como ventana modal.
    /// Al cerrarse, se recarga la lista de remotos del dashboard para reflejar
    /// los cambios (altas/bajas) sin reiniciar la aplicacion.
    /// </summary>
    private async void ManageAccounts_Click(object sender, RoutedEventArgs e)
    {
        if (Application.Current is not App app)
        {
            return;
        }

        var editor = app.Services.GetRequiredService<ConfigManagerWindow>();
        editor.Owner = this;
        editor.ShowDialog();

        // Refrescar la lista de remotos tras editar la configuracion.
        await _viewModel.InitializeAsync();
    }

    private void OnLockRequested(object? sender, EventArgs e)
    {
        // Ocultamos el dashboard y delegamos el re-bloqueo al App.
        Hide();

        if (Application.Current is App app)
        {
            app.ShowLockScreen();
        }
    }

    /// <summary>
    /// FASE 2 / FASE 5: al pulsar la X (o Alt+F4) se intercepta el cierre y se
    /// muestra un dialogo oscuro personalizado (<see cref="ExitDialog"/>) con
    /// tres opciones:
    ///
    ///  - "Segundo plano" -> se oculta la ventana y la app sigue viva en la
    ///                       bandeja del sistema, manteniendo los discos montados.
    ///  - "Salir y desmontar" -> cierre completo: se desmontan todas las
    ///                       unidades, se matan los procesos rclone huerfanos
    ///                       y se apaga la app.
    ///  - "Cancelar"      -> no se hace nada: la ventana permanece visible.
    ///
    /// El cierre real (bandeja -> "Salir completamente") sigue usando
    /// <see cref="AllowRealClose"/> y no vuelve a preguntar.
    /// </summary>
    protected override void OnClosing(CancelEventArgs e)
    {
        if (_hideOnClose)
        {
            // Interceptamos SIEMPRE el cierre para preguntar al usuario.
            e.Cancel = true;

            var dialog = new ExitDialog
            {
                Owner = this
            };
            dialog.ShowDialog();

            switch (dialog.Result)
            {
                case ExitDialogResult.RunInBackground:
                    // Minimizar a la bandeja: la app sigue viva y los discos
                    // permanecen montados.
                    Hide();
                    ShowTrayIcon();
                    return;

                case ExitDialogResult.ExitAndUnmount:
                    // Cierre completo: delegamos en App la limpieza global
                    // (desmontar todo + matar huerfanos) y el apagado.
                    if (Application.Current is App app)
                    {
                        _ = app.RequestExitFromUserAsync();
                    }
                    return;

                default:
                    // Cancelar: la ventana permanece visible y no se cierra.
                    return;
            }
        }

        _viewModel.LockRequested -= OnLockRequested;
        base.OnClosing(e);
    }

    /// <summary>
    /// FASE 5: garantiza que el icono de la bandeja este visible cuando la
    /// ventana se oculta. Reutiliza el servicio de bandeja ya existente en
    /// lugar de crear un segundo NotifyIcon.
    /// </summary>
    private void ShowTrayIcon()
    {
        if (Application.Current is not App app)
        {
            return;
        }

        try
        {
            var trayIcon = app.Services.GetService<ITrayIconService>();
            trayIcon?.Show();
        }
        catch (InvalidOperationException)
        {
            // El contenedor aun no esta listo: la bandeja ya se inicializa en
            // el arranque, asi que no es critico.
        }
    }

    /// <summary>
    /// FASE 2: autoriza el cierre real de la ventana. Lo invoca App.xaml.cs
    /// cuando el usuario elige "Salir completamente" desde la bandeja.
    /// </summary>
    public void AllowRealClose()
    {
        _hideOnClose = false;
    }

    /// <summary>
    /// FASE 2: restaura y activa la ventana desde la bandeja del sistema.
    /// </summary>
    public void ShowFromTray()
    {
        Show();

        if (WindowState == WindowState.Minimized)
        {
            WindowState = WindowState.Normal;
        }

        Activate();
        Topmost = true;
        Topmost = false;
        Focus();
    }
}
