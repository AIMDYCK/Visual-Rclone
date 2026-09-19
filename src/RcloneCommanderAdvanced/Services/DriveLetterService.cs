using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using RcloneCommanderAdvanced.Services.Abstractions;

namespace RcloneCommanderAdvanced.Services;

/// <summary>
/// Enumera las letras de unidad de Windows usando DriveInfo y aplica las
/// letras reservadas definidas por el usuario en la configuracion.
/// </summary>
public sealed class DriveLetterService : IDriveLetterService
{
    private readonly ISettingsService _settingsService;

    public DriveLetterService(ISettingsService settingsService)
    {
        _settingsService = settingsService;
    }

    /// <inheritdoc />
    public IReadOnlyList<string> GetUsedDriveLetters()
    {
        var used = new List<string>();

        try
        {
            foreach (var drive in DriveInfo.GetDrives())
            {
                var name = drive.Name; // Formato "C:\"
                if (name.Length >= 1)
                {
                    used.Add(name[..1].ToUpperInvariant());
                }
            }
        }
        catch (IOException)
        {
            // Si falla la enumeracion devolvemos lo que tengamos.
        }

        return used;
    }

    /// <inheritdoc />
    public IReadOnlyList<string> GetAvailableDriveLetters()
    {
        var used = new HashSet<string>(GetUsedDriveLetters(), StringComparer.OrdinalIgnoreCase);

        var reserved = _settingsService.Current.ReservedDriveLetters
            ?? new List<string>();

        foreach (var letter in reserved)
        {
            if (!string.IsNullOrWhiteSpace(letter))
            {
                used.Add(letter.Trim().ToUpperInvariant());
            }
        }

        var available = new List<string>();

        // Rango D..Z (A, B y C quedan excluidas por convencion de Windows).
        for (var c = 'D'; c <= 'Z'; c++)
        {
            var letter = c.ToString();
            if (!used.Contains(letter))
            {
                available.Add(letter);
            }
        }

        return available;
    }

    /// <inheritdoc />
    public bool IsDriveLetterAvailable(string letter)
    {
        if (string.IsNullOrWhiteSpace(letter))
        {
            return false;
        }

        var normalized = letter.Trim().TrimEnd(':').ToUpperInvariant();

        return GetAvailableDriveLetters()
            .Contains(normalized, StringComparer.OrdinalIgnoreCase);
    }

    /// <inheritdoc />
    public bool IsDriveReady(string letter)
    {
        if (string.IsNullOrWhiteSpace(letter))
        {
            return false;
        }

        var normalized = letter.Trim().TrimEnd(':').ToUpperInvariant();

        try
        {
            return DriveInfo.GetDrives().Any(d =>
                d.Name.StartsWith(normalized, StringComparison.OrdinalIgnoreCase) &&
                d.IsReady);
        }
        catch (IOException)
        {
            return false;
        }
    }

    /// <inheritdoc />
    public string GetVolumeLabel(string letter)
    {
        if (string.IsNullOrWhiteSpace(letter))
        {
            return string.Empty;
        }

        var normalized = letter.Trim().TrimEnd(':').ToUpperInvariant();

        try
        {
            var drive = DriveInfo.GetDrives().FirstOrDefault(d =>
                d.Name.StartsWith(normalized, StringComparison.OrdinalIgnoreCase));

            return drive?.IsReady == true ? drive.VolumeLabel : string.Empty;
        }
        catch (IOException)
        {
            return string.Empty;
        }
    }
}
