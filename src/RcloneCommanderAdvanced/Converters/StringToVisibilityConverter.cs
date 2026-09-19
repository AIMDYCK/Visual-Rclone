using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace RcloneCommanderAdvanced.Converters;

/// <summary>
/// Muestra un elemento solo si la cadena no esta vacia. Si se pasa el
/// parametro "Inverse" (o "invert"), la logica se invierte: el elemento se
/// muestra cuando la cadena esta vacia (util para estados vacios).
/// </summary>
public sealed class StringToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        var text = value as string;
        var isEmpty = string.IsNullOrWhiteSpace(text);

        var inverse = parameter is string p &&
                      (p.Equals("Inverse", StringComparison.OrdinalIgnoreCase) ||
                       p.Equals("invert", StringComparison.OrdinalIgnoreCase));

        var visible = inverse ? isEmpty : !isEmpty;
        return visible ? Visibility.Visible : Visibility.Collapsed;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
