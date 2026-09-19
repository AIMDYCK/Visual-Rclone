using System.Windows;
using System.Windows.Media;
using RcloneCommanderAdvanced.Helpers;
using RcloneCommanderAdvanced.ViewModels;

namespace RcloneCommanderAdvanced.Views;

/// <summary>
/// Editor interactivo de rclone.conf (Fase 4).
///
/// Ventana modal que lista los remotos como tarjetas y ofrece un asistente
/// de 4 pasos para crear uno nuevo. Toda la logica de negocio vive en
/// <see cref="ConfigManagerViewModel"/>; este code-behind solo resuelve la
/// seleccion visual del proveedor y el cierre de la ventana.
/// </summary>
public partial class ConfigManagerWindow : Window
{
    private readonly ConfigManagerViewModel _viewModel;

    public ConfigManagerWindow(ConfigManagerViewModel viewModel)
    {
        InitializeComponent();

        _viewModel = viewModel ?? throw new System.ArgumentNullException(nameof(viewModel));
        DataContext = _viewModel;

        // La carga de datos se difiere al evento Loaded (via LoadedCommand) en
        // lugar de ejecutarse en el constructor: asi la ventana ya esta montada
        // y cualquier error se puede reportar sin impedir que la vista se abra.
        Loaded += OnWindowLoaded;

        // Barra de titulo oscura nativa (Windows 10/11).
        WindowThemeHelper.ApplyDarkTitleBar(
            this,
            captionColor: Color.FromRgb(0x13, 0x17, 0x1F),
            textColor: Color.FromRgb(0xE6, 0xED, 0xF3));
    }

    /// <summary>
    /// Dispara la inicializacion del ViewModel una vez la ventana esta cargada.
    /// Se ejecuta una sola vez para no recargar la lista en cada reactivacion.
    /// </summary>
    private void OnWindowLoaded(object sender, RoutedEventArgs e)
    {
        Loaded -= OnWindowLoaded;

        if (_viewModel.LoadedCommand.CanExecute(null))
        {
            _viewModel.LoadedCommand.Execute(null);
        }
    }

    /// <summary>Cierra la ventana del editor de configuracion.</summary>
    private void Close_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }
}
