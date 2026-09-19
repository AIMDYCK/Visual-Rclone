using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using RcloneCommanderAdvanced.Services.Abstractions;

namespace RcloneCommanderAdvanced.ViewModels;

/// <summary>
/// Opcion de duracion para "confiar en este dispositivo".
/// </summary>
public sealed class TrustDurationOption
{
    public string Label { get; init; } = string.Empty;

    /// <summary>Duracion; null = confianza permanente.</summary>
    public TimeSpan? Duration { get; init; }
}

/// <summary>
/// ViewModel de la pantalla de bloqueo. La app arranca bloqueada y exige
/// el PIN maestro antes de desplegar el dashboard.
/// </summary>
public sealed partial class LockViewModel : ObservableObject
{
    private readonly ISecurityService _securityService;
    private readonly ILocalizationService _localization;

    private int _failedAttempts;

    public LockViewModel(ISecurityService securityService, ILocalizationService localization)
    {
        _securityService = securityService;
        _localization = localization;

        TrustDurations = new List<TrustDurationOption>
        {
            new() { Label = L("Str_Trust1Day", "1 day"), Duration = TimeSpan.FromDays(1) },
            new() { Label = L("Str_Trust7Days", "7 days"), Duration = TimeSpan.FromDays(7) },
            new() { Label = L("Str_Trust30Days", "30 days"), Duration = TimeSpan.FromDays(30) },
            new() { Label = L("Str_TrustForever", "Forever"), Duration = null },
        };

        _selectedTrustDuration = TrustDurations[0];
        _reminderPhrase = securityService.ReminderPhrase;
    }

    /// <summary>
    /// Atajo de traduccion: resuelve una clave en el idioma activo o devuelve
    /// el fallback indicado si no existe la clave.
    /// </summary>
    private string L(string key, string fallback) =>
        _localization.Get(key, fallback);

    [ObservableProperty]
    private string _pin = string.Empty;

    [ObservableProperty]
    private string _errorMessage = string.Empty;

    [ObservableProperty]
    private bool _isBusy;

    /// <summary>Si el usuario marca esta casilla, no se volvera a pedir el PIN.</summary>
    [ObservableProperty]
    private bool _trustThisDevice;

    /// <summary>
    /// Alias de solo lectura de <see cref="TrustThisDevice"/> pensado para
    /// enlazar la <c>Visibility</c> del selector de duracion en el XAML.
    /// Se notifica desde <see cref="OnTrustThisDeviceChanged"/>.
    /// </summary>
    public bool IsTrustDeviceChecked => TrustThisDevice;

    /// <summary>Duracion elegida para la confianza del dispositivo.</summary>
    [ObservableProperty]
    private TrustDurationOption _selectedTrustDuration;

    /// <summary>Frase de recordatorio configurada al crear el PIN (puede estar vacia).</summary>
    [ObservableProperty]
    private string _reminderPhrase = string.Empty;

    /// <summary>Opciones disponibles de duracion de confianza.</summary>
    public IReadOnlyList<TrustDurationOption> TrustDurations { get; }

    /// <summary>True si hay una frase de recordatorio que mostrar.</summary>
    public bool HasReminderPhrase => !string.IsNullOrWhiteSpace(ReminderPhrase);

    /// <summary>Se dispara cuando el PIN es correcto.</summary>
    public event EventHandler? Unlocked;

    /// <summary>Numero de intentos fallidos acumulados.</summary>
    public int FailedAttempts => _failedAttempts;

    partial void OnReminderPhraseChanged(string value) => OnPropertyChanged(nameof(HasReminderPhrase));

    /// <summary>
    /// Al cambiar "Confiar en este dispositivo" notificamos el alias
    /// <see cref="IsTrustDeviceChecked"/> para que el selector de duracion
    /// actualice su visibilidad de inmediato.
    /// </summary>
    partial void OnTrustThisDeviceChanged(bool value) => OnPropertyChanged(nameof(IsTrustDeviceChecked));

    [RelayCommand]
    private async Task UnlockAsync()
    {
        ErrorMessage = string.Empty;

        if (string.IsNullOrWhiteSpace(Pin))
        {
            ErrorMessage = L("Str_ErrorEnterPin", "Enter your PIN.");
            return;
        }

        IsBusy = true;
        try
        {
            var valid = await _securityService.VerifyPinAsync(Pin).ConfigureAwait(true);

            if (!valid)
            {
                _failedAttempts++;
                OnPropertyChanged(nameof(FailedAttempts));

                ErrorMessage = _failedAttempts >= 3
                    ? string.Format(
                        L("Str_ErrorIncorrectPinAttempts",
                            "Incorrect PIN ({0} failed attempts)."),
                        _failedAttempts)
                    : L("Str_ErrorIncorrectPin", "Incorrect PIN.");

                Pin = string.Empty;
                return;
            }

            _failedAttempts = 0;
            OnPropertyChanged(nameof(FailedAttempts));
            Pin = string.Empty;

            // Si el usuario marco "confiar en este dispositivo", persistimos
            // la confianza con la duracion elegida antes de desbloquear.
            if (TrustThisDevice)
            {
                await _securityService
                    .TrustDeviceAsync(SelectedTrustDuration?.Duration)
                    .ConfigureAwait(true);
            }

            RaiseUnlocked();
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>
    /// Dispara Unlocked garantizando que el manejador se ejecute en el hilo
    /// de UI (VerifyPinAsync usa ConfigureAwait(false) y podria dejarnos en
    /// un hilo del pool, lo que romperia DialogResult/Close en la ventana).
    /// </summary>
    private void RaiseUnlocked()
    {
        var handler = Unlocked;
        if (handler is null)
        {
            return;
        }

        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher is not null && !dispatcher.CheckAccess())
        {
            dispatcher.Invoke(() => handler(this, EventArgs.Empty));
        }
        else
        {
            handler(this, EventArgs.Empty);
        }
    }
}
