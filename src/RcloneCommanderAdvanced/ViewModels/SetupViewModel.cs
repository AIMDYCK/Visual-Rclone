using System;
using System.Threading.Tasks;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using RcloneCommanderAdvanced.Services.Abstractions;

namespace RcloneCommanderAdvanced.ViewModels;

/// <summary>
/// ViewModel del asistente de primer arranque (First-Run Setup).
/// Solicita la creacion del PIN maestro y valida la fortaleza minima.
/// </summary>
public sealed partial class SetupViewModel : ObservableObject
{
    private readonly ISecurityService _securityService;
    private readonly ILocalizationService _localization;

    public SetupViewModel(ISecurityService securityService, ILocalizationService localization)
    {
        _securityService = securityService;
        _localization = localization;
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
    private string _confirmPin = string.Empty;

    /// <summary>
    /// Frase de recordatorio opcional. Se muestra en la pantalla de bloqueo
    /// para ayudar al usuario a recordar el PIN si lo olvida.
    /// </summary>
    [ObservableProperty]
    private string _reminderPhrase = string.Empty;

    [ObservableProperty]
    private string _errorMessage = string.Empty;

    [ObservableProperty]
    private bool _isBusy;

    /// <summary>Se dispara cuando el PIN se creo correctamente.</summary>
    public event EventHandler? SetupCompleted;

    /// <summary>Longitud minima exigida al PIN.</summary>
    public int MinimumPinLength => 4;

    /// <summary>Texto de requisitos mostrado siempre en la ventana.</summary>
    public string RequirementText => string.Format(
        L("Str_ErrorPinRequirements", "Requirements: minimum {0} characters."),
        MinimumPinLength);

    /// <summary>
    /// Validacion en vivo: se reevalua cada vez que cambia el PIN o su
    /// confirmacion, de modo que el usuario ve el error mientras escribe
    /// y no solo al pulsar el boton.
    /// </summary>
    partial void OnPinChanged(string value) => Revalidate();

    partial void OnConfirmPinChanged(string value) => Revalidate();

    /// <summary>
    /// Recalcula el mensaje de error sin efectos secundarios. Devuelve true
    /// si el formulario es valido.
    /// </summary>
    private bool Revalidate()
    {
        // Mientras el usuario no haya escrito nada, no mostramos ruido.
        if (string.IsNullOrEmpty(Pin) && string.IsNullOrEmpty(ConfirmPin))
        {
            ErrorMessage = string.Empty;
            return false;
        }

        if (string.IsNullOrEmpty(Pin))
        {
            ErrorMessage = L("Str_ErrorEnterAPin", "Enter a PIN.");
            return false;
        }

        if (Pin.Length < MinimumPinLength)
        {
            ErrorMessage = string.Format(
                L("Str_ErrorPinTooShort",
                    "The PIN must be at least {0} characters (you have {1})."),
                MinimumPinLength, Pin.Length);
            return false;
        }

        if (string.IsNullOrEmpty(ConfirmPin))
        {
            ErrorMessage = L("Str_ErrorRepeatPin",
                "Repeat the PIN in the confirmation field.");
            return false;
        }

        if (!string.Equals(Pin, ConfirmPin, StringComparison.Ordinal))
        {
            ErrorMessage = L("Str_ErrorPinsDoNotMatch", "The PINs do not match.");
            return false;
        }

        ErrorMessage = string.Empty;
        return true;
    }

    [RelayCommand]
    private async Task CreatePinAsync()
    {
        if (!Revalidate())
        {
            // Si el mensaje quedo vacio por alguna razon, damos una pista.
            if (string.IsNullOrEmpty(ErrorMessage))
            {
                ErrorMessage = string.Format(
                    L("Str_ErrorPinTooShortPlain",
                        "The PIN must be at least {0} characters."),
                    MinimumPinLength);
            }

            return;
        }

        IsBusy = true;
        try
        {
            var created = await _securityService
                .CreateMasterPinAsync(Pin, ReminderPhrase)
                .ConfigureAwait(true);

            if (!created)
            {
                ErrorMessage = L("Str_ErrorPinAlreadyExists",
                    "A PIN is already configured.");
                return;
            }

            RaiseSetupCompleted();
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.IO.IOException)
        {
            ErrorMessage = string.Format(
                L("Str_ErrorSavePin", "Could not save the PIN: {0}"),
                ex.Message);
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>
    /// Dispara SetupCompleted garantizando que el manejador se ejecute en el
    /// hilo de UI (SaveAsync usa ConfigureAwait(false) y podria dejarnos en
    /// un hilo del pool, lo que romperia DialogResult/Close en la ventana).
    /// </summary>
    private void RaiseSetupCompleted()
    {
        var handler = SetupCompleted;
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
