namespace RcloneCommanderAdvanced.Services.Abstractions;

/// <summary>
/// Contrato del servicio de inicializacion (bootstrapper) del entorno de
/// configuracion de rclone.
///
/// PROBLEMA QUE RESUELVE:
/// En un entorno limpio (Windows Sandbox, PC recien formateado, primer
/// arranque) la carpeta <c>%APPDATA%\rclone</c> y el archivo
/// <c>rclone.conf</c> NO existen. Cualquier operacion que asuma su presencia
/// (lectura de remotos, comprobacion de cifrado, creacion de un remoto)
/// falla o se comporta de forma impredecible.
///
/// Este servicio garantiza, de forma idempotente y no destructiva, que:
///  1. El directorio contenedor del rclone.conf exista.
///  2. El archivo rclone.conf exista (vacio si es la primera vez).
///
/// NUNCA sobrescribe un archivo existente: si el usuario ya tiene remotos
/// configurados, se respetan intactos.
/// </summary>
public interface IRcloneConfigBootstrapper
{
    /// <summary>
    /// Ruta absoluta del rclone.conf que se va a garantizar.
    /// </summary>
    string ConfigPath { get; }

    /// <summary>
    /// Ruta absoluta del directorio que contiene el rclone.conf.
    /// </summary>
    string ConfigDirectory { get; }

    /// <summary>
    /// Garantiza que el directorio y el archivo de configuracion existan.
    /// Es idempotente: llamarlo varias veces no tiene efectos secundarios.
    /// No lanza excepcion si no puede crear los recursos; en su lugar
    /// devuelve un resultado que describe lo ocurrido.
    /// </summary>
    /// <returns>
    /// Resultado de la inicializacion, indicando si el entorno quedo listo
    /// y si hubo que crear el directorio y/o el archivo.
    /// </returns>
    RcloneConfigBootstrapResult EnsureInitialized();
}

/// <summary>
/// Resultado de la inicializacion del entorno de configuracion de rclone.
/// </summary>
/// <param name="Success">true si el directorio y el archivo existen al terminar.</param>
/// <param name="DirectoryCreated">true si el directorio tuvo que crearse.</param>
/// <param name="FileCreated">true si el archivo tuvo que crearse (estaba ausente).</param>
/// <param name="ConfigPath">Ruta absoluta del rclone.conf garantizado.</param>
/// <param name="ErrorMessage">Mensaje de error si <paramref name="Success"/> es false.</param>
public sealed record RcloneConfigBootstrapResult(
    bool Success,
    bool DirectoryCreated,
    bool FileCreated,
    string ConfigPath,
    string? ErrorMessage = null)
{
    /// <summary>true si no hubo que crear nada (el entorno ya estaba listo).</summary>
    public bool AlreadyInitialized => Success && !DirectoryCreated && !FileCreated;
}
