namespace RcloneCommanderAdvanced.Services.Abstractions;

/// <summary>
/// Contrato del servicio que localiza el ejecutable rclone.exe y winfsp.
/// </summary>
public interface IRcloneLocator
{
    /// <summary>
    /// Directorio interno gestionado por la aplicacion donde se instalan las
    /// dependencias descargadas (p. ej. rclone.exe). Es
    /// <c>%APPDATA%\RcloneCommanderAdvanced\bin</c>.
    /// </summary>
    string InternalBinDirectory { get; }

    /// <summary>Ruta resuelta de rclone.exe, o null si no se encuentra.</summary>
    string? ResolveRclonePath();

    /// <summary>True si rclone.exe esta disponible.</summary>
    bool IsRcloneAvailable();

    /// <summary>True si WinFsp parece estar instalado en el sistema.</summary>
    bool IsWinFspInstalled();

    /// <summary>Devuelve la version de rclone (o cadena vacia si falla).</summary>
    string GetRcloneVersion();
}
