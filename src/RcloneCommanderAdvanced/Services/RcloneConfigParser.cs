using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using RcloneCommanderAdvanced.Models;
using RcloneCommanderAdvanced.Services.Abstractions;

namespace RcloneCommanderAdvanced.Services;

/// <summary>
/// Implementacion del parser de rclone.conf.
///
/// Estrategia: parseo INI linea a linea combinado con Regex para tolerar
/// las particularidades del formato de rclone (bloques [nombre], pares
/// clave = valor, comentarios con # o ; y valores con espacios).
///
/// NO se hardcodea ningun nombre de remoto: todo se descubre en runtime.
/// </summary>
public sealed class RcloneConfigParser : IRcloneConfigParser
{
    // [nombre_del_remoto]  -> captura el nombre permitiendo espacios y puntos.
    private static readonly Regex SectionRegex =
        new(@"^\s*\[(?<name>[^\]]+)\]\s*$", RegexOptions.Compiled);

    // clave = valor  -> tolera espacios alrededor del '=' y valores vacios.
    private static readonly Regex KeyValueRegex =
        new(@"^\s*(?<key>[^=;#]+?)\s*=\s*(?<value>.*)$", RegexOptions.Compiled);

    private readonly ISettingsService _settingsService;

    public RcloneConfigParser(ISettingsService settingsService)
    {
        _settingsService = settingsService;
    }

    /// <inheritdoc />
    public string ResolvedConfigPath
    {
        get
        {
            var configured = _settingsService.Current.RcloneConfigPath;
            if (!string.IsNullOrWhiteSpace(configured))
            {
                return Environment.ExpandEnvironmentVariables(configured);
            }

            // Ruta predeterminada de rclone en Windows.
            var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            return Path.Combine(appData, "rclone", "rclone.conf");
        }
    }

    /// <inheritdoc />
    public bool ConfigFileExists() => File.Exists(ResolvedConfigPath);

    /// <inheritdoc />
    public IReadOnlyList<RemoteEntry> Parse()
    {
        var path = ResolvedConfigPath;

        if (!File.Exists(path))
        {
            return Array.Empty<RemoteEntry>();
        }

        try
        {
            var content = File.ReadAllText(path);
            return ParseContent(content);
        }
        catch (IOException)
        {
            return Array.Empty<RemoteEntry>();
        }
        catch (UnauthorizedAccessException)
        {
            return Array.Empty<RemoteEntry>();
        }
    }

    /// <inheritdoc />
    public IReadOnlyList<RemoteEntry> ParseContent(string iniContent)
    {
        var results = new List<RemoteEntry>();

        if (string.IsNullOrWhiteSpace(iniContent))
        {
            return results;
        }

        RemoteEntry? current = null;

        foreach (var rawLine in iniContent.Split('\n'))
        {
            var line = rawLine.TrimEnd('\r');

            // Ignorar lineas vacias y comentarios.
            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            var trimmed = line.TrimStart();
            if (trimmed.StartsWith('#') || trimmed.StartsWith(';'))
            {
                continue;
            }

            var sectionMatch = SectionRegex.Match(line);
            if (sectionMatch.Success)
            {
                // Cerrar el bloque anterior y abrir uno nuevo.
                if (current is not null)
                {
                    results.Add(current);
                }

                current = new RemoteEntry
                {
                    Name = sectionMatch.Groups["name"].Value.Trim()
                };
                continue;
            }

            if (current is null)
            {
                // Claves fuera de cualquier seccion: se ignoran.
                continue;
            }

            var kvMatch = KeyValueRegex.Match(line);
            if (!kvMatch.Success)
            {
                continue;
            }

            var key = kvMatch.Groups["key"].Value.Trim();
            var value = kvMatch.Groups["value"].Value.Trim();

            if (key.Length == 0)
            {
                continue;
            }

            current.RawKeys[key] = value;

            if (key.Equals("type", StringComparison.OrdinalIgnoreCase))
            {
                current.Type = value;
            }
            else if (key.Equals("root_folder_id", StringComparison.OrdinalIgnoreCase) ||
                     key.Equals("root_folder", StringComparison.OrdinalIgnoreCase))
            {
                current.RootFolder = value;
            }
        }

        if (current is not null)
        {
            results.Add(current);
        }

        return results;
    }
}
