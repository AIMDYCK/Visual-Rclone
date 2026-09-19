using System.Windows;
using System.Windows.Media;
using RcloneCommanderAdvanced.Helpers;
using RcloneCommanderAdvanced.ViewModels;

namespace RcloneCommanderAdvanced.Views;

/// <summary>
/// Ventana modal de pre-flight (Fase 1).
///
/// Se muestra cuando falta rclone.exe o WinFsp. El usuario puede reintentar
/// la deteccion (tras instalar la dependencia) o salir de la aplicacion.
/// </summary>
public partial class DependencyWarningWindow : Window
{
    private readonly DependencyWarningViewModel _viewModel;

    public DependencyWarningWindow(DependencyWarningViewModel viewModel)
    {
        InitializeComponent();

        _viewModel = viewModel;
        DataContext = _viewModel;

        WindowThemeHelper.ApplyDarkTitleBar(
            this,
            captionColor: Color.FromRgb(0x13, 0x17, 0x1F),
            textColor: Color.FromRgb(0xE6, 0xED, 0xF3));
    }

    /// <summary>
    /// El usuario pulsa "Reintentar": cerramos con DialogResult=true para que
    /// App.xaml.cs vuelva a ejecutar la comprobacion de dependencias.
    /// </summary>
    private void Retry_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = true;
        Close();
    }

    /// <summary>El usuario decide salir: cerramos sin reintentar.</summary>
    private void Exit_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
}
