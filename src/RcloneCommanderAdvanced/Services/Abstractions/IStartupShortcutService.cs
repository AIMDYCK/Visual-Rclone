namespace RcloneCommanderAdvanced.Services.Abstractions;

/// <summary>
/// FASE 2: gestiona el arranque automatico con Windows mediante un acceso
/// directo (.lnk) en la carpeta Startup del usuario.
///
/// IMPORTANTE (seguridad): este servicio SOLO escribe en
/// %APPDATA%\Microsoft\Windows\Start Menu\Programs\Startup. No toca el
/// Registro ni variables de entorno globales.
/// </summary>
public interface IStartupShortcutService
{
    /// <summary>Indica si el acceso directo de arranque existe actualmente.</summary>
    bool IsEnabled { get; }

    /// <summary>
    /// Crea (o reemplaza) el acceso directo de arranque apuntando al ejecutable
    /// actual con el argumento <c>--minimized</c>.
    /// </summary>
    /// <returns>true si la operacion se completo correctamente.</returns>
    bool Enable();

    /// <summary>Elimina el acceso directo de arranque si existe.</summary>
    /// <returns>true si la operacion se completo correctamente.</returns>
    bool Disable();
}
