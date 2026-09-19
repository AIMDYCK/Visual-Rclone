using System.Windows;
using System.Windows.Media;
using RcloneCommanderAdvanced.Helpers;
using RcloneCommanderAdvanced.ViewModels;

namespace RcloneCommanderAdvanced.Views;

/// <summary>
/// Ventana de configuracion inicial ("First Run Setup").
///
/// Se muestra cuando falta alguna dependencia critica (rclone.exe o WinFsp) en
/// un entorno limpio. Ofrece la instalacion de 1 clic de rclone y el enlace
/// oficial de WinFsp.
///
/// El code-behind es deliberadamente minimo (estricto MVVM): solo conecta los
/// eventos del ViewModel con el cierre del dialogo. Toda la logica de descarga,
/// deteccion y estado vive en <see cref="FirstRunSetupViewModel"/>.
/// </summary>
public partial class FirstRunSetupWindow : Window
{
    private readonly FirstRunSetupViewModel _viewModel;

    public FirstRunSetupWindow(FirstRunSetupViewModel viewModel)
    {
        InitializeComponent();

        _viewModel = viewModel;
        DataContext = _viewModel;

        // El ViewModel decide cuando la configuracion esta completa o cancelada.
        _viewModel.SetupCompleted += OnSetupCompleted;
        _viewModel.SetupCancelled += OnSetupCancelled;

        WindowThemeHelper.ApplyDarkTitleBar(
            this,
            captionColor: Color.FromRgb(0x13, 0x17, 0x1F),
            textColor: Color.FromRgb(0xE6, 0xED, 0xF3));
    }

    private void OnSetupCompleted(object? sender, System.EventArgs e)
    {
        DialogResult = true;
        Close();
    }

    private void OnSetupCancelled(object? sender, System.EventArgs e)
    {
        DialogResult = false;
        Close();
    }

    protected override void OnClosed(System.EventArgs e)
    {
        // Evitar fugas de suscripcion.
        _viewModel.SetupCompleted -= OnSetupCompleted;
        _viewModel.SetupCancelled -= OnSetupCancelled;

        base.OnClosed(e);
    }
}
