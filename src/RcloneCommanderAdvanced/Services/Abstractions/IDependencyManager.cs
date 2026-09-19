using System;
using System.Threading;
using System.Threading.Tasks;

namespace RcloneCommanderAdvanced.Services.Abstractions;

/// <summary>
/// Contrato del gestor de dependencias externas (rclone.exe y WinFsp).
///
/// OBJETIVO:
/// Permitir que la aplicacion se autoabastezca en un entorno limpio
/// (Windows Sandbox, PC recien instalado, maquina virtual) sin obligar al
/// usuario a descargar e instalar rclone manualmente.
///
/// ESTRATEGIA:
///  - rclone.exe es un binario portable: se descarga el ZIP oficial de
///    Windows x64, se extrae en un directorio interno del perfil del usuario
///    (<c>%APPDATA%\RcloneCommanderAdvanced\bin</c>) y queda listo para usar.
///  - WinFsp es un driver de sistema que requiere un instalador MSI y
///    privilegios de administrador: NO se puede instalar silenciosamente de
///    forma fiable. Por eso el gestor solo ofrece el enlace oficial de
///    descarga y una comprobacion de reintento.
/// </summary>
public interface IDependencyManager
{
    /// <summary>URL oficial del ZIP portable de rclone para Windows x64.</summary>
    string RcloneDownloadUrl { get; }

    /// <summary>URL oficial de descarga de WinFsp.</summary>
    string WinFspDownloadUrl { get; }

    /// <summary>Directorio interno donde se instala rclone.exe.</summary>
    string InternalBinDirectory { get; }

    /// <summary>Ruta esperada de rclone.exe dentro del directorio interno.</summary>
    string InternalRclonePath { get; }

    /// <summary>
    /// True si rclone.exe esta disponible (en el directorio interno o en
    /// cualquier otra ubicacion conocida del sistema).
    /// </summary>
    bool IsRcloneInstalled();

    /// <summary>True si WinFsp esta instalado en el sistema.</summary>
    bool IsWinFspInstalled();

    /// <summary>
    /// True si TODAS las dependencias criticas estan presentes.
    /// </summary>
    bool AreDependenciesInstalled();

    /// <summary>
    /// Descarga el ZIP oficial de rclone, lo extrae en el directorio interno
    /// y deja <c>rclone.exe</c> listo para ser invocado.
    /// </summary>
    /// <param name="progress">
    /// Callback opcional con el porcentaje (0-100) y un mensaje de estado.
    /// </param>
    /// <param name="cancellationToken">Token de cancelacion.</param>
    /// <returns>Resultado de la operacion.</returns>
    Task<DependencyInstallResult> DownloadAndSetupRcloneAsync(
        Action<int, string>? progress = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Abre la pagina oficial de descarga de WinFsp en el navegador.
    /// </summary>
    void OpenWinFspDownloadPage();
}

/// <summary>
/// Resultado de una operacion de instalacion de dependencias.
/// </summary>
/// <param name="Success">true si la dependencia quedo operativa.</param>
/// <param name="Message">Mensaje descriptivo del resultado.</param>
/// <param name="InstalledPath">Ruta del binario instalado, si aplica.</param>
public sealed record DependencyInstallResult(
    bool Success,
    string Message,
    string? InstalledPath = null)
{
    /// <summary>Crea un resultado exitoso.</summary>
    public static DependencyInstallResult Ok(string message, string? path = null) =>
        new(true, message, path);

    /// <summary>Crea un resultado fallido.</summary>
    public static DependencyInstallResult Fail(string message) =>
        new(false, message);
}
