using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using RcloneCommanderAdvanced.Services.Abstractions;

namespace RcloneCommanderAdvanced.Services;

/// <summary>
/// Modulo de mantenimiento del cache VFS.
///
/// Lee el peso real de %LOCALAPPDATA%\rclone\vfs y vfsMeta y permite
/// purgarlos fisicamente con System.IO tras matar los procesos de rclone
/// que mantendrian archivos bloqueados.
/// </summary>
public sealed class VfsMaintenanceService : IVfsMaintenanceService
{
    private readonly IMountProcessManager _processManager;

    public VfsMaintenanceService(IMountProcessManager processManager)
    {
        _processManager = processManager;

        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var rcloneDir = Path.Combine(localAppData, "rclone");

        VfsCachePath = Path.Combine(rcloneDir, "vfs");
        VfsMetaPath = Path.Combine(rcloneDir, "vfsMeta");
    }

    /// <inheritdoc />
    public string VfsCachePath { get; }

    /// <inheritdoc />
    public string VfsMetaPath { get; }

    /// <inheritdoc />
    public Task<long> GetVfsCacheSizeBytesAsync(CancellationToken cancellationToken = default) =>
        Task.Run(() => CalculateDirectorySize(VfsCachePath, cancellationToken), cancellationToken);

    /// <inheritdoc />
    public Task<long> GetVfsMetaSizeBytesAsync(CancellationToken cancellationToken = default) =>
        Task.Run(() => CalculateDirectorySize(VfsMetaPath, cancellationToken), cancellationToken);

    /// <inheritdoc />
    public async Task<PurgeResult> PurgeAsync(CancellationToken cancellationToken = default)
    {
        // 1. Calcular el peso antes de borrar.
        var before = await GetVfsCacheSizeBytesAsync(cancellationToken).ConfigureAwait(false)
                   + await GetVfsMetaSizeBytesAsync(cancellationToken).ConfigureAwait(false);

        // 2. Matar procesos de rclone para liberar los handles de archivo.
        var killed = await _processManager.KillOrphanRcloneProcessesAsync().ConfigureAwait(false);

        // 3. Dar un margen al sistema para liberar los locks.
        await Task.Delay(800, cancellationToken).ConfigureAwait(false);

        var errors = 0;

        foreach (var path in new[] { VfsCachePath, VfsMetaPath })
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (!Directory.Exists(path))
            {
                continue;
            }

            if (!TryDeleteDirectory(path))
            {
                errors++;
            }
        }

        // 4. Recrear las carpetas vacias para que rclone las encuentre listas.
        TryCreateDirectory(VfsCachePath);
        TryCreateDirectory(VfsMetaPath);

        var after = await GetVfsCacheSizeBytesAsync(cancellationToken).ConfigureAwait(false)
                  + await GetVfsMetaSizeBytesAsync(cancellationToken).ConfigureAwait(false);

        var freed = Math.Max(0, before - after);

        var message = errors == 0
            ? $"Purga completada. Liberados {FormatSize(freed)} ({killed} proceso(s) finalizado(s))."
            : $"Purga parcial: {errors} carpeta(s) no pudieron eliminarse por completo. " +
              $"Liberados {FormatSize(freed)}.";

        return new PurgeResult
        {
            Success = errors == 0,
            BytesFreed = freed,
            ProcessesKilled = killed,
            Message = message
        };
    }

    /// <inheritdoc />
    public string FormatSize(long bytes)
    {
        if (bytes <= 0)
        {
            return "0 MB";
        }

        const double kb = 1024d;
        const double mb = kb * 1024d;
        const double gb = mb * 1024d;

        if (bytes >= gb)
        {
            return $"{bytes / gb:0.00} GB";
        }

        if (bytes >= mb)
        {
            return $"{bytes / mb:0.00} MB";
        }

        return $"{bytes / kb:0.00} KB";
    }

    private static long CalculateDirectorySize(string path, CancellationToken cancellationToken)
    {
        if (!Directory.Exists(path))
        {
            return 0;
        }

        long total = 0;

        try
        {
            var options = new EnumerationOptions
            {
                RecurseSubdirectories = true,
                IgnoreInaccessible = true,
                AttributesToSkip = FileAttributes.ReparsePoint
            };

            foreach (var file in Directory.EnumerateFiles(path, "*", options))
            {
                cancellationToken.ThrowIfCancellationRequested();

                try
                {
                    total += new FileInfo(file).Length;
                }
                catch (IOException)
                {
                    // Archivo bloqueado o eliminado durante la enumeracion.
                }
            }
        }
        catch (UnauthorizedAccessException)
        {
            // Sin permisos sobre alguna subcarpeta.
        }
        catch (DirectoryNotFoundException)
        {
            return 0;
        }

        return total;
    }

    private static bool TryDeleteDirectory(string path)
    {
        try
        {
            Directory.Delete(path, recursive: true);
            return true;
        }
        catch (IOException)
        {
            // Algun archivo sigue bloqueado: intento archivo por archivo.
            return TryDeleteContents(path);
        }
        catch (UnauthorizedAccessException)
        {
            return false;
        }
    }

    private static bool TryDeleteContents(string path)
    {
        var allDeleted = true;

        try
        {
            foreach (var file in Directory.EnumerateFiles(path, "*", SearchOption.AllDirectories))
            {
                try
                {
                    File.SetAttributes(file, FileAttributes.Normal);
                    File.Delete(file);
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    allDeleted = false;
                }
            }

            foreach (var dir in Directory.EnumerateDirectories(path, "*", SearchOption.AllDirectories))
            {
                try
                {
                    Directory.Delete(dir, recursive: true);
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    allDeleted = false;
                }
            }
        }
        catch (DirectoryNotFoundException)
        {
            return true;
        }

        return allDeleted;
    }

    private static void TryCreateDirectory(string path)
    {
        try
        {
            Directory.CreateDirectory(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Best-effort.
        }
    }
}
