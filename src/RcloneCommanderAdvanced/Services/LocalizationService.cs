using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using RcloneCommanderAdvanced.Services.Abstractions;

namespace RcloneCommanderAdvanced.Services;

/// <summary>
/// Implementacion del servicio de internacionalizacion basada en
/// <see cref="ResourceDictionary"/> dinamicos.
///
/// Cada idioma vive en <c>Themes/Lang.xx-XX.xaml</c> y define un conjunto de
/// claves <c>Str_*</c>. Al cambiar de idioma se reemplaza el diccionario
/// fusionado en <see cref="Application.Current"/>, de modo que todos los
/// enlaces <c>{DynamicResource Str_Xxx}</c> se refrescan en caliente sin
/// reiniciar la aplicacion.
///
/// El idioma por defecto y fallback es en-US: si una clave no existe en el
/// idioma activo, se resuelve contra el diccionario ingles.
/// </summary>
public sealed class LocalizationService : ILocalizationService
{
    /// <summary>Cultura por defecto y de respaldo.</summary>
    public const string DefaultLanguageCode = "en-US";

    private const string ResourceDictionaryPrefix = "Themes/Lang.";

    private readonly Dictionary<string, ResourceDictionary> _cache = new(StringComparer.OrdinalIgnoreCase);

    private ResourceDictionary? _activeDictionary;
    private ResourceDictionary? _fallbackDictionary;

    public LocalizationService()
    {
        AvailableLanguages = new List<LanguageOption>
        {
            new() { Code = "en-US", DisplayName = "English",  Flag = "\U0001F1FA\U0001F1F8" },
            new() { Code = "es-ES", DisplayName = "Espa\u00f1ol", Flag = "\U0001F1EA\U0001F1F8" },
            new() { Code = "fr-FR", DisplayName = "Fran\u00e7ais", Flag = "\U0001F1EB\U0001F1F7" },
            new() { Code = "de-DE", DisplayName = "Deutsch",  Flag = "\U0001F1E9\U0001F1EA" },
        };

        CurrentCulture = CultureInfo.GetCultureInfo(DefaultLanguageCode);
        CurrentLanguageCode = DefaultLanguageCode;
    }

    /// <inheritdoc />
    public CultureInfo CurrentCulture { get; private set; }

    /// <inheritdoc />
    public string CurrentLanguageCode { get; private set; }

    /// <inheritdoc />
    public IReadOnlyList<LanguageOption> AvailableLanguages { get; }

    /// <inheritdoc />
    public event EventHandler? LanguageChanged;

    /// <inheritdoc />
    public string this[string key] => Get(key, $"[{key}]");

    /// <inheritdoc />
    public string Get(string key, string fallback)
    {
        if (string.IsNullOrWhiteSpace(key))
        {
            return fallback;
        }

        // 1. Idioma activo.
        if (_activeDictionary is not null && _activeDictionary.Contains(key))
        {
            return _activeDictionary[key]?.ToString() ?? fallback;
        }

        // 2. Fallback (ingles).
        if (_fallbackDictionary is not null && _fallbackDictionary.Contains(key))
        {
            return _fallbackDictionary[key]?.ToString() ?? fallback;
        }

        return fallback;
    }

    /// <inheritdoc />
    public void SetLanguage(string languageCode)
    {
        var normalized = Normalize(languageCode);

        // Cargamos (y cacheamos) el diccionario del idioma solicitado.
        var dictionary = LoadDictionary(normalized);

        // El fallback ingles se carga una sola vez.
        _fallbackDictionary ??= LoadDictionary(DefaultLanguageCode);

        var app = Application.Current;
        if (app is not null)
        {
            // Quitamos el diccionario anterior (si lo habia) e insertamos el nuevo.
            if (_activeDictionary is not null)
            {
                app.Resources.MergedDictionaries.Remove(_activeDictionary);
            }

            // El diccionario ingles ya viene fusionado de forma estatica en
            // App.xaml como red de seguridad. Evitamos anadirlo por duplicado
            // cuando el idioma activo es precisamente el de por defecto.
            var alreadyMerged = app.Resources.MergedDictionaries.Contains(dictionary);

            if (dictionary is not null && !alreadyMerged)
            {
                app.Resources.MergedDictionaries.Add(dictionary);
            }
        }

        _activeDictionary = dictionary;
        CurrentLanguageCode = normalized;
        CurrentCulture = CultureInfo.GetCultureInfo(normalized);

        // Sincronizamos la cultura del hilo para que los formatos numericos y
        // de fecha acompanen al idioma elegido.
        CultureInfo.DefaultThreadCurrentCulture = CurrentCulture;
        CultureInfo.DefaultThreadCurrentUICulture = CurrentCulture;

        LanguageChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// Normaliza un codigo de idioma a uno soportado. Acepta tanto el codigo
    /// completo ("es-ES") como el neutro ("es"), y cae a en-US si no hay
    /// coincidencia.
    /// </summary>
    private static string Normalize(string? languageCode)
    {
        if (string.IsNullOrWhiteSpace(languageCode))
        {
            return DefaultLanguageCode;
        }

        var exact = DefaultLanguageCode.Equals(languageCode, StringComparison.OrdinalIgnoreCase);
        if (exact)
        {
            return DefaultLanguageCode;
        }

        var supported = new[] { "en-US", "es-ES", "fr-FR", "de-DE" };

        foreach (var code in supported)
        {
            if (code.Equals(languageCode, StringComparison.OrdinalIgnoreCase))
            {
                return code;
            }
        }

        // Coincidencia por idioma neutro (ej: "es" -> "es-ES").
        var neutral = languageCode.Split('-')[0];
        foreach (var code in supported)
        {
            if (code.StartsWith(neutral, StringComparison.OrdinalIgnoreCase))
            {
                return code;
            }
        }

        return DefaultLanguageCode;
    }

    /// <summary>
    /// Carga (con cache) el <see cref="ResourceDictionary"/> de un idioma.
    /// Devuelve null si el recurso no existe, para no romper la app.
    /// </summary>
    private ResourceDictionary? LoadDictionary(string languageCode)
    {
        if (_cache.TryGetValue(languageCode, out var cached))
        {
            return cached;
        }

        try
        {
            var uri = new Uri(
                $"{ResourceDictionaryPrefix}{languageCode}.xaml",
                UriKind.Relative);

            var dictionary = new ResourceDictionary { Source = uri };
            _cache[languageCode] = dictionary;
            return dictionary;
        }
        catch (Exception)
        {
            // Un idioma sin diccionario simplemente no aporta traducciones;
            // el fallback ingles cubre el resto.
            return null;
        }
    }
}
