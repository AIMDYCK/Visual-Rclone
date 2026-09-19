using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using RcloneCommanderAdvanced.Services.Abstractions;

namespace RcloneCommanderAdvanced.Services;

/// <summary>
/// FASE 2: implementacion de <see cref="IStartupShortcutService"/> basada en un
/// acceso directo (.lnk) en la carpeta Startup del usuario.
///
/// Se usa el objeto COM "WScript.Shell" mediante enlace tardio
/// (Type.GetTypeFromProgID) para no arrastrar una referencia COM al proyecto.
/// El acceso directo apunta al ejecutable actual y pasa el argumento
/// <c>--minimized</c>, que App.xaml.cs interpreta para arrancar sin mostrar
/// la ventana principal.
///
/// SEGURIDAD: unicamente se escribe dentro de la carpeta Startup del usuario.
/// No se modifica el Registro ni variables de entorno globales.
/// </summary>
public sealed class StartupShortcutService : IStartupShortcutService
{
    private const string ShortcutFileName = "Visual Rclone.lnk";
    private const string MinimizedArgument = "--minimized";
    private const string DefaultExecutableName = "RcloneCommanderAdvanced.exe";

    /// <inheritdoc />
    public bool IsEnabled => File.Exists(GetShortcutPath());

    /// <inheritdoc />
    public bool Enable()
    {
        try
        {
            var shortcutPath = GetShortcutPath();
            var targetPath = GetExecutablePath();

            if (string.IsNullOrWhiteSpace(targetPath) || !File.Exists(targetPath))
            {
                return false;
            }

            var shellType = Type.GetTypeFromProgID("WScript.Shell");
            if (shellType is null)
            {
                return false;
            }

            dynamic? shell = null;
            try
            {
                shell = Activator.CreateInstance(shellType);
                if (shell is null)
                {
                    return false;
                }

                dynamic shortcut = shell.CreateShortcut(shortcutPath);
                shortcut.TargetPath = targetPath;
                shortcut.Arguments = MinimizedArgument;
                shortcut.WorkingDirectory = Path.GetDirectoryName(targetPath) ?? string.Empty;
                shortcut.Description = "Visual Rclone (inicio minimizado)";
                shortcut.IconLocation = targetPath + ",0";
                shortcut.Save();
            }
            finally
            {
                if (shell is not null && Marshal.IsComObject(shell))
                {
                    Marshal.FinalReleaseComObject(shell);
                }
            }

            return File.Exists(shortcutPath);
        }
        catch (Exception ex) when (ex is COMException or IOException or UnauthorizedAccessException
                                       or InvalidCastException or MissingMethodException)
        {
            return false;
        }
    }

    /// <inheritdoc />
    public bool Disable()
    {
        try
        {
            var shortcutPath = GetShortcutPath();
            if (File.Exists(shortcutPath))
            {
                File.Delete(shortcutPath);
            }

            return !File.Exists(shortcutPath);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    /// <summary>Ruta completa del acceso directo dentro de la carpeta Startup.</summary>
    private static string GetShortcutPath()
    {
        var startupFolder = Environment.GetFolderPath(
            Environment.SpecialFolder.Startup,
            Environment.SpecialFolderOption.Create);

        return Path.Combine(startupFolder, ShortcutFileName);
    }

    /// <summary>
    /// Ruta del ejecutable actual. Se prefiere la ruta del proceso en ejecucion
    /// (que apunta al .exe real, no al host de dotnet) y se cae al directorio
    /// base de la aplicacion como respaldo.
    ///
    /// NOTA: no se usa Assembly.Location porque devuelve cadena vacia en
    /// aplicaciones empaquetadas como archivo unico (single-file), lo que
    /// romperia el acceso directo de inicio automatico en la version publicada.
    /// </summary>
    private static string GetExecutablePath()
    {
        try
        {
            var processPath = Environment.ProcessPath;
            if (!string.IsNullOrWhiteSpace(processPath) &&
                processPath.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
            {
                return processPath;
            }
        }
        catch (Exception ex) when (ex is InvalidOperationException or NotSupportedException)
        {
            // Continuamos con el respaldo basado en el directorio base.
        }

        // Respaldo seguro para single-file: el directorio base de la aplicacion.
        var baseDirectory = AppContext.BaseDirectory;
        if (!string.IsNullOrWhiteSpace(baseDirectory))
        {
            var exePath = Path.Combine(baseDirectory, DefaultExecutableName);
            if (File.Exists(exePath))
            {
                return exePath;
            }
        }

        return string.Empty;
    }
}
