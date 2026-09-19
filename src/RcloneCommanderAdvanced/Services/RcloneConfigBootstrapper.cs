using System;
using System.IO;
using RcloneCommanderAdvanced.Services.Abstractions;

namespace RcloneCommanderAdvanced.Services;

/// <summary>
/// Implementacion del bootstrapper del entorno de configuracion de rclone.
///
/// Estrategia (idempotente y NO destructiva):
///  1. Resuelve la ruta efectiva del rclone.conf a traves de
///     <see cref="IRcloneConfigParser.ResolvedConfigPath"/>, de modo que se
///     respeta cualquier ruta personalizada que el usuario haya configurado.
///  2. Si el directorio contenedor no existe, lo crea.
///  3. Si el archivo no existe, lo crea vacio.
///
/// IMPORTANTE: nunca sobrescribe ni trunca un archivo existente. Si el usuario
/// ya tiene remotos configurados, permanecen intactos.
///
/// No lanza excepciones: cualquier fallo de E/S se captura y se devuelve como
/// un <see cref="RcloneConfigBootstrapResult"/> con Success = false, para que
/// el arranque pueda decidir como continuar sin morir.
/// </summary>
public sealed class RcloneConfigBootstrapper : IRcloneConfigBootstrapper
{
    private readonly IRcloneConfigParser _configParser;

    public RcloneConfigBootstrapper(IRcloneConfigParser configParser)
    {
        _configParser = configParser;
    }

    /// <inheritdoc />
    public string ConfigPath => _configParser.ResolvedConfigPath;

    /// <inheritdoc />
    public string ConfigDirectory
    {
        get
        {
            var path = ConfigPath;
            var directory = Path.GetDirectoryName(path);
            return string.IsNullOrEmpty(directory) ? path : directory;
        }
    }

    /// <inheritdoc />
    public RcloneConfigBootstrapResult EnsureInitialized()
    {
        var path = ConfigPath;
        var directory = ConfigDirectory;

        var directoryCreated = false;
        var fileCreated = false;

        try
        {
            // 1. Garantizar el directorio contenedor.
            if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);
                directoryCreated = true;
            }

            // 2. Garantizar el archivo de configuracion.
            //    Solo se crea si NO existe: jamas se sobrescribe.
            if (!File.Exists(path))
            {
                // Un rclone.conf vacio es valido: rclone lo interpreta como
                // "sin remotos" y la UI mostrara el estado vacio normal.
                File.WriteAllText(path, string.Empty);
                fileCreated = true;
            }

            return new RcloneConfigBootstrapResult(
                Success: true,
                DirectoryCreated: directoryCreated,
                FileCreated: fileCreated,
                ConfigPath: path);
        }
        catch (Exception ex) when (ex is IOException
                                       or UnauthorizedAccessException
                                       or NotSupportedException
                                       or ArgumentException
                                       or System.Security.SecurityException)
        {
            // No abortamos el arranque: devolvemos el fallo para que la capa
            // superior lo registre y continue en modo degradado (la UI
            // mostrara el estado vacio y permitira reintentar).
            return new RcloneConfigBootstrapResult(
                Success: false,
                DirectoryCreated: directoryCreated,
                FileCreated: fileCreated,
                ConfigPath: path,
                ErrorMessage: ex.Message);
        }
    }
}
