using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using RcloneCommanderAdvanced.Services.Abstractions;

namespace RcloneCommanderAdvanced.Services;

/// <summary>
/// Implementacion de <see cref="IRcloneRcClient"/> basada en
/// <see cref="HttpClient"/>.
///
/// Detalles de diseno:
///  - Un unico <see cref="HttpClient"/> estatico y reutilizado: crear uno por
///    peticion agota los sockets del sistema (TIME_WAIT) cuando se sondea
///    cada segundo por cada unidad montada.
///  - Timeout corto (2 s): si el proceso RC no responde, preferimos fallar
///    rapido y reintentar en el siguiente tick antes que bloquear la UI.
///  - <c>--rc-no-auth</c> esta activo en los montajes, por lo que no se
///    envian credenciales. Si en el futuro se activa auth, aqui se anadiria
///    el header Basic.
/// </summary>
public sealed class RcloneRcClient : IRcloneRcClient
{
    private static readonly HttpClient Http = CreateHttpClient();

    private static HttpClient CreateHttpClient()
    {
        var handler = new SocketsHttpHandler
        {
            // Reutilizamos conexiones y evitamos el overhead de DNS/TCP.
            PooledConnectionLifetime = TimeSpan.FromMinutes(2),
            ConnectTimeout = TimeSpan.FromSeconds(2)
        };

        return new HttpClient(handler)
        {
            Timeout = TimeSpan.FromSeconds(2)
        };
    }

    /// <inheritdoc />
    public async Task<RcStatsResult> GetStatsAsync(
        int port,
        CancellationToken cancellationToken = default)
    {
        if (port <= 0)
        {
            return RcStatsResult.Fail($"RcPort invalido ({port}).");
        }

        // DIAGNOSTICO Monitor de Trafico: usamos 127.0.0.1 en lugar de
        // "localhost". En .NET, "localhost" puede resolverse primero a ::1
        // (IPv6); si el servidor RC de rclone solo escucha en IPv4, la
        // conexion falla de forma silenciosa. La IP literal elimina esa
        // ambiguedad.
        var url = $"http://127.0.0.1:{port}/core/stats";

        try
        {
            // La API RC de rclone exige POST en TODOS sus endpoints, incluido
            // /core/stats. Un GET devuelve "404 Not Found". Enviamos un cuerpo
            // JSON vacio ("{}") porque rclone lo requiere aunque no haya
            // parametros.
            using var content = new StringContent(
                "{}",
                System.Text.Encoding.UTF8,
                "application/json");

            using var response = await Http
                .PostAsync(url, content, cancellationToken)
                .ConfigureAwait(false);

            if (!response.IsSuccessStatusCode)
            {
                // DIAGNOSTICO: exponemos el codigo HTTP exacto.
                return RcStatsResult.Fail(
                    $"HTTP ERROR: {(int)response.StatusCode} - {response.ReasonPhrase}");
            }

            var json = await response.Content
                .ReadAsStringAsync(cancellationToken)
                .ConfigureAwait(false);

            if (string.IsNullOrWhiteSpace(json))
            {
                return RcStatsResult.Fail("Respuesta vacia de /core/stats.");
            }

            CoreStatsDto? dto;
            try
            {
                dto = JsonSerializer.Deserialize<CoreStatsDto>(json, JsonOptions);
            }
            catch (JsonException jsonEx)
            {
                // DIAGNOSTICO: JSON malformado o inesperado.
                return RcStatsResult.Fail(
                    $"JSON Parse Error: {jsonEx.Message} | body={Truncate(json, 200)}");
            }

            if (dto is null)
            {
                return RcStatsResult.Fail(
                    $"JSON Parse Error: deserializacion nula | body={Truncate(json, 200)}");
            }

            // BUGFIX Monitor de Trafico (VFS): en montajes con cache VFS
            // (--vfs-cache-mode full/writes) el vaciado de la cache es
            // asincrono y rclone NO lo contabiliza en la velocidad raiz
            // ("speed"/"uploadSpeed"/"downloadSpeed" suelen venir en 0). La
            // velocidad real esta en cada objeto del array "transferring".
            // Sumamos las velocidades individuales y usamos ese valor cuando
            // la raiz viene a cero, para que la grafica refleje el trafico
            // real durante una copia a la unidad montada.
            var transferSpeedSum = 0.0;
            var transferCount = 0;

            if (dto.Transferring is { Count: > 0 })
            {
                transferCount = dto.Transferring.Count;

                foreach (var transfer in dto.Transferring)
                {
                    // Preferimos "speed" (instantanea); si viene a cero,
                    // caemos a "speedAvg" (media) como respaldo.
                    var perTransfer = transfer.Speed > 0
                        ? transfer.Speed
                        : transfer.SpeedAvg;

                    if (perTransfer > 0)
                    {
                        transferSpeedSum += perTransfer;
                    }
                }
            }

            var rootSpeed = dto.Speed;
            var rootUpload = dto.UploadSpeed;
            var rootDownload = dto.DownloadSpeed;

            // Si la raiz no reporta nada pero hay transferencias activas,
            // usamos la suma de las velocidades individuales.
            if (rootSpeed <= 0 && transferSpeedSum > 0)
            {
                rootSpeed = transferSpeedSum;
            }

            if (rootUpload <= 0 && rootDownload <= 0 && transferSpeedSum > 0)
            {
                // No podemos distinguir subida/bajada por transferencia de
                // forma fiable; asignamos la suma a la direccion dominante
                // segun los bytes acumulados para no perder el dato.
                if (dto.BytesUploaded >= dto.BytesDownloaded)
                {
                    rootUpload = transferSpeedSum;
                }
                else
                {
                    rootDownload = transferSpeedSum;
                }
            }

            return RcStatsResult.Ok(new RcStats
            {
                TotalBytes = dto.Bytes,
                BytesUploaded = dto.BytesUploaded,
                BytesDownloaded = dto.BytesDownloaded,
                SpeedBytesPerSecond = rootSpeed,
                UploadSpeedBytesPerSecond = rootUpload,
                DownloadSpeedBytesPerSecond = rootDownload,
                ActiveTransfers = transferCount,
                Errors = dto.Errors
            });
        }
        catch (Exception ex)
        {
            // DIAGNOSTICO: exponemos el tipo y mensaje EXACTOS de la excepcion
            // (HttpRequestException, TaskCanceledException por timeout, etc.).
            return RcStatsResult.Fail($"{ex.GetType().Name}: {ex.Message}");
        }
    }

