using System.Threading;
using System.Threading.Tasks;

namespace RcloneCommanderAdvanced.Services.Abstractions;

/// <summary>
/// Cliente HTTP para la API RC (Remote Control) de rclone.
///
/// Cada montaje se lanza con <c>--rc --rc-addr localhost:{puerto} --rc-no-auth</c>,
/// de modo que expone un pequeno servidor HTTP local con endpoints de control
/// en caliente. Este cliente encapsula las llamadas que necesita el panel:
///
///  - <c>GET  /core/stats</c>     -> telemetria (bytes transferidos, velocidad).
///  - <c>POST /core/bwlimit</c>   -> limitador de ancho de banda en caliente.
///  - <c>POST /mount/unmount</c>  -> desmontaje limpio (sin matar el proceso).
///
/// El cliente es seguro para uso concurrente y reutiliza un unico
/// <see cref="System.Net.Http.HttpClient"/> (evita agotar sockets).
/// </summary>
public interface IRcloneRcClient
{
    /// <summary>
    /// Consulta <c>/core/stats</c> en el puerto RC indicado y devuelve un
    /// resultado que contiene la telemetria normalizada o el error EXACTO
    /// que impidio obtenerla (codigo HTTP, excepcion o fallo de parseo JSON).
    ///
    /// DIAGNOSTICO Monitor de Trafico: antes devolvia <c>null</c> para todos
    /// los fallos, ocultando la causa real. Ahora el error viaja en el
    /// resultado para poder mostrarlo en la UI.
    /// </summary>
    /// <param name="port">Puerto RC del montaje.</param>
    /// <param name="cancellationToken">Token de cancelacion.</param>
    Task<RcStatsResult> GetStatsAsync(int port, CancellationToken cancellationToken = default);

    /// <summary>
    /// Aplica un limite de ancho de banda en caliente mediante
    /// <c>POST /core/bwlimit</c> con el parametro <c>rate</c>.
    /// </summary>
    /// <param name="port">Puerto RC del montaje.</param>
    /// <param name="rate">
    /// Limite en formato rclone (ej. <c>"10M"</c>, <c>"1M"</c>, <c>"off"</c>).
    /// </param>
    /// <param name="cancellationToken">Token de cancelacion.</param>
    /// <returns><c>true</c> si el endpoint acepto el cambio.</returns>
    Task<bool> SetBandwidthLimitAsync(
        int port,
        string rate,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Solicita un desmontaje LIMPIO mediante <c>POST /mount/unmount</c>.
    ///
    /// A diferencia de matar el proceso (<c>Process.Kill</c>), este endpoint
    /// permite que rclone vacie la cache VFS en disco y cierre el punto de
    /// montaje WinFsp de forma ordenada, evitando corrupcion de datos y
    /// unidades "fantasma" que quedan colgadas en el Explorador.
    /// </summary>
    /// <param name="port">Puerto RC del montaje.</param>
    /// <param name="mountPoint">
    /// Punto de montaje a desmontar (ej. <c>"X:"</c>). Si es <c>null</c> o
    /// vacio, rclone desmonta el unico montaje activo de esa instancia.
    /// </param>
    /// <param name="cancellationToken">Token de cancelacion.</param>
    /// <returns>
    /// <c>true</c> si el endpoint acepto la peticion de desmontaje.
    /// </returns>
    Task<bool> UnmountAsync(
        int port,
        string? mountPoint,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// DIAGNOSTICO Monitor de Trafico: resultado de una consulta a
/// <c>/core/stats</c>. Lleva la telemetria cuando la peticion tuvo exito o,
/// en caso contrario, el mensaje de error EXACTO (codigo HTTP, tipo y
/// mensaje de la excepcion, o fallo de parseo JSON) para exponerlo en la UI.
/// </summary>
public sealed class RcStatsResult
{
    /// <summary>Telemetria obtenida, o <c>null</c> si la consulta fallo.</summary>
    public RcStats? Stats { get; init; }

    /// <summary>
    /// Mensaje de error exacto cuando <see cref="Stats"/> es <c>null</c>.
    /// Vacio cuando la consulta tuvo exito.
    /// </summary>
    public string Error { get; init; } = string.Empty;

    /// <summary>Indica si la consulta se completo con exito.</summary>
    public bool IsSuccess => Stats is not null;

    /// <summary>Crea un resultado exitoso.</summary>
    public static RcStatsResult Ok(RcStats stats) => new() { Stats = stats };

    /// <summary>Crea un resultado fallido con el error exacto.</summary>
    public static RcStatsResult Fail(string error) => new() { Error = error };
}

/// <summary>
/// Telemetria normalizada extraida de <c>/core/stats</c>.
/// </summary>
public sealed class RcStats
{
    /// <summary>Bytes totales transferidos (subida + bajada) desde el arranque.</summary>
    public long TotalBytes { get; init; }

    /// <summary>Bytes subidos (upload) desde el arranque.</summary>
    public long BytesUploaded { get; init; }

    /// <summary>Bytes bajados (download) desde el arranque.</summary>
    public long BytesDownloaded { get; init; }

    /// <summary>Velocidad instantanea total en bytes/segundo.</summary>
    public double SpeedBytesPerSecond { get; init; }

    /// <summary>Velocidad de subida instantanea en bytes/segundo.</summary>
    public double UploadSpeedBytesPerSecond { get; init; }

    /// <summary>Velocidad de bajada instantanea en bytes/segundo.</summary>
    public double DownloadSpeedBytesPerSecond { get; init; }

    /// <summary>Numero de transferencias activas en este momento.</summary>
    public int ActiveTransfers { get; init; }

    /// <summary>Numero de errores acumulados.</summary>
    public int Errors { get; init; }
}
