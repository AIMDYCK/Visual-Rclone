using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using RcloneCommanderAdvanced.Models;

namespace RcloneCommanderAdvanced.Services.Abstractions;

/// <summary>
/// Resultado de una operacion de gestion de remotos (crear/eliminar).
/// </summary>
public sealed class RcloneConfigOperationResult
{
    /// <summary>Indica si la operacion finalizo correctamente.</summary>
    public bool Success { get; init; }

    /// <summary>Codigo de salida del proceso rclone (-1 si no se pudo iniciar).</summary>
    public int ExitCode { get; init; }

    /// <summary>Salida estandar capturada del proceso.</summary>
    public string StandardOutput { get; init; } = string.Empty;

    /// <summary>Salida de error capturada del proceso.</summary>
    public string StandardError { get; init; } = string.Empty;

    /// <summary>Mensaje legible para mostrar en la UI.</summary>
    public string Message { get; init; } = string.Empty;

    /// <summary>Constructor de exito.</summary>
    public static RcloneConfigOperationResult Ok(string message, string stdout = "", string stderr = "")
        => new() { Success = true, ExitCode = 0, Message = message, StandardOutput = stdout, StandardError = stderr };

    /// <summary>Constructor de fallo.</summary>
    public static RcloneConfigOperationResult Fail(string message, int exitCode = -1, string stdout = "", string stderr = "")
        => new() { Success = false, ExitCode = exitCode, Message = message, StandardOutput = stdout, StandardError = stderr };
}

/// <summary>
/// Contrato del servicio que gestiona el archivo rclone.conf de forma segura,
/// delegando SIEMPRE en el binario oficial de rclone (nunca editando el INI a
/// mano). Esto garantiza que la sintaxis, el cifrado de secretos y las claves
/// especificas de cada proveedor se generen correctamente.
/// </summary>
public interface IRcloneConfigManager
{
    /// <summary>
    /// Crea (o actualiza) un remoto ejecutando:
    /// <c>rclone config create {name} {type} {key=value ...} --config "{path}"</c>.
    /// </summary>
    /// <param name="name">Nombre del remoto (sin espacios).</param>
    /// <param name="type">Tipo de backend rclone (drive, onedrive, dropbox, local, s3...).</param>
    /// <param name="parameters">Pares clave/valor adicionales (client_id, client_secret, etc.).</param>
    /// <param name="onOutput">Callback opcional invocado con cada linea de salida (para indicadores de progreso).</param>
    /// <param name="cancellationToken">Token de cancelacion.</param>
    Task<RcloneConfigOperationResult> CreateRemoteAsync(
        string name,
        string type,
        IReadOnlyDictionary<string, string>? parameters = null,
        Action<string>? onOutput = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Renombra un remoto existente ejecutando:
    /// <c>rclone config rename {oldName} {newName} --config "{path}"</c>.
    /// rclone conserva intactos todos los parametros del remoto al renombrarlo.
    /// </summary>
    /// <param name="oldName">Nombre actual del remoto.</param>
    /// <param name="newName">Nombre nuevo (sin espacios ni caracteres prohibidos).</param>
    /// <param name="cancellationToken">Token de cancelacion.</param>
    Task<RcloneConfigOperationResult> RenameRemoteAsync(
        string oldName,
        string newName,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Actualiza un remoto existente ejecutando:
    /// <c>rclone config update {name} {key=value ...} --config "{path}"</c>.
    /// A diferencia de <see cref="CreateRemoteAsync"/>, NO cambia el tipo de
    /// backend (rclone no lo permite); solo sobrescribe los parametros
    /// indicados, respetando el cifrado de secretos del archivo.
    /// </summary>
    /// <param name="name">Nombre del remoto existente a modificar.</param>
    /// <param name="parameters">Pares clave/valor a sobrescribir (root_folder_id, client_id, etc.).</param>
    /// <param name="onOutput">Callback opcional invocado con cada linea de salida.</param>
    /// <param name="cancellationToken">Token de cancelacion.</param>
    Task<RcloneConfigOperationResult> UpdateRemoteAsync(
        string name,
        IReadOnlyDictionary<string, string>? parameters = null,
        Action<string>? onOutput = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Elimina un remoto ejecutando <c>rclone config delete {name} --config "{path}"</c>.
    /// </summary>
    Task<RcloneConfigOperationResult> DeleteRemoteAsync(
        string name,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Reautentica un remoto ejecutando <c>rclone config reconnect {name}: --config "{path}"</c>.
    /// rclone abre el navegador, completa el flujo OAuth y guarda el token nuevo
    /// en rclone.conf sin que el usuario tenga que tocar el JSON a mano.
    /// </summary>
    /// <param name="name">Nombre del remoto existente (sin los dos puntos finales).</param>
    /// <param name="onOutput">Callback opcional invocado con cada linea de salida.</param>
    /// <param name="cancellationToken">Token de cancelacion.</param>
    Task<RcloneConfigOperationResult> ReconnectRemoteAsync(
        string name,
        Action<string>? onOutput = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Obtiene el esquema completo de un backend ejecutando
    /// <c>rclone config providers</c> y extrayendo la entrada cuyo prefijo
    /// coincide. Devuelve <c>null</c> si rclone no esta disponible o el backend
    /// no existe. El resultado se cachea en memoria por tipo de backend.
    /// </summary>
    /// <param name="type">Tipo de backend rclone (drive, onedrive, s3...).</param>
    /// <param name="cancellationToken">Token de cancelacion.</param>
    Task<ProviderSchema?> GetProviderSchemaAsync(
        string type,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Comprueba si ya existe un remoto con ese nombre en el rclone.conf activo.
    /// </summary>
    bool RemoteExists(string name);

    /// <summary>
    /// Valida que un remoto responde ejecutando <c>rclone lsd {name}: --config "{path}"</c>.
    /// Es una operacion ligera de solo lectura que confirma que las credenciales
    /// y el endpoint funcionan antes de dar por buena la creacion del remoto.
    /// </summary>
    /// <param name="name">Nombre del remoto a probar (sin los dos puntos finales).</param>
    /// <param name="onOutput">Callback opcional invocado con cada linea de salida.</param>
    /// <param name="cancellationToken">Token de cancelacion.</param>
    Task<RcloneConfigOperationResult> TestRemoteAsync(
        string name,
        Action<string>? onOutput = null,
        CancellationToken cancellationToken = default);
}
