using System;
using System.Threading.Tasks;
using RcloneCommanderAdvanced.Models;

namespace RcloneCommanderAdvanced.Services.Abstractions;

/// <summary>
/// Contrato de persistencia de la configuracion del usuario (appsettings.json).
/// </summary>
public interface ISettingsService
{
    /// <summary>Configuracion actual en memoria.</summary>
    AppSettings Current { get; }

    /// <summary>Ruta fisica del archivo de configuracion.</summary>
    string SettingsFilePath { get; }

    /// <summary>Se dispara cada vez que la configuracion se guarda correctamente.</summary>
    event EventHandler<AppSettings>? SettingsSaved;

    /// <summary>Carga la configuracion desde disco. Si no existe, crea una por defecto.</summary>
    Task<AppSettings> LoadAsync();

    /// <summary>Persiste la configuracion actual en disco (escritura atomica).</summary>
    Task SaveAsync();

    /// <summary>Persiste una instancia concreta y la deja como actual.</summary>
    Task SaveAsync(AppSettings settings);
}
