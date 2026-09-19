using System;

namespace RcloneCommanderAdvanced.Models;

/// <summary>
/// FEATURE Monitor de Trafico: una muestra puntual de telemetria de red.
///
/// Cada muestra representa el estado de las velocidades de subida y bajada
/// en un instante concreto (una por segundo). El Monitor de Trafico acumula
/// estas muestras en un buffer circular y las usa para dibujar la grafica
/// de lineas historica.
///
/// Es un tipo inmutable: una vez creada, la muestra no cambia, lo que evita
/// condiciones de carrera entre el hilo de sondeo y el hilo de UI.
/// </summary>
public sealed class TrafficSample
{
    /// <summary>Marca temporal UTC en la que se capturo la muestra.</summary>
    public DateTime TimestampUtc { get; }

    /// <summary>Velocidad de bajada instantanea en bytes/segundo.</summary>
    public double DownloadBytesPerSecond { get; }

    /// <summary>Velocidad de subida instantanea en bytes/segundo.</summary>
    public double UploadBytesPerSecond { get; }

    /// <summary>Numero de transferencias activas en el momento de la muestra.</summary>
    public int ActiveTransfers { get; }

    public TrafficSample(
        DateTime timestampUtc,
        double downloadBytesPerSecond,
        double uploadBytesPerSecond,
        int activeTransfers)
    {
        TimestampUtc = timestampUtc;
        DownloadBytesPerSecond = downloadBytesPerSecond < 0 ? 0 : downloadBytesPerSecond;
        UploadBytesPerSecond = uploadBytesPerSecond < 0 ? 0 : uploadBytesPerSecond;
        ActiveTransfers = activeTransfers < 0 ? 0 : activeTransfers;
    }

    /// <summary>Velocidad total (subida + bajada) en bytes/segundo.</summary>
    public double TotalBytesPerSecond => DownloadBytesPerSecond + UploadBytesPerSecond;
}
