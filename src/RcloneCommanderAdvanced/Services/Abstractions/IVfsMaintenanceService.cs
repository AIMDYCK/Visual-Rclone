using System.Threading;
using System.Threading.Tasks;

namespace RcloneCommanderAdvanced.Services.Abstractions;

/// <summary>
/// Contrato del modulo de mantenimiento del cache VFS en disco.
/// </summary>
public interface IVfsMaintenanceService
{
    /// <summary>Ruta de la carpeta de cache VFS (%LOCALAPPDATA%\rclone\vfs).</summary>
    string VfsCachePath { get; }

    /// <summary>Ruta de la carpeta de metadatos VFS (%LOCALAPPDATA%\rclone\vfsMeta).</summary>
    string VfsMetaPath { get; }

    /// <summary>Calcula el peso total del cache VFS en bytes.</summary>
    Task<long> GetVfsCacheSizeBytesAsync(CancellationToken cancellationToken = default);

    /// <summary>Calcula el peso total de los metadatos VFS en bytes.</summary>
    Task<long> GetVfsMetaSizeBytesAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Purga completa: mata los procesos de rclone y elimina fisicamente
    /// las carpetas vfs y vfsMeta mediante System.IO.
    /// </summary>
    Task<PurgeResult> PurgeAsync(CancellationToken cancellationToken = default);

    /// <summary>Formatea un tamano en bytes a texto legible (KB/MB/GB).</summary>
    string FormatSize(long bytes);
}

/// <summary>Resultado de una operacion de purga.</summary>
public sealed class PurgeResult
{
    public bool Success { get; init; }
    public long BytesFreed { get; init; }
    public int ProcessesKilled { get; init; }
    public string Message { get; init; } = string.Empty;
}
