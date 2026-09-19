using System;

namespace RcloneCommanderAdvanced.Services.Abstractions;

/// <summary>
/// FASE 2: contrato del icono de la bandeja del sistema (System Tray).
///
/// La aplicacion vive en segundo plano: al cerrar o minimizar la ventana no
/// se sale, sino que se oculta y queda accesible desde la bandeja.
/// </summary>
public interface ITrayIconService : IDisposable
{
    /// <summary>Muestra el icono en la bandeja (idempotente).</summary>
    void Show();

    /// <summary>Oculta y libera el icono de la bandeja.</summary>
    void Hide();

    /// <summary>Muestra una notificacion emergente (balloon) en la bandeja.</summary>
    void ShowNotification(string title, string message);

    /// <summary>Se dispara cuando el usuario pide abrir el panel principal.</summary>
    event EventHandler? OpenRequested;

    /// <summary>Se dispara cuando el usuario pide desmontar todas las unidades.</summary>
    event EventHandler? UnmountAllRequested;

    /// <summary>Se dispara cuando el usuario pide salir completamente de la app.</summary>
    event EventHandler? ExitRequested;
}
