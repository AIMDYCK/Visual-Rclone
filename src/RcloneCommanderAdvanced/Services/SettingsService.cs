using System;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using RcloneCommanderAdvanced.Models;
using RcloneCommanderAdvanced.Services.Abstractions;

namespace RcloneCommanderAdvanced.Services;

/// <summary>
/// Persistencia de la configuracion en un appsettings.json ubicado en
/// %LOCALAPPDATA%\RcloneCommanderAdvanced\appsettings.json.
///
/// Se usa escritura atomica (archivo .tmp + File.Replace) para evitar
/// corromper la configuracion si la app se cierra abruptamente.
/// </summary>
public sealed class SettingsService : ISettingsService
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter() }
    };

    private readonly SemaphoreSlim _ioLock = new(1, 1);

    public SettingsService()
    {
        var baseDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "RcloneCommanderAdvanced");

        Directory.CreateDirectory(baseDir);
        SettingsFilePath = Path.Combine(baseDir, "appsettings.json");
    }

    /// <inheritdoc />
    public AppSettings Current { get; private set; } = new();

    /// <inheritdoc />
    public string SettingsFilePath { get; }

    /// <inheritdoc />
    public event EventHandler<AppSettings>? SettingsSaved;

    /// <inheritdoc />
    public async Task<AppSettings> LoadAsync()
    {
        await _ioLock.WaitAsync().ConfigureAwait(false);
        try
        {
            if (!File.Exists(SettingsFilePath))
            {
                // Primer arranque: creamos el archivo con los valores por defecto
                // para que el usuario tenga una plantilla editable desde el inicio.
                Current = new AppSettings();
                await WriteToDiskAsync(Current).ConfigureAwait(false);
                return Current;
            }

            var json = await File.ReadAllTextAsync(SettingsFilePath).ConfigureAwait(false);

            if (string.IsNullOrWhiteSpace(json))
            {
                Current = new AppSettings();
                return Current;
            }

            var loaded = JsonSerializer.Deserialize<AppSettings>(json, SerializerOptions);
            Current = loaded ?? new AppSettings();
            return Current;
        }
        catch (JsonException)
        {
            // Configuracion corrupta: se respalda y se regenera.
            TryBackupCorruptedFile();
            Current = new AppSettings();
            return Current;
        }
        finally
        {
            _ioLock.Release();
        }
    }

    /// <inheritdoc />
    public Task SaveAsync() => SaveAsync(Current);

    /// <inheritdoc />
    public async Task SaveAsync(AppSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);

        await _ioLock.WaitAsync().ConfigureAwait(false);
        try
        {
            Current = settings;
            await WriteToDiskAsync(settings).ConfigureAwait(false);
            SettingsSaved?.Invoke(this, settings);
        }
        finally
        {
            _ioLock.Release();
        }
    }

    /// <summary>
    /// Escritura atomica en disco. Debe invocarse con el _ioLock ya adquirido.
    /// </summary>
    private async Task WriteToDiskAsync(AppSettings settings)
    {
        var json = JsonSerializer.Serialize(settings, SerializerOptions);
        var tempPath = SettingsFilePath + ".tmp";

        await File.WriteAllTextAsync(tempPath, json).ConfigureAwait(false);

        if (File.Exists(SettingsFilePath))
        {
            File.Replace(tempPath, SettingsFilePath, null);
        }
        else
        {
            File.Move(tempPath, SettingsFilePath);
        }
    }

    private void TryBackupCorruptedFile()
    {
        try
        {
            var backup = SettingsFilePath + $".corrupt-{DateTime.Now:yyyyMMddHHmmss}.bak";
            File.Copy(SettingsFilePath, backup, overwrite: true);
        }
        catch (IOException)
        {
            // El respaldo es best-effort.
        }
    }
}
