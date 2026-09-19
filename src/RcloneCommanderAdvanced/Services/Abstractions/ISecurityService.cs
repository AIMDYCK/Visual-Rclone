using System;
using System.Threading.Tasks;

namespace RcloneCommanderAdvanced.Services.Abstractions;

/// <summary>
/// Contrato del modulo de seguridad (PIN maestro / First-Run Setup).
/// </summary>
public interface ISecurityService
{
    /// <summary>True si ya existe un PIN maestro configurado.</summary>
    bool IsPinConfigured { get; }

    /// <summary>True si la sesion actual ya fue desbloqueada.</summary>
    bool IsUnlocked { get; }

    /// <summary>
    /// True si este equipo esta marcado como "de confianza" y aun no ha
    /// expirado el periodo elegido por el usuario. En ese caso la app puede
    /// arrancar sin pedir el PIN.
    /// </summary>
    bool IsDeviceTrusted { get; }

    /// <summary>Frase de recordatorio opcional definida al crear el PIN.</summary>
    string ReminderPhrase { get; }

    /// <summary>
    /// Crea el PIN maestro en el primer arranque.
    /// Devuelve false si ya existia un PIN previo.
    /// </summary>
    /// <param name="pin">PIN en claro (nunca se persiste tal cual).</param>
    /// <param name="reminderPhrase">Frase de recordatorio opcional (puede ser vacia).</param>
    Task<bool> CreateMasterPinAsync(string pin, string reminderPhrase = "");

    /// <summary>Valida el PIN introducido contra el hash almacenado.</summary>
    Task<bool> VerifyPinAsync(string pin);

    /// <summary>Bloquea la sesion actual (vuelve a exigir PIN).</summary>
    void Lock();

    /// <summary>
    /// Cambia el PIN maestro validando primero el PIN actual.
    /// </summary>
    Task<bool> ChangePinAsync(string currentPin, string newPin);

    /// <summary>
    /// Marca este equipo como de confianza para no volver a pedir el PIN
    /// durante el periodo indicado. Pasa <c>null</c> para confianza permanente.
    /// </summary>
    Task TrustDeviceAsync(TimeSpan? duration);

    /// <summary>Revoca la confianza de este equipo (volvera a pedir el PIN).</summary>
    Task RevokeTrustAsync();

    /// <summary>
    /// Marca la sesion como desbloqueada porque el equipo es de confianza,
    /// sin necesidad de introducir el PIN.
    /// </summary>
    void UnlockTrustedDevice();

    /// <summary>Actualiza la frase de recordatorio.</summary>
    Task SetReminderPhraseAsync(string reminderPhrase);
}
