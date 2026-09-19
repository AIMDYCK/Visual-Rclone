using System.Collections.Generic;
using System.Globalization;

namespace RcloneCommanderAdvanced.Services.Abstractions;

/// <summary>
/// Contrato del servicio de internacionalizacion (i18n).
///
/// La aplicacion soporta cuatro culturas: ingles (en-US, por defecto y
/// fallback), espanol (es-ES), frances (fr-FR) y aleman (de-DE). El servicio
/// expone tanto el idioma activo como la lista de idiomas disponibles para el
/// selector de la UI, y permite cambiar de idioma en caliente sin reiniciar.
/// </summary>
public interface ILocalizationService
{
    /// <summary>Cultura activa en este momento.</summary>
    CultureInfo CurrentCulture { get; }

    /// <summary>Codigo de la cultura activa (ej: "en-US").</summary>
    string CurrentLanguageCode { get; }

    /// <summary>Idiomas soportados, listos para enlazar a un ComboBox.</summary>
    IReadOnlyList<LanguageOption> AvailableLanguages { get; }

    /// <summary>
    /// Se dispara tras cambiar de idioma. Las vistas pueden suscribirse para
    /// refrescar textos que no esten enlazados dinamicamente.
    /// </summary>
    event System.EventHandler? LanguageChanged;

    /// <summary>
    /// Aplica una cultura por su codigo (ej: "es-ES"). Si el codigo no esta
    /// soportado, se aplica la cultura por defecto (en-US).
    /// </summary>
    void SetLanguage(string languageCode);

    /// <summary>
    /// Devuelve el texto asociado a una clave en el idioma activo. Si la clave
    /// no existe, devuelve la propia clave entre corchetes para facilitar el
    /// diagnostico sin romper la UI.
    /// </summary>
    string this[string key] { get; }

    /// <summary>
    /// Devuelve el texto asociado a una clave, o <paramref name="fallback"/>
    /// si la clave no existe.
    /// </summary>
    string Get(string key, string fallback);
}

/// <summary>
/// Idioma disponible para el selector de la UI.
/// </summary>
public sealed class LanguageOption
{
    /// <summary>Codigo de cultura (ej: "en-US").</summary>
    public string Code { get; init; } = string.Empty;

    /// <summary>Nombre nativo mostrado en el ComboBox (ej: "English").</summary>
    public string DisplayName { get; init; } = string.Empty;

    /// <summary>Bandera/emoji opcional para enriquecer el selector.</summary>
    public string Flag { get; init; } = string.Empty;
}
