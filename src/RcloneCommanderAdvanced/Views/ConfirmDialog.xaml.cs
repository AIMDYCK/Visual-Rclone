using System.Windows;
using System.Windows.Input;

namespace RcloneCommanderAdvanced.Views;

/// <summary>
/// Dialogo de confirmacion generico con la estetica oscura de la aplicacion.
///
/// Sustituye al MessageBox nativo para respetar el tema visual y el sistema
/// multilenguaje: el llamador inyecta las cadenas ya localizadas (resueltas
/// desde el diccionario del idioma activo) a traves de las propiedades
/// <see cref="HeaderText"/>, <see cref="MessageText"/>, <see cref="ConfirmText"/>
/// y <see cref="CancelText"/>.
///
/// Uso tipico (accion destructiva):
///   var dialog = new ConfirmDialog
///   {
///       Owner = this,
///       HeaderText = L("Str_DeleteConfirmHeader", "DELETE REMOTE"),
///       MessageText = string.Format(L("Str_DeleteConfirmMessage", "..."), name),
///       ConfirmText = L("Str_DeleteConfirmButton", "DELETE"),
///       CancelText = L("Str_DeleteCancelButton", "CANCEL")
///   };
///   if (dialog.ShowDialog() == true) { ...borrar... }
/// </summary>
public partial class ConfirmDialog : Window
{
    /// <summary>Titulo mostrado en la cabecera del dialogo.</summary>
    public string HeaderText { get; set; } = string.Empty;

    /// <summary>Cuerpo del mensaje de advertencia.</summary>
    public string MessageText { get; set; } = string.Empty;

    /// <summary>Etiqueta del boton que confirma la accion destructiva.</summary>
    public string ConfirmText { get; set; } = string.Empty;

    /// <summary>Etiqueta del boton que cancela la accion.</summary>
    public string CancelText { get; set; } = string.Empty;

    public ConfirmDialog()
    {
        InitializeComponent();

        // Escape = cancelar (comportamiento esperado en un dialogo).
        PreviewKeyDown += OnPreviewKeyDown;
    }

    private void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            DialogResult = false;
            e.Handled = true;
        }
    }

    private void Confirm_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = true;
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
    }
}
