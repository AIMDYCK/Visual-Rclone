namespace RcloneCommanderAdvanced.Models;

/// <summary>
/// Estado en tiempo real de un montaje gestionado por el ProcessManager.
/// </summary>
public enum MountState
{
    /// <summary>El remoto esta configurado pero no montado.</summary>
    Stopped = 0,

    /// <summary>Se lanzo el proceso y se espera a que la unidad aparezca.</summary>
    Starting = 1,

    /// <summary>El proceso esta vivo y la unidad responde.</summary>
    Mounted = 2,

    /// <summary>El proceso termino con error o la unidad desaparecio.</summary>
    Error = 3,

    /// <summary>Se solicito el desmontaje y se esta esperando la salida del proceso.</summary>
    Stopping = 4
}