    /// <summary>Recorta un texto largo para incrustarlo en un mensaje de error.</summary>
    private static string Truncate(string value, int maxLength) =>
        value.Length <= maxLength ? value : value[..maxLength] + "...";

    /// <inheritdoc />
    public async Task<bool> SetBandwidthLimitAsync(
        int port,
        string rate,
        CancellationToken cancellationToken = default)
    {
        if (port <= 0 || string.IsNullOrWhiteSpace(rate))
        {
            return false;
        }

        try
        {
            var url = $"http://localhost:{port}/core/bwlimit";

            // rclone espera el parametro "rate" (ej. "10M", "off").
            var payload = new BwLimitRequest { Rate = rate };

            using var response = await Http
                .PostAsJsonAsync(url, payload, JsonOptions, cancellationToken)
                .ConfigureAwait(false);

            return response.IsSuccessStatusCode;
        }
        catch (Exception ex) when (
            ex is HttpRequestException or TaskCanceledException or
            OperationCanceledException or JsonException or UriFormatException)
        {
            return false;
        }
    }

    /// <inheritdoc />
    public async Task<bool> UnmountAsync(
        int port,
        string? mountPoint,
        CancellationToken cancellationToken = default)
    {
        if (port <= 0)
        {
            return false;
        }

        try
        {
            var url = $"http://localhost:{port}/mount/unmount";

            // rclone acepta "mountPoint" (ej. "X:") o "dir". Si no se indica,
            // desmonta el unico montaje de esa instancia RC.
            var payload = new UnmountRequest { MountPoint = mountPoint ?? string.Empty };

            using var response = await Http
                .PostAsJsonAsync(url, payload, JsonOptions, cancellationToken)
                .ConfigureAwait(false);

            return response.IsSuccessStatusCode;
        }
        catch (Exception ex) when (
            ex is HttpRequestException or TaskCanceledException or
            OperationCanceledException or JsonException or UriFormatException)
        {
            return false;
        }
    }

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        NumberHandling = JsonNumberHandling.AllowReadingFromString
    };

    /// <summary>
    /// DTO que refleja el JSON de <c>/core/stats</c>. rclone devuelve los
    /// bytes como numero (a veces como string), por eso activamos
    /// <see cref="JsonNumberHandling.AllowReadingFromString"/>.
    /// </summary>
    private sealed class CoreStatsDto
    {
        [JsonPropertyName("bytes")]
        public long Bytes { get; set; }

        [JsonPropertyName("bytesUploaded")]
        public long BytesUploaded { get; set; }

        [JsonPropertyName("bytesDownloaded")]
        public long BytesDownloaded { get; set; }

        [JsonPropertyName("speed")]
        public double Speed { get; set; }

        [JsonPropertyName("uploadSpeed")]
        public double UploadSpeed { get; set; }

        [JsonPropertyName("downloadSpeed")]
        public double DownloadSpeed { get; set; }

        [JsonPropertyName("errors")]
        public int Errors { get; set; }

        [JsonPropertyName("transferring")]
        public List<TransferringDto>? Transferring { get; set; }
    }

    /// <summary>
    /// DTO de cada objeto dentro del array <c>transferring</c> de
    /// <c>/core/stats</c>. En montajes VFS, la velocidad raiz (<c>speed</c>)
    /// suele venir en 0 porque el vaciado de la cache no se contabiliza como
    /// una transferencia sincrona; la velocidad real esta en cada elemento.
    /// </summary>
    private sealed class TransferringDto
    {
        [JsonPropertyName("speed")]
        public double Speed { get; set; }

        [JsonPropertyName("speedAvg")]
        public double SpeedAvg { get; set; }

        [JsonPropertyName("bytes")]
        public long Bytes { get; set; }

        [JsonPropertyName("name")]
        public string? Name { get; set; }
    }

    /// <summary>Cuerpo de la peticion a <c>/core/bwlimit</c>.</summary>
    private sealed class BwLimitRequest
    {
        [JsonPropertyName("rate")]
        public string Rate { get; set; } = string.Empty;
    }

    /// <summary>Cuerpo de la peticion a <c>/mount/unmount</c>.</summary>
    private sealed class UnmountRequest
    {
        [JsonPropertyName("mountPoint")]
        public string MountPoint { get; set; } = string.Empty;
    }
}
