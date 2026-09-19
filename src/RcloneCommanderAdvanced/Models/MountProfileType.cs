namespace RcloneCommanderAdvanced.Models;

/// <summary>
/// Perfiles de montaje predefinidos. Cada perfil inyecta un conjunto de
/// argumentos en la linea de comandos de rclone.exe.
/// </summary>
public enum MountProfileType
{
    /// <summary>Perfil 1 - Lectura Segura (Anti-Ransomware).</summary>
    SafeRead = 0,

    /// <summary>Perfil 2 - Alto Rendimiento (Cache SSD 5GB).</summary>
    HighPerformance = 1,

    /// <summary>Perfil 3 - Transferencia Masiva (Buffer RAM, cero escrituras SSD).</summary>
    BulkTransfer = 2,

    /// <summary>Perfil personalizado: el usuario define los argumentos extra.</summary>
    Custom = 99
}
