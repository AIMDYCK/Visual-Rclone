using System;
using System.Globalization;
using System.Windows.Data;

namespace RcloneCommanderAdvanced.Converters;

/// <summary>
/// Invierte un valor booleano. Util para habilitar/deshabilitar controles.
/// </summary>
public sealed class InverseBoolConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        value is bool b && !b;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        value is bool b && !b;
}
