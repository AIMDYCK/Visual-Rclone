namespace RcloneCommanderAdvanced.Services.Abstractions;

/// <summary>
/// FASE 4: contrato del servicio que gestiona el soporte de rclone.conf
/// cifrado.
///
/// rclone permite cifrar el archivo de configuracion con "rclone config
/// encryption". Cuando esta cifrado, el archivo empieza por la cabecera
/// <c>RCLONE_ENCRYPT_V0</c> y cualquier operacion (listremotes, mount, ...)
/// exige la contrasena en la variable de entorno <c>RCLONE_CONFIG_PASS</c>.
///
/// Este servicio:
///  - Detecta si el archivo esta cifrado.
///  - Valida una contrasena ejecutando rclone con la variable de entorno
///    establecida a nivel de PROCESO (nunca global).
///  - Mantiene la contrasena en memoria para reinyectarla en cada instancia
///    de rclone que lance la aplicacion.
/// </summary>
public interface IConfigEncryptionService
{
    /// <summary>
    /// Indica si el rclone.conf resuelto esta cifrado (cabecera
    /// <c>RCLONE_ENCRYPT_V0</c>).
    /// </summary>
    bool IsConfigEncrypted();

    /// <summary>
    /// True cuando ya se dispone de una contrasena valida en memoria para el
    /// archivo cifrado actual.
    /// </summary>
    bool HasPassword { get; }

    /// <summary>
    /// Valida la contrasena ejecutando <c>rclone listremotes</c> con
    /// <c>RCLONE_CONFIG_PASS</c> fijada a nivel de proceso. Si es correcta,
    /// la conserva en memoria para futuras invocaciones y devuelve true.
    /// Si es incorrecta, limpia la variable de entorno y devuelve false.
    /// </summary>
    /// <param name="password">Contrasena introducida por el usuario.</param>
    bool TryUnlock(string password);

    /// <summary>
    /// Aplica la contrasena almacenada a la variable de entorno de proceso
    /// antes de lanzar una instancia de rclone. No hace nada si no hay
    /// contrasena o el archivo no esta cifrado.
    /// </summary>
    void ApplyPasswordToEnvironment();

    /// <summary>
    /// Limpia la contrasena en memoria y elimina la variable de entorno de
    /// proceso. Se invoca al cerrar la aplicacion o al bloquear la sesion.
    /// </summary>
    void ClearPassword();
}
