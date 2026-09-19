using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using RcloneCommanderAdvanced.Services.Abstractions;

namespace RcloneCommanderAdvanced.ViewModels;

/// <summary>
/// FASE 4: ViewModel de la ventana modal que solicita la contrasena del
/// rclone.conf cifrado.
///
/// La validacion real la realiza <see cref="IConfigEncryptionService"/>: aqui
/// solo se recoge la contrasena, se delega la comprobacion y se expone el
/// mensaje de error si falla. La contrasena nunca se persiste.
/// </summary>
public sealed partial class PasswordPromptViewModel : ObservableObject
{
    private readonly IConfigEncryptionService _encryptionService;
    private readonly ILocalizationService _localization;

    public PasswordPromptViewModel(
        IConfigEncryptionService encryptionService,
        ILocalizationService localization)
    {
        _encryptionService = encryptionService;
        _localization = localization;
    }

    /// <summary>Contrasena introducida por el usuario (enlazada al PasswordBox).</summary>
    [ObservableProperty]
    private string _password = string.Empty;

    /// <summary>Mensaje de error mostrado cuando la contrasena es incorrecta.</summary>
    [ObservableProperty]
    private string _errorMessage = string.Empty;

    /// <summary>True mientras se valida la contrasena (deshabilita la UI).</summary>
    [ObservableProperty]
    private bool _isBusy;

    /// <summary>
    /// Resultado de la ultima validacion. La ventana lo consulta al cerrarse
    /// para decidir si continua el arranque o vuelve a pedir la contrasena.
    /// </summary>
    public bool IsUnlocked { get; private set; }

    /// <summary>
    /// Valida la contrasena contra rclone. Si es correcta, marca
    /// <see cref="IsUnlocked"/> y la ventana se cierra con exito.
    /// </summary>
    public bool Validate()
    {
        if (string.IsNullOrEmpty(Password))
        {
            ErrorMessage = _localization.Get(
                "Str_PasswordEmpty",
                "Please enter the configuration password.");
            return false;
        }

        IsBusy = true;
        try
        {
            var ok = _encryptionService.TryUnlock(Password);
            if (ok)
            {
                IsUnlocked = true;
                ErrorMessage = string.Empty;
                return true;
            }

            ErrorMessage = _localization.Get(
                "Str_PasswordIncorrect",
                "Incorrect password. Please try again.");
            Password = string.Empty;
            return false;
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>
    /// Limpia el estado del ViewModel. Se invoca al cerrar la ventana para no
    /// dejar la contrasena en memoria mas tiempo del necesario.
    /// </summary>
    public void Reset()
    {
        Password = string.Empty;
        ErrorMessage = string.Empty;
        IsBusy = false;
    }
}
