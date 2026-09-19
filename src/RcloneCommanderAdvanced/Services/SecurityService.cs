using System;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using RcloneCommanderAdvanced.Services.Abstractions;

namespace RcloneCommanderAdvanced.Services;

/// <summary>
/// Implementacion del PIN maestro.
///
/// El PIN NUNCA se almacena en claro: se deriva con PBKDF2 (SHA-256,
/// 210.000 iteraciones, salt aleatorio de 32 bytes) y se guarda el hash
/// en Base64 junto al salt dentro de appsettings.json.
///
/// Ademas gestiona la "confianza de dispositivo": si el usuario lo autoriza,
/// este equipo puede arrancar sin pedir el PIN durante 7 dias, 30 dias o
/// de forma permanente. La confianza se ata a una huella del equipo para
/// invalidarla automaticamente si cambia el hardware o el usuario.
/// </summary>
public sealed class SecurityService : ISecurityService
{
    private const int SaltSize = 32;
    private const int HashSize = 32;
    private const int Iterations = 210_000;

    private readonly ISettingsService _settingsService;

    public SecurityService(ISettingsService settingsService)
    {
        _settingsService = settingsService;
    }

    /// <inheritdoc />
    public bool IsPinConfigured =>
        !string.IsNullOrWhiteSpace(_settingsService.Current.MasterPinHash) &&
        !string.IsNullOrWhiteSpace(_settingsService.Current.MasterPinSalt);

    /// <inheritdoc />
    public bool IsUnlocked { get; private set; }

    /// <inheritdoc />
    public string ReminderPhrase => _settingsService.Current.PinReminderPhrase ?? string.Empty;

    /// <inheritdoc />
    public bool IsDeviceTrusted
    {
        get
        {
            var settings = _settingsService.Current;

            // La confianza solo tiene sentido si hay un PIN configurado.
            if (!IsPinConfigured)
            {
                return false;
            }

            // Si la huella del equipo no coincide, la confianza quedo invalidada.
            if (!string.IsNullOrEmpty(settings.TrustedDeviceFingerprint) &&
                !string.Equals(settings.TrustedDeviceFingerprint, GetDeviceFingerprint(), StringComparison.Ordinal))
            {
                return false;
            }

            if (settings.TrustedDeviceForever)
            {
                return true;
            }

            return settings.TrustedDeviceUntil.HasValue &&
                   settings.TrustedDeviceUntil.Value > DateTime.UtcNow;
        }
    }

    /// <inheritdoc />
    public async Task<bool> CreateMasterPinAsync(string pin, string reminderPhrase = "")
    {
        if (string.IsNullOrWhiteSpace(pin))
        {
            return false;
        }

        if (IsPinConfigured)
        {
            return false;
        }

        var salt = RandomNumberGenerator.GetBytes(SaltSize);
        var hash = DeriveHash(pin, salt);

        var settings = _settingsService.Current;
        settings.MasterPinSalt = Convert.ToBase64String(salt);
        settings.MasterPinHash = Convert.ToBase64String(hash);
        settings.PinReminderPhrase = reminderPhrase?.Trim() ?? string.Empty;
        settings.IsFirstRunCompleted = true;

        await _settingsService.SaveAsync(settings).ConfigureAwait(false);

        IsUnlocked = true;
        return true;
    }

    /// <inheritdoc />
    public async Task<bool> VerifyPinAsync(string pin)
    {
        if (string.IsNullOrWhiteSpace(pin) || !IsPinConfigured)
        {
            return false;
        }

        var settings = _settingsService.Current;

        byte[] salt;
        byte[] expected;

        try
        {
            salt = Convert.FromBase64String(settings.MasterPinSalt);
            expected = Convert.FromBase64String(settings.MasterPinHash);
        }
        catch (FormatException)
        {
            return false;
        }

        var actual = DeriveHash(pin, salt);

        // Comparacion en tiempo constante para evitar timing attacks.
        var isValid = CryptographicOperations.FixedTimeEquals(actual, expected);

        if (isValid)
        {
            IsUnlocked = true;
        }

        await Task.CompletedTask.ConfigureAwait(false);
        return isValid;
    }

    /// <inheritdoc />
    public void Lock() => IsUnlocked = false;

    /// <inheritdoc />
    public async Task<bool> ChangePinAsync(string currentPin, string newPin)
    {
        if (string.IsNullOrWhiteSpace(newPin))
        {
            return false;
        }

        var verified = await VerifyPinAsync(currentPin).ConfigureAwait(false);
        if (!verified)
        {
            return false;
        }

        var salt = RandomNumberGenerator.GetBytes(SaltSize);
        var hash = DeriveHash(newPin, salt);

        var settings = _settingsService.Current;
        settings.MasterPinSalt = Convert.ToBase64String(salt);
        settings.MasterPinHash = Convert.ToBase64String(hash);

        await _settingsService.SaveAsync(settings).ConfigureAwait(false);
        return true;
    }

    /// <inheritdoc />
    public async Task TrustDeviceAsync(TimeSpan? duration)
    {
        var settings = _settingsService.Current;

        settings.TrustedDeviceFingerprint = GetDeviceFingerprint();

        if (duration.HasValue)
        {
            settings.TrustedDeviceForever = false;
            settings.TrustedDeviceUntil = DateTime.UtcNow.Add(duration.Value);
        }
        else
        {
            // Confianza permanente.
            settings.TrustedDeviceForever = true;
            settings.TrustedDeviceUntil = null;
        }

        await _settingsService.SaveAsync(settings).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task RevokeTrustAsync()
    {
        var settings = _settingsService.Current;

        settings.TrustedDeviceForever = false;
        settings.TrustedDeviceUntil = null;
        settings.TrustedDeviceFingerprint = string.Empty;

        await _settingsService.SaveAsync(settings).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public void UnlockTrustedDevice()
    {
        if (IsDeviceTrusted)
        {
            IsUnlocked = true;
        }
    }

    /// <inheritdoc />
    public async Task SetReminderPhraseAsync(string reminderPhrase)
    {
        var settings = _settingsService.Current;
        settings.PinReminderPhrase = reminderPhrase?.Trim() ?? string.Empty;

        await _settingsService.SaveAsync(settings).ConfigureAwait(false);
    }

    private static byte[] DeriveHash(string pin, byte[] salt) =>
        Rfc2898DeriveBytes.Pbkdf2(
            password: pin,
            salt: salt,
            iterations: Iterations,
            hashAlgorithm: HashAlgorithmName.SHA256,
            outputLength: HashSize);

    /// <summary>
    /// Huella estable del equipo: combina nombre de maquina y usuario. Si el
    /// usuario copia el appsettings.json a otro equipo, la confianza se
    /// invalida automaticamente.
    /// </summary>
    private static string GetDeviceFingerprint()
    {
        var raw = $"{Environment.MachineName}|{Environment.UserName}";
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(raw));
        return Convert.ToBase64String(bytes);
    }
}
