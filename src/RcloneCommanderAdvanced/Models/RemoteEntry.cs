using System.Collections.Generic;

namespace RcloneCommanderAdvanced.Models;

/// <summary>
/// Representa un remoto detectado en el archivo rclone.conf.
/// Es el resultado del parseo INI y NO contiene configuracion hardcodeada:
/// el nombre y el tipo provienen siempre del archivo del usuario.
/// </summary>
public sealed class RemoteEntry
{
    /// <summary>Nombre del remoto tal como aparece en rclone.conf (ej: "mi_drive").</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>Tipo de backend declarado en el INI (ej: "drive", "s3", "onedrive").</summary>
    public string Type { get; set; } = string.Empty;

    /// <summary>Ruta del subdirectorio remoto opcional (clave "root_folder_id" o similar).</summary>
    public string? RootFolder { get; set; }

    /// <summary>Todas las claves del bloque INI, utiles para diagnostico o UI avanzada.</summary>
    public Dictionary<string, string> RawKeys { get; set; } = new();

    /// <summary>
    /// Indica si la unidad debe montarse como "oculta": la letra de unidad se
    /// oculta en el Explorador de Windows modificando la clave de registro
    /// <c>NoDrives</c> (HKCU\...\Policies\Explorer).
    /// </summary>
    public bool IsHiddenDrive { get; set; }

    /// <summary>Letra de unidad asignada por el usuario (ej: "X"). Vacio = sin asignar.</summary>
    public string DriveLetter { get; set; } = string.Empty;

    /// <summary>
    /// Modo de montaje elegido por el usuario: unidad de red
    /// (<c>--network-mode</c>) o disco fisico/local (sin ese flag).
    /// </summary>
    public MountMode MountMode { get; set; } = MountMode.NetworkDrive;

    /// <summary>Perfil de montaje seleccionado por el usuario.</summary>
    public MountProfileType Profile { get; set; } = MountProfileType.SafeRead;

    /// <summary>Argumentos extra definidos por el usuario (solo si Profile == Custom).</summary>
    public string CustomArguments { get; set; } = string.Empty;

    // ---------------------------------------------------------------------
    // FASE 1: Optimizacion VFS (Virtual File System) por remoto.
    //
    // Estos tres parametros son los que mas impactan el rendimiento real de
    // un montaje rclone en Windows. Se exponen en la tarjeta del dashboard
    // con Binding bidireccional y se inyectan en la linea de comandos de
    // `rclone mount` solo cuando el usuario los ha configurado (no vacios).
    //
    // Vacio = no se inyecta el flag y rclone usa su propio valor por defecto,
    // salvo VfsCacheMode que cae a "full" (estandar recomendado en Windows).
    // ---------------------------------------------------------------------

    /// <summary>
    /// Modo de cache VFS (<c>--vfs-cache-mode</c>): off, minimal, writes, full.
    /// Vacio = se usa "full" (estandar recomendado para Windows).
    /// </summary>
    public string VfsCacheMode { get; set; } = string.Empty;

    /// <summary>
    /// Tamano del buffer de lectura (<c>--buffer-size</c>), ej. "16M", "64M",
    /// "128M". Vacio = no se inyecta (rclone usa su valor por defecto).
    /// </summary>
    public string BufferSize { get; set; } = string.Empty;

    /// <summary>
    /// Tiempo de cache del listado de directorios (<c>--dir-cache-time</c>),
    /// ej. "1h", "24h", "72h". Vacio = no se inyecta (rclone usa su valor por
    /// defecto).
    /// </summary>
    public string DirCacheTime { get; set; } = string.Empty;

    /// <summary>Indica si el remoto debe montarse al pulsar "Montar Todo".</summary>
    public bool IsEnabled { get; set; }

    /// <summary>Etiqueta legible para la UI.</summary>
    public string DisplayLabel =>
        string.IsNullOrWhiteSpace(DriveLetter) ? Name : $"{Name}  [{DriveLetter}:]";
}
