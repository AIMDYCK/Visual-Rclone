using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using RcloneCommanderAdvanced.Models;

namespace RcloneCommanderAdvanced.Services.Abstractions;

/// <summary>
/// Contrato del gestor de subprocesos de rclone.
/// Cada montaje se ejecuta en su propia instancia de rclone.exe con un
/// puerto RC dinamico y aislado para evitar colisiones de red.
/// </summary>
public interface IMountProcessManager : IAsyncDisposable
{
    /// <summary>Se dispara cuando cambia el estado de un montaje.</summary>
    event EventHandler<MountStateChangedEventArgs>? StateChanged;

    /// <summary>Se dispara cuando un montaje emite una linea de log.</summary>
    event EventHandler<MountLogEventArgs>? LogReceived;

    /// <summary>Devuelve una instantanea de todos los montajes gestionados.</summary>
    IReadOnlyCollection<MountRuntimeInfo> GetSnapshot();

    /// <summary>Devuelve la informacion de un remoto concreto, o null.</summary>
    MountRuntimeInfo? GetInfo(string remoteName);

    /// <summary>
    /// Monta un remoto aplicando el perfil indicado. Devuelve true si el
    /// proceso arranco correctamente.
    /// </summary>
    Task<bool> MountAsync(RemoteEntry remote, CancellationToken cancellationToken = default);

    /// <summary>Desmonta un remoto concreto matando su arbol de procesos.</summary>
    Task<bool> UnmountAsync(string remoteName, CancellationToken cancellationToken = default);

    /// <summary>Monta todos los remotos habilitados en paralelo controlado.</summary>
    Task<int> MountAllAsync(IEnumerable<RemoteEntry> remotes, CancellationToken cancellationToken = default);

    /// <summary>Desmonta todos los montajes activos (Kill Tree global).</summary>
    Task<int> UnmountAllAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Mata cualquier proceso rclone.exe huerfano que haya quedado de
    /// ejecuciones anteriores. Se invoca al arrancar y al cerrar la app.
    /// </summary>
    Task<int> KillOrphanRcloneProcessesAsync();
}

/// <summary>Argumentos del evento de cambio de estado.</summary>
public sealed class MountStateChangedEventArgs : EventArgs
{
    public string RemoteName { get; init; } = string.Empty;
    public MountState State { get; init; }
    public string? Message { get; init; }
}

/// <summary>Argumentos del evento de log.</summary>
public sealed class MountLogEventArgs : EventArgs
{
    public string RemoteName { get; init; } = string.Empty;
    public string Line { get; init; } = string.Empty;
    public bool IsError { get; init; }
}

/// <summary>Informacion en runtime de un montaje.</summary>
public sealed class MountRuntimeInfo
{
    public string RemoteName { get; init; } = string.Empty;
    public string DriveLetter { get; init; } = string.Empty;
    public MountProfileType Profile { get; init; }
    public MountState State { get; set; } = MountState.Stopped;
    public int ProcessId { get; set; }
    public int RcPort { get; set; }
    public string LastError { get; set; } = string.Empty;
    public DateTime? StartedAtUtc { get; set; }
}
