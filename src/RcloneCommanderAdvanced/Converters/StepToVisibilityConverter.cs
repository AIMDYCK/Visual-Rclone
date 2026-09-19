using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace RcloneCommanderAdvanced.Converters;

/// <summary>
/// Convierte el numero de paso actual del asistente (int) en una
/// <see cref="Visibility"/>. El parametro del converter indica el paso que
/// debe quedar visible; si coincide con el valor, se muestra; en caso
/// contrario se colapsa.
///
/// Uso en XAML:
///   Visibility="{Binding WizardStep,
///                Converter={StaticResource StepToVisibility},
///                ConverterParameter=2}"
/// </summary>
public sealed class StepToVisibilityConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is null || parameter is null)
        {
            return Visibility.Collapsed;
        }

        var current = value is int i
            ? i
            : int.TryParse(value.ToString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed)
                ? parsed
                : -1;

        var expected = parameter is int p
            ? p
            : int.TryParse(parameter.ToString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var target)
                ? target
                : -1;

        return current == expected ? Visibility.Visible : Visibility.Collapsed;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
