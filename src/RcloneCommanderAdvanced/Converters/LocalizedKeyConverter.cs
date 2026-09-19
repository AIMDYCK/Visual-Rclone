using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace RcloneCommanderAdvanced.Converters;

/// <summary>
/// Resuelve una clave de recurso (por ejemplo "Str_ProfileSafeReadName")
/// contra los diccionarios de recursos de la aplicacion y devuelve el texto
/// traducido en el idioma activo.
///
/// Se usa para enlazar propiedades que contienen CLAVES de traduccion (como
/// <c>MountProfile.DisplayNameKey</c>) en lugar de texto ya resuelto, de modo
/// que el idioma pueda cambiarse en caliente sin recrear los ViewModels.
///
/// Si la clave no existe, se devuelve la propia clave como fallback para
/// facilitar el diagnostico.
/// </summary>
public sealed class LocalizedKeyConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        var key = value as string;

        if (string.IsNullOrWhiteSpace(key))
        {
            return string.Empty;
        }

        var resource = Application.Current?.TryFindResource(key);
        return resource as string ?? key;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
