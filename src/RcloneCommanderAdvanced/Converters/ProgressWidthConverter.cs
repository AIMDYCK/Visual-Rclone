using System;
using System.Globalization;
using System.Windows.Data;

namespace RcloneCommanderAdvanced.Converters;

/// <summary>
/// Convierte un porcentaje (0-100) y un ancho disponible en el ancho
/// en pixeles que debe ocupar la barra de progreso.
///
/// Uso: MultiBinding con [Porcentaje, AnchoDisponible].
/// </summary>
public sealed class ProgressWidthConverter : IMultiValueConverter
{
    public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
    {
        if (values.Length < 2)
        {
            return 0d;
        }

        if (values[0] is not double percent || double.IsNaN(percent))
        {
            return 0d;
        }

        if (values[1] is not double availableWidth || double.IsNaN(availableWidth) || availableWidth <= 0)
        {
            return 0d;
        }

        var clamped = Math.Max(0d, Math.Min(100d, percent));
        return availableWidth * (clamped / 100d);
    }

    public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
