using System.Windows;
using System.Windows.Controls;
using RcloneCommanderAdvanced.Models;

namespace RcloneCommanderAdvanced.Selectors;

/// <summary>
/// Selector de plantilla para el formulario dinamico del paso 3 del asistente.
///
/// rclone publica, para cada backend, un esquema con decenas de opciones de
/// tipos muy distintos (listas cerradas, booleanos, secretos, texto libre).
/// En lugar de hardcodear un campo por proveedor, el paso 3 se genera con un
/// <c>ItemsControl</c> y este selector decide, opcion por opcion, que control
/// de WPF dibujar:
///
///   - <b>ComboBox</b>   cuando el esquema declara una lista cerrada de valores
///                       validos (<see cref="ProviderOptionModel.IsChoice"/>).
///   - <b>CheckBox</b>   cuando el tipo es booleano (<see cref="ProviderOptionModel.IsBoolean"/>).
///   - <b>PasswordBox</b> cuando la clave es un secreto (<see cref="ProviderOptionModel.IsSecret"/>).
///   - <b>TextBox</b>    en cualquier otro caso (texto libre, rutas, endpoints...).
///
/// Se usa un <see cref="DataTemplateSelector"/> (y no DataTriggers) porque las
/// condiciones son mutuamente excluyentes y dependen de propiedades calculadas
/// del modelo: un selector mantiene la logica en C# testeable y deja el XAML
/// declarativo y legible.
/// </summary>
public sealed class ProviderOptionTemplateSelector : DataTemplateSelector
{
    /// <summary>Plantilla para opciones con lista cerrada de valores (ComboBox).</summary>
    public DataTemplate? ChoiceTemplate { get; set; }

    /// <summary>Plantilla para opciones booleanas (CheckBox).</summary>
    public DataTemplate? BooleanTemplate { get; set; }

    /// <summary>Plantilla para secretos (PasswordBox).</summary>
    public DataTemplate? SecretTemplate { get; set; }

    /// <summary>Plantilla por defecto para texto libre (TextBox).</summary>
    public DataTemplate? TextTemplate { get; set; }

    /// <inheritdoc />
    public override DataTemplate? SelectTemplate(object item, DependencyObject container)
    {
        if (item is not ProviderOptionModel option)
        {
            return TextTemplate;
        }

        // El orden importa y NO es arbitrario.
        //
        // BUGFIX (OAuth client_secret): rclone publica para "client_secret" del
        // backend drive una lista de Examples (sugiere el secreto por defecto de
        // rclone). Con la comprobacion de IsChoice en primer lugar, el campo se
        // renderizaba como ComboBox y el valor tecleado por el usuario se perdia:
        // un ComboBox con SelectedValue no captura texto libre que no figure
        // entre los ejemplos, de modo que Value quedaba vacio y rclone abortaba
        // con "client_secret is missing".
        //
        // Por eso los SECRETOS se comprueban ANTES que las listas de valores:
        // un secreto jamas debe renderizarse como lista cerrada, aunque el
        // esquema le adjunte ejemplos. El resto del orden se mantiene.
        if (option.IsSecret)
        {
            return SecretTemplate ?? TextTemplate;
        }

        if (option.IsChoice)
        {
            return ChoiceTemplate ?? TextTemplate;
        }

        if (option.IsBoolean)
        {
            return BooleanTemplate ?? TextTemplate;
        }

        return TextTemplate;
    }
}
