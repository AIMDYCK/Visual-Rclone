using System;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using RcloneCommanderAdvanced.Services.Abstractions;

namespace RcloneCommanderAdvanced.Services;

/// <summary>
/// Implementacion del gestor de dependencias externas.
///
/// FLUJO DE INSTALACION DE RCLONE (1 clic):
///  1. Se descarga el ZIP oficial <c>rclone-current-windows-amd64.zip</c> a un
///     archivo temporal, reportando el progreso real (bytes / total).
///  2. Se extrae el contenido en el directorio interno
///     <c>%APPDATA%\RcloneCommanderAdvanced\bin</c>. El ZIP de rclone contiene
///     una carpeta raiz versionada (p. ej. <c>rclone-v1.68.0-windows-amd64\</c>),
///     por lo que se localiza recursivamente <c>rclone.exe</c> y se copia a la
///     raiz del directorio interno para tener una ruta estable.
///  3. Se elimina el ZIP temporal.
///
/// SEGURIDAD: no se modifica el Registro ni variables de entorno globales. Todo
/// vive en el perfil del usuario. La descarga es HTTPS desde el dominio oficial
/// de rclone.
/// </summary>
public sealed class DependencyManager : IDependencyManager
{
    private const string RcloneZipUrl =
        "https://downloads.rclone.org/rclone-current-windows-amd64.zip";

    private const string WinFspUrl = "https://winfsp.dev/rel/";

    private readonly IRcloneLocator _locator;

    public DependencyManager(IRcloneLocator locator)
    {
        _locator = locator;
    }

    /// <inheritdoc />
    public string RcloneDownloadUrl => RcloneZipUrl;

    /// <inheritdoc />
    public string WinFspDownloadUrl => WinFspUrl;

    /// <inheritdoc />
    public string InternalBinDirectory => _locator.InternalBinDirectory;

    /// <inheritdoc />
    public string InternalRclonePath =>
        Path.Combine(InternalBinDirectory, "rclone.exe");

    /// <inheritdoc />
    public bool IsRcloneInstalled() => _locator.IsRcloneAvailable();

    /// <inheritdoc />
    public bool IsWinFspInstalled() => _locator.IsWinFspInstalled();

    /// <inheritdoc />
    public bool AreDependenciesInstalled() => IsRcloneInstalled() && IsWinFspInstalled();

    /// <inheritdoc />
    public async Task<DependencyInstallResult> DownloadAndSetupRcloneAsync(
        Action<int, string>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var tempZip = Path.Combine(
            Path.GetTempPath(),
            $"rclone-{Guid.NewGuid():N}.zip");

        try
        {
            // 1. Garantizar el directorio interno.
            Directory.CreateDirectory(InternalBinDirectory);

            // 2. Descargar el ZIP con progreso real.
            progress?.Invoke(0, "Connecting...");

            using var http = new HttpClient
            {
                Timeout = TimeSpan.FromMinutes(10)
            };

            using var response = await http.GetAsync(
                RcloneZipUrl,
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken).ConfigureAwait(false);

            response.EnsureSuccessStatusCode();

            var totalBytes = response.Content.Headers.ContentLength ?? -1L;

            await using (var source = await response.Content
                .ReadAsStreamAsync(cancellationToken).ConfigureAwait(false))
            await using (var destination = new FileStream(
                tempZip, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                var buffer = new byte[81920];
                long downloaded = 0;
                int read;

                while ((read = await source.ReadAsync(
                    buffer, cancellationToken).ConfigureAwait(false)) > 0)
                {
                    await destination.WriteAsync(
                        buffer.AsMemory(0, read), cancellationToken).ConfigureAwait(false);

                    downloaded += read;

                    if (totalBytes > 0)
                    {
                        // Reservamos el 0-90% para la descarga y el 90-100%
                        // para la extraccion.
                        var percent = (int)(downloaded * 90 / totalBytes);
                        progress?.Invoke(percent, "Downloading rclone...");
                    }
                }
            }

            cancellationToken.ThrowIfCancellationRequested();

            // 3. Extraer el ZIP en un directorio temporal de staging para
            //    localizar rclone.exe sin ensuciar el directorio interno.
            progress?.Invoke(90, "Extracting...");

            var staging = Path.Combine(
                Path.GetTempPath(),
                $"rclone-extract-{Guid.NewGuid():N}");

            try
            {
                ZipFile.ExtractToDirectory(tempZip, staging);

                var extractedExe = FindFileRecursive(staging, "rclone.exe");
                if (extractedExe is null)
                {
                    return DependencyInstallResult.Fail(
                        "The downloaded archive did not contain rclone.exe.");
                }

                // 4. Copiar rclone.exe a la raiz del directorio interno.
                File.Copy(extractedExe, InternalRclonePath, overwrite: true);

                progress?.Invoke(100, "Done.");

                return DependencyInstallResult.Ok(
                    "rclone installed successfully.",
                    InternalRclonePath);
            }
            finally
            {
                TryDeleteDirectory(staging);
            }
        }
        catch (OperationCanceledException)
        {
            return DependencyInstallResult.Fail("The download was cancelled.");
        }
        catch (HttpRequestException ex)
        {
            return DependencyInstallResult.Fail(
                $"Network error while downloading rclone: {ex.Message}");
        }
        catch (Exception ex) when (ex is IOException
                                       or UnauthorizedAccessException
                                       or InvalidDataException
                                       or NotSupportedException)
        {
            return DependencyInstallResult.Fail(
                $"Could not install rclone: {ex.Message}");
        }
        finally
        {
            // Limpieza best-effort del ZIP temporal.
            try
            {
                if (File.Exists(tempZip))
                {
                    File.Delete(tempZip);
                }
            }
            catch (IOException)
            {
                // Ignorado: el archivo temporal se limpiara por el SO.
            }
        }
    }

    /// <inheritdoc />
    public void OpenWinFspDownloadPage()
    {
        try
        {
            Process.Start(new ProcessStartInfo(WinFspUrl) { UseShellExecute = true });
        }
        catch (Exception)
        {
            // Si no se puede abrir el navegador, no hay nada mas que hacer.
        }
    }

    /// <summary>
    /// Busca un archivo por nombre de forma recursiva dentro de un directorio.
    /// </summary>
    private static string? FindFileRecursive(string directory, string fileName)
    {
        try
        {
            foreach (var file in Directory.EnumerateFiles(
                directory, fileName, SearchOption.AllDirectories))
            {
                return file;
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Directorio inaccesible: se ignora.
        }

        return null;
    }

    /// <summary>
    /// Elimina un directorio de forma recursiva, ignorando errores.
    /// </summary>
    private static void TryDeleteDirectory(string directory)
    {
        try
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Limpieza best-effort.
        }
    }
}
