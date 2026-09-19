using System;
using System.Diagnostics;
using System.IO;
using Microsoft.Win32;
using RcloneCommanderAdvanced.Services.Abstractions;

namespace RcloneCommanderAdvanced.Services;

/// <summary>
/// Localiza rclone.exe siguiendo este orden de prioridad:
/// 1. Ruta explicita configurada por el usuario.
/// 2. Directorio interno gestionado por la app (dependencias descargadas).
/// 3. Ejecutable junto a la aplicacion (distribucion portable).
/// 4. Busqueda en la variable de entorno PATH.
/// 5. Rutas de instalacion comunes en Windows.
///
/// Tambien detecta WinFsp, requisito indispensable para montar unidades.
/// </summary>
public sealed class RcloneLocator : IRcloneLocator
{
    private readonly ISettingsService _settingsService;

    public RcloneLocator(ISettingsService settingsService)
    {
        _settingsService = settingsService;
    }

    /// <inheritdoc />
    public string InternalBinDirectory =>
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "RcloneCommanderAdvanced",
            "bin");

    /// <inheritdoc />
    public string? ResolveRclonePath()
    {
        // 1. Ruta configurada explicitamente.
        var configured = _settingsService.Current.RcloneExecutablePath;
        if (!string.IsNullOrWhiteSpace(configured))
        {
            var expanded = Environment.ExpandEnvironmentVariables(configured);
            if (File.Exists(expanded))
            {
                return expanded;
            }
        }

        // 2. Directorio interno gestionado por la app: aqui aterrizan las
        //    dependencias descargadas por el instalador de 1 clic.
        var internalPath = Path.Combine(InternalBinDirectory, "rclone.exe");
        if (File.Exists(internalPath))
        {
            return internalPath;
        }

        // 3. Junto a la aplicacion.
        var local = Path.Combine(AppContext.BaseDirectory, "rclone.exe");
        if (File.Exists(local))
        {
            return local;
        }

        // 4. PATH del sistema.
        var fromPath = SearchInPath("rclone.exe");
        if (fromPath is not null)
        {
            return fromPath;
        }

        // 5. Rutas comunes.
        var candidates = new[]
        {
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "rclone", "rclone.exe"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "rclone", "rclone.exe"),
            @"C:\rclone\rclone.exe"
        };

        foreach (var candidate in candidates)
        {
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        return null;
    }

    /// <inheritdoc />
    public bool IsRcloneAvailable() => ResolveRclonePath() is not null;

    /// <inheritdoc />
    public bool IsWinFspInstalled()
    {
        // WinFsp registra su DLL de servicio en el sistema.
        var system32 = Environment.GetFolderPath(Environment.SpecialFolder.System);
        if (File.Exists(Path.Combine(system32, "winfsp-x64.dll")))
        {
            return true;
        }

        // Comprobacion alternativa via registro de Windows.
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\WOW6432Node\WinFsp");
            if (key is not null)
            {
                return true;
            }

            using var nativeKey = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\WinFsp");
            return nativeKey is not null;
        }
        catch (System.Security.SecurityException)
        {
            return false;
        }
    }

    /// <inheritdoc />
    public string GetRcloneVersion()
    {
        var path = ResolveRclonePath();
        if (path is null)
        {
            return string.Empty;
        }

        try
        {
            using var process = Process.Start(new ProcessStartInfo
            {
                FileName = path,
                Arguments = "version",
                RedirectStandardOutput = true,
                UseShellExecute = false,
                CreateNoWindow = true
            });

            if (process is null)
            {
                return string.Empty;
            }

            var output = process.StandardOutput.ReadToEnd();
            process.WaitForExit(5000);

            // La primera linea suele ser "rclone v1.xx.x".
            var firstLine = output.Split('\n')[0].Trim();
            return firstLine;
        }
        catch (Exception ex) when (ex is IOException or InvalidOperationException)
        {
            return string.Empty;
        }
    }

    private static string? SearchInPath(string executable)
    {
        var pathVariable = Environment.GetEnvironmentVariable("PATH");
        if (string.IsNullOrWhiteSpace(pathVariable))
        {
            return null;
        }

        foreach (var dir in pathVariable.Split(Path.PathSeparator))
        {
            if (string.IsNullOrWhiteSpace(dir))
            {
                continue;
            }

            try
            {
                var full = Path.Combine(dir.Trim(), executable);
                if (File.Exists(full))
                {
                    return full;
                }
            }
            catch (ArgumentException)
            {
                // Entrada de PATH invalida: se ignora.
            }
        }

        return null;
    }
}
