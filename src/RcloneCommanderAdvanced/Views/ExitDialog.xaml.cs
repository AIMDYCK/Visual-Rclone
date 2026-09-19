using System.Windows;
using System.Windows.Input;

namespace RcloneCommanderAdvanced.Views;

/// <summary>
/// Resultado elegido por el usuario en el dialogo de cierre.
/// </summary>
public enum ExitDialogResult
{
    /// <summary>El usuario cancelo: la ventana principal no se cierra.</summary>
    Cancel,

    /// <summary>El usuario quiere seguir en segundo plano (bandeja del sistema).</summary>
    RunInBackground,

    /// <summary>El usuario quiere cerrar la aplicacion y desmontar todo.</summary>
    ExitAndUnmount
}

/// <summary>
/// Dialogo de confirmacion de cierre con la estetica oscura de la aplicacion.
///
/// Sustituye al MessageBox nativo para respetar el tema visual y el sistema
/// multilenguaje (todas las cadenas se resuelven via DynamicResource).
///
/// Uso:
///   var dialog = new ExitDialog { Owner = this };
///   dialog.ShowDialog();
///   switch (dialog.Result) { ... }
/// </summary>
public partial class ExitDialog : Window
{
    /// <summary>
    /// Resultado elegido. Por defecto es <see cref="ExitDialogResult.Cancel"/>
    /// para que cerrar el dialogo con Alt+F4 o Escape no cierre la app.
    /// </summary>
    public ExitDialogResult Result { get; private set; } = ExitDialogResult.Cancel;

    public ExitDialog()
    {
        InitializeComponent();

        // Escape = cancelar (comportamiento esperado en un dialogo).
        PreviewKeyDown += OnPreviewKeyDown;
    }

    private void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            Result = ExitDialogResult.Cancel;
            DialogResult = false;
            e.Handled = true;
        }
    }

    private void Background_Click(object sender, RoutedEventArgs e)
    {
        Result = ExitDialogResult.RunInBackground;
        DialogResult = true;
    }

    private void Exit_Click(object sender, RoutedEventArgs e)
    {
        Result = ExitDialogResult.ExitAndUnmount;
        DialogResult = true;
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        Result = ExitDialogResult.Cancel;
        DialogResult = false;
    }
}
