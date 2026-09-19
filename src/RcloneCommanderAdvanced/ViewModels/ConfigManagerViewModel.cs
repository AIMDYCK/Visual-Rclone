using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using RcloneCommanderAdvanced.Models;
using RcloneCommanderAdvanced.Services.Abstractions;
using RcloneCommanderAdvanced.Views;

namespace RcloneCommanderAdvanced.ViewModels;

/// <summary>
/// ViewModel del editor interactivo de rclone.conf (Fase 4).
///
/// Expone la lista de remotos como tarjetas y un asistente de 4 pasos para
/// crear uno nuevo. Toda la escritura se delega en <see cref="IRcloneConfigManager"/>,
/// que a su vez usa el binario oficial de rclone: nunca se edita el INI a mano.
/// </summary>
public sealed partial class ConfigManagerViewModel : ObservableObject
{
    private readonly IRcloneConfigParser _parser;
    private readonly IRcloneConfigManager _configManager;
    private readonly ILocalizationService _localization;

    /// <summary>
    /// Nombre de remoto valido: solo letras, digitos, guion y guion bajo.
    /// Es deliberadamente estricto para evitar nombres que rclone o el sistema
    /// de archivos de Windows no puedan manejar (espacios, dos puntos, etc.).
    /// </summary>
    private static readonly Regex RemoteNameRegex =
        new(@"^[a-zA-Z0-9_-]+$", RegexOptions.Compiled);

    public ConfigManagerViewModel(
        IRcloneConfigParser parser,
        IRcloneConfigManager configManager,
        ILocalizationService localization)
    {
        _parser = parser;
        _configManager = configManager;
        _localization = localization;

        Providers = new ObservableCollection<RemoteProvider>(RemoteProvider.All);
        AdvancedProviders = new ObservableCollection<RemoteProvider>(RemoteProvider.Advanced);
        SelectedProvider = Providers.FirstOrDefault();
        SelectedAdvancedProvider = AdvancedProviders.FirstOrDefault();

        _localization.LanguageChanged += (_, _) => RefreshLocalizedText();
    }

    // ---------------------------------------------------------------------
    // Lista de remotos
    // ---------------------------------------------------------------------

    /// <summary>Tarjetas de remotos detectados en el rclone.conf activo.</summary>
    public ObservableCollection<RemoteCardItem> Remotes { get; } = new();

    /// <summary>True cuando no hay ningun remoto configurado.</summary>
    [ObservableProperty]
    private bool _isEmpty;

    /// <summary>Ruta del rclone.conf que se esta editando.</summary>
    [ObservableProperty]
    private string _configPath = string.Empty;

    // ---------------------------------------------------------------------
    // Asistente (wizard)
    // ---------------------------------------------------------------------

    /// <summary>Paso actual del asistente (1..4). 0 = lista, sin asistente.</summary>
    [ObservableProperty]
    private int _wizardStep;

    /// <summary>Proveedores disponibles para el selector visual (tarjetas).</summary>
    public ObservableCollection<RemoteProvider> Providers { get; }

    /// <summary>
    /// Backends adicionales mostrados en el desplegable cuando el usuario elige
    /// la tarjeta "Otros / Avanzado".
    /// </summary>
    public ObservableCollection<RemoteProvider> AdvancedProviders { get; }

    [ObservableProperty]
    private RemoteProvider? _selectedProvider;

    /// <summary>Backend elegido en el desplegable de "Otros / Avanzado".</summary>
    [ObservableProperty]
    private RemoteProvider? _selectedAdvancedProvider;

    [ObservableProperty]
    private string _remoteName = string.Empty;

    [ObservableProperty]
    private string _clientId = string.Empty;

    [ObservableProperty]
    private string _clientSecret = string.Empty;

    /// <summary>Usuario para proveedores que autentican con usuario/contrasena.</summary>
    [ObservableProperty]
    private string _userName = string.Empty;

    /// <summary>Contrasena para proveedores que autentican con usuario/contrasena.</summary>
    [ObservableProperty]
    private string _password = string.Empty;

    // ---------------------------------------------------------------------
    // Cifrado (crypt)
    // ---------------------------------------------------------------------

    /// <summary>
    /// Remoto subyacente y ruta que se va a cifrar, en formato
    /// "remoto:/ruta" (ej: "MiDrive:/CarpetaSegura"). Es el parametro
    /// <c>remote</c> del backend crypt.
    /// </summary>
    [ObservableProperty]
    private string _cryptRemote = string.Empty;

    /// <summary>
    /// Contrasena de cifrado (password1). rclone la ofusca al guardarla en
    /// rclone.conf; aqui solo se transporta hasta el comando de creacion.
    /// </summary>
    [ObservableProperty]
    private string _cryptPassword = string.Empty;

    /// <summary>
    /// Modo de ofuscacion de nombres de archivo (filename_encryption):
    /// "standard", "obfuscate" u "off".
    /// </summary>
    [ObservableProperty]
    private string _cryptFilenameEncryption = "standard";

    /// <summary>Modos de ofuscacion de nombres soportados por crypt.</summary>
    public IReadOnlyList<string> CryptFilenameEncryptionModes { get; } = new[]
    {
        "standard",
        "obfuscate",
        "off"
    };

    /// <summary>Mensaje de validacion mostrado bajo el campo de nombre.</summary>
    [ObservableProperty]
    private string _validationMessage = string.Empty;

    /// <summary>
    /// True cuando el nombre introducido en el paso 1 es invalido (vacio, con
    /// caracteres prohibidos o ya existente). La vista lo usa para pintar el
    /// mensaje en rojo y bloquear el boton NEXT en tiempo real.
    /// </summary>
    [ObservableProperty]
    private bool _hasNameError;

    /// <summary>
    /// Opciones dinamicas del backend seleccionado, generadas a partir del
    /// esquema que rclone publica en <c>rclone config providers</c>. Es la
    /// fuente del formulario del paso 3: no hay campos hardcodeados.
    /// </summary>
    public ObservableCollection<ProviderOptionModel> WizardOptions { get; } = new();

    /// <summary>True mientras se esta descargando el esquema del backend.</summary>
    [ObservableProperty]
    private bool _isLoadingOptions;

    /// <summary>
    /// True cuando el backend efectivo no expone ninguna opcion configurable
    /// (por ejemplo "local" o "union"): el paso 3 muestra una nota informativa.
    /// </summary>
    [ObservableProperty]
    private bool _hasNoWizardOptions;

    /// <summary>True mientras se ejecuta una operacion de rclone.</summary>
    [ObservableProperty]
    private bool _isBusy;

    /// <summary>Texto de estado mostrado durante el flujo OAuth.</summary>
    [ObservableProperty]
    private string _busyMessage = string.Empty;

    /// <summary>Salida en crudo de rclone, util para diagnostico en la UI.</summary>
    [ObservableProperty]
    private string _operationLog = string.Empty;

    /// <summary>Mensaje de resultado final (exito o error).</summary>
    [ObservableProperty]
    private string _resultMessage = string.Empty;

    /// <summary>True si el ultimo resultado fue un error.</summary>
    [ObservableProperty]
    private bool _hasError;

    // ---------------------------------------------------------------------
    // Edicion de un remoto existente
    // ---------------------------------------------------------------------

    /// <summary>True cuando el panel de edicion de un remoto esta abierto.</summary>
    [ObservableProperty]
    private bool _isEditOpen;

    /// <summary>Nombre original del remoto que se esta editando (referencia para el rename).</summary>
    [ObservableProperty]
    private string _editOriginalName = string.Empty;

    /// <summary>Nombre editable del remoto (permite renombrar al guardar).</summary>
    [ObservableProperty]
    private string _editRemoteName = string.Empty;

    /// <summary>Tipo de backend del remoto en edicion (solo lectura: rclone no lo permite cambiar).</summary>
    [ObservableProperty]
    private string _editType = string.Empty;

    /// <summary>
    /// Diccionario dinamico con TODOS los pares clave/valor del remoto.
    /// Es la base del editor avanzado: se rellena al abrir y se vuelca
    /// entero a rclone al guardar.
    /// </summary>
    public ObservableCollection<ConfigOptionItem> EditOptions { get; } = new();

    /// <summary>Nombre legible del proveedor del remoto en edicion.</summary>
    [ObservableProperty]
    private string _editProviderName = string.Empty;

    /// <summary>Glifo del proveedor del remoto en edicion.</summary>
    [ObservableProperty]
    private string _editGlyph = "\uE753";

    /// <summary>Mensaje de validacion del panel de edicion.</summary>
    [ObservableProperty]
    private string _editValidationMessage = string.Empty;

    /// <summary>
    /// Valor actual de <c>client_id</c> del remoto en edicion. Se precarga al
    /// abrir el panel leyendo el bloque del remoto en rclone.conf y se vuelca
    /// con <c>rclone config update</c> al guardar. Vacio = limpiar la clave.
    /// </summary>
    [ObservableProperty]
    private string _editClientId = string.Empty;

    /// <summary>
    /// Valor actual de <c>client_secret</c> del remoto en edicion. Se precarga
    /// al abrir el panel y se vuelca con <c>rclone config update</c> al guardar.
    ///
    /// NOTA: rclone guarda este valor CIFRADO en rclone.conf, por lo que al
    /// leerlo de vuelta aparece como un blob opaco. Se muestra tal cual para
    /// que el usuario pueda reemplazarlo; si lo deja intacto se reenvia el
    /// mismo valor y rclone lo vuelve a cifrar sin cambios funcionales.
    /// Vacio = limpiar la clave en el .conf.
    /// </summary>
    [ObservableProperty]
    private string _editClientSecret = string.Empty;

    /// <summary>
    /// True cuando el backend del remoto en edicion admite credenciales OAuth
    /// propias (client_id/client_secret). Controla la visibilidad de la seccion
    /// "OAuth credentials" en el panel de edicion.
    /// </summary>
    [ObservableProperty]
    private bool _canEditOAuthCredentials;

    /// <summary>
    /// Copia de <see cref="EditClientId"/> tal como estaba al abrir el panel.
    /// Sirve para detectar si el usuario modifico la credencial y, en ese caso,
    /// forzar la reautenticacion OAuth tras guardar. No se enlaza a la vista.
    /// </summary>
    private string _originalClientId = string.Empty;

    /// <summary>
    /// Copia de <see cref="EditClientSecret"/> tal como estaba al abrir el panel.
    /// Sirve para detectar cambios en la credencial y disparar la reconexion.
    /// No se enlaza a la vista.
    /// </summary>
    private string _originalClientSecret = string.Empty;

    /// <summary>
    /// True cuando el remoto en edicion admite reautenticacion OAuth. Se activa
    /// solo si el backend declara la clave <c>token</c> en su esquema, que es
    /// la firma de los proveedores con flujo OAuth (drive, onedrive, dropbox...).
    /// </summary>
    [ObservableProperty]
    private bool _canReauthenticate;

    /// <summary>Mensaje de estado del proceso de reautenticacion OAuth.</summary>
    [ObservableProperty]
    private string _reauthStatusMessage = string.Empty;

    /// <summary>True mientras se ejecuta el flujo OAuth de reconexion.</summary>
    [ObservableProperty]
    private bool _isReauthenticating;

    /// <summary>
    /// Numero de claves sensibles ocultas del editor (token, client_secret...).
    /// Se muestra al usuario para que sepa que existen pero no son editables.
    /// </summary>
    [ObservableProperty]
    private int _hiddenSensitiveCount;

    /// <summary>
    /// True cuando hay al menos una clave sensible oculta. Se expone como bool
    /// porque el converter BoolToVisibility de la vista no interpreta enteros:
    /// enlazar el contador directamente dejaba el aviso siempre colapsado.
    /// </summary>
    public bool HasHiddenSensitive => HiddenSensitiveCount > 0;

    /// <summary>
    /// Aviso ya formateado con el numero de claves sensibles ocultas.
    ///
    /// La plantilla localizada (Str_SensitiveCount) contiene un marcador {0}
    /// que WPF NO sustituye al enlazarla con DynamicResource dentro de un Run:
    /// el texto se renderizaba literalmente como "2 {0} protected value(s)
    /// hidden". Aqui se resuelve la plantilla y se aplica string.Format para
    /// que el contador aparezca en su sitio sin exponer el marcador crudo.
    /// </summary>
    public string HiddenSensitiveMessage
    {
        get
        {
            var template = System.Windows.Application.Current?.TryFindResource("Str_SensitiveCount") as string;
            if (string.IsNullOrWhiteSpace(template))
            {
                template = L("Str_SensitiveCountFallback", "{0} protected value(s) hidden");
            }

            try
            {
                return string.Format(template, HiddenSensitiveCount);
            }
            catch (FormatException)
            {
                // Plantilla mal formada: degradar a un texto seguro sin marcador.
                return string.Format(
                    L("Str_SensitiveCountFallback", "{0} protected value(s) hidden"),
                    HiddenSensitiveCount);
            }
        }
    }

    partial void OnHiddenSensitiveCountChanged(int value)
    {
        OnPropertyChanged(nameof(HasHiddenSensitive));
        OnPropertyChanged(nameof(HiddenSensitiveMessage));
    }

    /// <summary>True cuando el panel de edicion esta visible y la lista oculta.</summary>
    public bool IsEditVisible => IsEditOpen;

    /// <summary>True cuando el asistente esta visible (paso >= 1).</summary>
    public bool IsWizardOpen => WizardStep >= 1;

    /// <summary>True cuando se muestra la lista de remotos.</summary>
    public bool IsListVisible => WizardStep == 0;

    /// <summary>
    /// True cuando el usuario ha elegido la tarjeta "Otros / Avanzado"; en ese
    /// caso se muestra el desplegable con el resto de backends.
    /// </summary>
    public bool IsAdvancedProviderSelected => SelectedProvider?.IsAdvanced == true;

    /// <summary>
    /// Proveedor efectivo con el que se creara el remoto: si se eligio
    /// "Otros / Avanzado" se usa el del desplegable; en caso contrario, la
    /// tarjeta seleccionada.
    /// </summary>
    public RemoteProvider? EffectiveProvider =>
        IsAdvancedProviderSelected ? SelectedAdvancedProvider : SelectedProvider;

    /// <summary>True si el proveedor efectivo requiere credenciales OAuth.</summary>
    public bool ProviderRequiresOAuth => EffectiveProvider?.RequiresOAuth == true;

    /// <summary>True si el proveedor efectivo requiere claves (S3, B2...).</summary>
    public bool ProviderRequiresKeys => EffectiveProvider?.RequiresKeys == true;

    /// <summary>True si el proveedor efectivo requiere usuario y contrasena.</summary>
    public bool ProviderRequiresUserPassword => EffectiveProvider?.RequiresUserPassword == true;

    /// <summary>
    /// True si el proveedor efectivo es la capa de cifrado (crypt). En ese caso
    /// el paso 3 muestra el formulario especifico: remoto subyacente, ruta,
    /// contrasena y modo de ofuscacion de nombres.
    /// </summary>
    public bool ProviderRequiresCrypt => EffectiveProvider?.RequiresCrypt == true;

    /// <summary>
    /// True cuando el proveedor efectivo no necesita credenciales interactivas
    /// (local, HTTP publico, union...). Se muestra una nota informativa.
    /// </summary>
    public bool ProviderRequiresNoCredentials => EffectiveProvider?.RequiresNoCredentials == true;

    /// <summary>
    /// True cuando el backend efectivo usa el flujo OAuth interactivo (Drive,
    /// Dropbox, OneDrive...). En ese caso el paso 4 ofrece "AUTHENTICATE IN
    /// BROWSER" y rclone abre el navegador para capturar el token.
    /// </summary>
    public bool IsOAuthFlow => EffectiveProvider?.RequiresOAuth == true;

    /// <summary>
    /// True cuando el backend efectivo NO usa OAuth: el paso 4 ensena
    /// "TEST CONNECTION & SAVE" y valida con <c>rclone lsd</c>.
    /// </summary>
    public bool IsDirectFlow => !IsOAuthFlow;

    /// <summary>
    /// Texto del boton principal del paso 4, dependiente del flujo del backend.
    /// </summary>
    public string AuthenticateButtonText => IsOAuthFlow
        ? L("Str_AuthenticateInBrowser", "AUTHENTICATE IN BROWSER")
        : L("Str_TestAndSave", "TEST CONNECTION & SAVE");

    /// <summary>
    /// Texto de estado mostrado durante el paso 4, dependiente del flujo.
    /// </summary>
    public string AuthenticateHintText => IsOAuthFlow
        ? L("Str_OAuthHint", "rclone will open your browser to complete the sign-in.")
        : L("Str_DirectHint", "The remote will be created and tested against the provider.");

    /// <summary>Titulo del paso actual del asistente.</summary>
    public string WizardStepTitle => WizardStep switch
    {
        1 => L("Str_WizardStepName", "Step 1 of 4 - Remote name"),
        2 => L("Str_WizardStepProvider", "Step 2 of 4 - Provider"),
        3 => L("Str_WizardStepCredentials", "Step 3 of 4 - Credentials"),
        4 => L("Str_WizardStepAuthenticate", "Step 4 of 4 - Authenticate"),
        _ => string.Empty
    };

    // ---------------------------------------------------------------------
    // Ciclo de vida
    // ---------------------------------------------------------------------

    /// <summary>Carga la lista de remotos desde el rclone.conf activo.</summary>
    public void Load()
    {
        ConfigPath = _parser.ResolvedConfigPath;
        Remotes.Clear();

        foreach (var entry in _parser.Parse())
        {
            Remotes.Add(new RemoteCardItem(entry));
        }

        IsEmpty = Remotes.Count == 0;
    }

    /// <summary>
    /// Inicializacion asincrona disparada por el evento Loaded de la vista.
    ///
    /// Se mantiene deliberadamente separada del constructor: ningun proceso
    /// externo (rclone) debe ejecutarse durante la construccion de la ventana,
    /// porque un fallo ahi impediria que la vista llegue a mostrarse. Aqui la
    /// vista ya esta montada, de modo que cualquier error se puede reportar de
    /// forma visible sin bloquear la navegacion.
    /// </summary>
    [RelayCommand]
    private void Loaded()
    {
        try
        {
            Load();
        }
        catch (Exception ex)
        {
            System.Windows.MessageBox.Show(
                $"{L("Str_ErrorLoadingRemotes", "Error loading the remote list:")}\n\n{ex}",
                L("Str_ErrorOpeningEditor", "Error opening editor"),
                System.Windows.MessageBoxButton.OK,
                System.Windows.MessageBoxImage.Error);
        }
    }

    // ---------------------------------------------------------------------
    // Comandos del asistente
    // ---------------------------------------------------------------------

    /// <summary>Abre el asistente en el paso 1 con el estado limpio.</summary>
    [RelayCommand]
    private void StartAddRemote()
    {
        RemoteName = string.Empty;
        ClientId = string.Empty;
        ClientSecret = string.Empty;
        UserName = string.Empty;
        Password = string.Empty;
        ValidationMessage = string.Empty;
        HasNameError = false;
        ResultMessage = string.Empty;
        HasError = false;
        OperationLog = string.Empty;
        WizardOptions.Clear();
        HasNoWizardOptions = false;
        SelectedProvider = Providers.FirstOrDefault();
        SelectedAdvancedProvider = AdvancedProviders.FirstOrDefault();
        WizardStep = 1;
        OnPropertyChanged(nameof(IsWizardOpen));
        OnPropertyChanged(nameof(IsListVisible));
    }

    /// <summary>Cierra el asistente y vuelve a la lista.</summary>
    [RelayCommand]
    private void CancelWizard()
    {
        WizardStep = 0;
        ValidationMessage = string.Empty;
        ResultMessage = string.Empty;
        HasError = false;
        OnPropertyChanged(nameof(IsWizardOpen));
        OnPropertyChanged(nameof(IsListVisible));
    }

    /// <summary>Avanza al siguiente paso validando el actual.</summary>
    [RelayCommand]
    private async Task NextStepAsync()
    {
        if (WizardStep == 1 && !ValidateRemoteName())
        {
            return;
        }

        if (WizardStep >= 4)
        {
            return;
        }

        WizardStep++;
        ValidationMessage = string.Empty;
        OnPropertyChanged(nameof(WizardStepTitle));

        // Al entrar en el paso 3 se descarga el esquema del backend elegido y
        // se genera el formulario dinamico. Es asincrono para no bloquear la UI
        // mientras rclone responde.
        if (WizardStep == 3)
        {
            await LoadWizardOptionsAsync().ConfigureAwait(true);
        }
    }

    /// <summary>Retrocede al paso anterior.</summary>
    [RelayCommand]
    private void PreviousStep()
    {
        if (WizardStep > 1)
        {
            WizardStep--;
            ValidationMessage = string.Empty;
            OnPropertyChanged(nameof(WizardStepTitle));
        }
    }

    /// <summary>
    /// Abre en el navegador la documentacion oficial de rclone para crear un
    /// Client ID propio del proveedor seleccionado. Es una accion puramente
    /// informativa: no modifica la configuracion.
    /// </summary>
    [RelayCommand]
    private void OpenProviderDocs()
    {
        var provider = SelectedProvider;
        if (provider is null || string.IsNullOrWhiteSpace(provider.Type))
        {
            return;
        }

        var url = $"https://rclone.org/{provider.Type}/#making-your-own-client-id";

        try
        {
            Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            // No queremos que un fallo al abrir el navegador rompa el asistente.
            ResultMessage = ex.Message;
            HasError = true;
        }
    }

    /// <summary>
    /// Ejecuta la creacion del remoto con los datos recopilados. Para los
    /// proveedores OAuth, rclone abrira el navegador para completar el flujo;
    /// la salida se refleja en <see cref="OperationLog"/> mientras tanto.
    /// </summary>
    [RelayCommand]
    private async Task AuthenticateAsync()
    {
        if (!ValidateRemoteName())
        {
            WizardStep = 1;
            OnPropertyChanged(nameof(WizardStepTitle));
            return;
        }

        var provider = EffectiveProvider;
        if (provider is null || provider.IsAdvanced)
        {
            // No se puede crear un remoto con la tarjeta "Otros / Avanzado"
            // sin haber elegido un backend concreto en el desplegable.
            ValidationMessage = L("Str_AdvancedProviderRequired",
                "Please choose a backend from the list.");
            WizardStep = 2;
            OnPropertyChanged(nameof(WizardStepTitle));
            return;
        }

        // Crypt exige remoto subyacente y contrasena: sin ellos rclone crearia
        // un remoto inutil. Se valida antes de lanzar el proceso.
        if (provider.RequiresCrypt)
        {
            if (string.IsNullOrWhiteSpace(CryptRemote))
            {
                ValidationMessage = L("Str_CryptRemoteRequired",
                    "Please enter the underlying remote and path (e.g. MyDrive:/SecureFolder).");
                WizardStep = 3;
                OnPropertyChanged(nameof(WizardStepTitle));
                return;
            }

            if (string.IsNullOrWhiteSpace(CryptPassword))
            {
                ValidationMessage = L("Str_CryptPasswordRequired",
                    "Please enter the encryption password.");
                WizardStep = 3;
                OnPropertyChanged(nameof(WizardStepTitle));
                return;
            }
        }

        IsBusy = true;
        HasError = false;
        ResultMessage = string.Empty;
        OperationLog = string.Empty;
        BusyMessage = IsOAuthFlow
            ? L("Str_Authenticating", "Waiting for OAuth in your browser...")
            : L("Str_TestingConnection", "Creating and testing the remote...");

        try
        {
            var parameters = BuildParameters(provider);

            var result = await _configManager.CreateRemoteAsync(
                RemoteName.Trim(),
                provider.Type,
                parameters,
                line => OperationLog += line + Environment.NewLine)
                .ConfigureAwait(true);

            if (!result.Success)
            {
                ResultMessage = result.Message;
                HasError = true;
                return;
            }

            // Flujo no-OAuth: validamos el remoto recien creado con una lectura
            // ligera. Si falla, se elimina para no dejar un remoto roto en la
            // configuracion y se informa del error.
            if (IsDirectFlow)
            {
                BusyMessage = L("Str_TestingConnection", "Creating and testing the remote...");

                var test = await _configManager.TestRemoteAsync(
                    RemoteName.Trim(),
                    line => OperationLog += line + Environment.NewLine)
                    .ConfigureAwait(true);

                if (!test.Success)
                {
                    await _configManager.DeleteRemoteAsync(RemoteName.Trim())
                        .ConfigureAwait(true);

                    ResultMessage = string.Format(
                        L("Str_StatusTestFailed",
                            "The remote was created but the connection test failed: {0}"),
                        test.Message);
                    HasError = true;
                    return;
                }
            }

            ResultMessage = string.Format(
                L("Str_StatusRemoteCreated", "Remote \"{0}\" created successfully."),
                RemoteName.Trim());
            HasError = false;

            Load();
            WizardStep = 0;
            OnPropertyChanged(nameof(IsWizardOpen));
            OnPropertyChanged(nameof(IsListVisible));
        }
        finally
        {
            IsBusy = false;
            BusyMessage = string.Empty;
        }
    }

    /// <summary>
    /// Elimina el remoto indicado por la tarjeta.
    ///
    /// Antes de tocar rclone.conf se muestra un dialogo de confirmacion con la
    /// estetica oscura de la app (nunca el MessageBox nativo): el borrado es
    /// irreversible, asi que solo se ejecuta si el usuario confirma de forma
    /// explicita. Si cancela, no se modifica nada.
    /// </summary>
    [RelayCommand]
    private async Task DeleteRemoteAsync(RemoteCardItem? card)
    {
        if (card is null || IsBusy)
        {
            return;
        }

        // Confirmacion previa obligatoria (accion destructiva e irreversible).
        var confirm = new ConfirmDialog
        {
            Owner = System.Windows.Application.Current?.MainWindow,
            HeaderText = L("Str_DeleteConfirmHeader", "DELETE REMOTE"),
            MessageText = string.Format(
                L("Str_DeleteConfirmMessage",
                    "Are you sure you want to delete the remote '{0}' from rclone.conf? This action cannot be undone."),
                card.Name),
            ConfirmText = L("Str_DeleteConfirmButton", "DELETE"),
            CancelText = L("Str_DeleteCancelButton", "CANCEL")
        };

        if (confirm.ShowDialog() != true)
        {
            // El usuario cancelo: se aborta el borrado sin tocar rclone.conf.
            Debug.WriteLine($"[delete] Borrado de '{card.Name}' cancelado por el usuario.");
            return;
        }

        IsBusy = true;
        HasError = false;
        ResultMessage = string.Empty;

        try
        {
            var result = await _configManager.DeleteRemoteAsync(card.Name).ConfigureAwait(true);

            if (result.Success)
            {
                ResultMessage = string.Format(
                    L("Str_StatusRemoteDeleted", "Remote \"{0}\" deleted."),
                    card.Name);
                HasError = false;
                Load();
            }
            else
            {
                ResultMessage = result.Message;
                HasError = true;
            }
        }
        finally
        {
            IsBusy = false;
        }
    }

    // ---------------------------------------------------------------------
    // Edicion de un remoto existente
    // ---------------------------------------------------------------------

    /// <summary>
    /// Abre el panel de edicion para la tarjeta indicada, precargando su
    /// nombre y TODOS los pares clave/valor del bloque en rclone.conf.
    /// El tipo de backend se muestra como solo lectura porque rclone no
    /// permite cambiarlo en un remoto existente.
    /// </summary>
    [RelayCommand]
    private async Task EditRemoteAsync(RemoteCardItem? card)
    {
        if (card is null || IsBusy)
        {
            return;
        }

        try
        {
            EditOriginalName = card.Name;
            EditRemoteName = card.Name;
            EditType = card.Type;
            EditProviderName = card.ProviderName;
            EditGlyph = card.Glyph;
            EditValidationMessage = string.Empty;
            ReauthStatusMessage = string.Empty;
            ResultMessage = string.Empty;
            HasError = false;
            CanReauthenticate = false;
            HiddenSensitiveCount = 0;

            // Precargar las credenciales OAuth propias del remoto (si existen)
            // para que el usuario pueda verlas/modificarlas en el panel.
            // rclone guarda client_secret CIFRADO en rclone.conf, por lo que el
            // valor leido es el blob cifrado; se muestra tal cual y, si el
            // usuario no lo toca, se reenvia identico (rclone lo re-cifra).
            EditClientId = card.RawKeys.TryGetValue("client_id", out var existingId)
                ? existingId
                : string.Empty;
            EditClientSecret = card.RawKeys.TryGetValue("client_secret", out var existingSecret)
                ? existingSecret
                : string.Empty;
            CanEditOAuthCredentials = false;

            // Guardar copia de los valores originales para detectar, al guardar,
            // si el usuario modifico las credenciales OAuth. Un cambio obliga a
            // reautorizar (rclone config reconnect) para emitir tokens nuevos.
            _originalClientId = EditClientId;
            _originalClientSecret = EditClientSecret;

            // Volcar el diccionario del remoto en filas editables.
            //
            // La clave "type" se EXCLUYE por completo: es estructural (define el
            // backend) y ya se muestra arriba en el TextBox estatico "Backend
            // type". Dejarla en la tabla inferior permitia borrarla y corromper
            // el remoto, asi que no debe existir como par clave/valor editable.
            EditOptions.Clear();
            foreach (var kvp in card.RawKeys.OrderBy(k => k.Key, StringComparer.OrdinalIgnoreCase))
            {
                if (kvp.Key.Equals("type", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                EditOptions.Add(new ConfigOptionItem(kvp.Key, kvp.Value));
            }

            // Abrir el panel ANTES de consultar el esquema: asi el usuario ve
            // los campos basicos (nombre, tipo) aunque el enriquecimiento falle.
            IsEditOpen = true;
            OnPropertyChanged(nameof(IsEditVisible));
        }
        catch (Exception ex)
        {
            System.Windows.MessageBox.Show(
                $"{L("Str_ErrorPreparingEditor", "Error preparing the editor:")}\n\n{ex}",
                L("Str_ErrorOpeningEditor", "Error opening editor"),
                System.Windows.MessageBoxButton.OK,
                System.Windows.MessageBoxImage.Error);
            return;
        }

        // Enriquecer con el esquema del backend: ayuda, valores validos y
        // deteccion de claves sensibles. Si rclone no responde o el parseo
        // falla, el editor sigue siendo funcional en modo simple.
        try
        {
            await ApplyProviderSchemaAsync(card.Type).ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            System.Windows.MessageBox.Show(
                $"{string.Format(L("Str_ErrorLoadingSchema", "Error loading the backend schema '{0}':"), card.Type)}\n\n{ex}",
                L("Str_ErrorOpeningEditor", "Error opening editor"),
                System.Windows.MessageBoxButton.OK,
                System.Windows.MessageBoxImage.Warning);
        }
    }

    /// <summary>
    /// Consulta el esquema del backend y lo aplica a cada fila del editor.
    /// Oculta las claves sensibles (token, client_secret...) y habilita el
    /// boton de reautenticacion OAuth cuando el backend lo soporta.
    /// </summary>
    private async Task ApplyProviderSchemaAsync(string type)
    {
        var schema = await _configManager.GetProviderSchemaAsync(type).ConfigureAwait(true);
        if (schema is null)
        {
            return;
        }

        var hidden = 0;

        foreach (var option in EditOptions)
        {
            var schemaOption = schema.FindOption(option.Key);
            option.ApplySchema(schemaOption);

            if (option.IsSensitive)
            {
                hidden++;
            }
        }

        HiddenSensitiveCount = hidden;

        // El backend soporta OAuth si su esquema declara la clave "token".
        CanReauthenticate = schema.FindOption("token") is not null;

        // El backend admite credenciales OAuth propias (client_id/client_secret)
        // si su esquema declara al menos una de esas dos claves. En ese caso se
        // muestra la seccion "OAuth credentials" del panel de edicion.
        CanEditOAuthCredentials =
            schema.FindOption("client_id") is not null
            || schema.FindOption("client_secret") is not null;
    }

    /// <summary>
    /// Lanza el flujo OAuth nativo de rclone para renovar el token del remoto.
    /// rclone abre el navegador, captura el token nuevo y actualiza
    /// rclone.conf sin que el usuario toque el JSON a mano.
    /// </summary>
    [RelayCommand]
    private async Task ReauthenticateAsync()
    {
        if (IsBusy || IsReauthenticating || string.IsNullOrWhiteSpace(EditOriginalName))
        {
            return;
        }

        IsReauthenticating = true;
        ReauthStatusMessage = L("Str_ReauthInProgress",
            "Opening the browser to renew the token...");
        HasError = false;

        try
        {
            var result = await _configManager
                .ReconnectRemoteAsync(EditOriginalName, line =>
                {
                    if (!string.IsNullOrWhiteSpace(line))
                    {
                        ReauthStatusMessage = line.Trim();
                    }
                })
                .ConfigureAwait(true);

            if (result.Success)
            {
                ReauthStatusMessage = L("Str_ReauthSuccess",
                    "Token renewed successfully.");
                HasError = false;

                // Recargar el remoto para reflejar el token nuevo en memoria.
                Load();
            }
            else
            {
                ReauthStatusMessage = result.Message;
                HasError = true;
            }
        }
        finally
        {
            IsReauthenticating = false;
        }
    }

    /// <summary>Cierra el panel de edicion sin guardar cambios.</summary>
    [RelayCommand]
    private void CancelRemoteEdit()
    {
        IsEditOpen = false;
        EditValidationMessage = string.Empty;
        EditOptions.Clear();
        OnPropertyChanged(nameof(IsEditVisible));
    }

    /// <summary>
    /// Anade una fila vacia al editor para que el usuario pueda inyectar
    /// manualmente un parametro de rclone que no estuviera presente.
    /// </summary>
    [RelayCommand]
    private void AddOption()
    {
        EditOptions.Add(new ConfigOptionItem(string.Empty, string.Empty));
    }

    /// <summary>Elimina del editor la fila indicada (salvo las protegidas).</summary>
    [RelayCommand]
    private void RemoveOption(ConfigOptionItem? option)
    {
        if (option is null || option.IsProtected)
        {
            return;
        }

        EditOptions.Remove(option);
    }

    /// <summary>
    /// Guarda los cambios del remoto en edicion. Si el nombre cambio, primero
    /// renombra el remoto con <c>rclone config rename</c>; despues vuelca el
    /// diccionario completo con <c>rclone config update</c>.
    /// </summary>
    [RelayCommand]
    private async Task SaveRemoteEditAsync()
    {
        if (IsBusy || string.IsNullOrWhiteSpace(EditOriginalName))
        {
            return;
        }

        var newName = EditRemoteName?.Trim() ?? string.Empty;

        // Validacion del nombre: obligatorio y sin caracteres prohibidos.
        if (string.IsNullOrWhiteSpace(newName))
        {
            EditValidationMessage = L("Str_RemoteNameRequired", "Please enter a remote name.");
            return;
        }

        if (!RemoteNameRegex.IsMatch(newName))
        {
            EditValidationMessage = L("Str_RemoteNameInvalid",
                "The name cannot contain spaces or the characters \\ / : * ? \" < > |");
            return;
        }

        // Validacion de las claves/valores: ninguna clave puede estar vacia ni
        // contener '=' (romperia el par clave=valor de rclone).
        foreach (var option in EditOptions)
        {
            var key = option.Key?.Trim() ?? string.Empty;

            if (string.IsNullOrWhiteSpace(key))
            {
                EditValidationMessage = L("Str_EditKeyRequired",
                    "Every option needs a key name.");
                return;
            }

            if (key.Contains('=') || key.Contains('\n') || key.Contains('\r'))
            {
                EditValidationMessage = L("Str_EditKeyInvalid",
                    "Option keys cannot contain '=' or line breaks.");
                return;
            }
        }

        // Detectar claves duplicadas (comparacion sin distinguir mayusculas).
        var duplicate = EditOptions
            .GroupBy(o => o.Key.Trim(), StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault(g => g.Count() > 1);

        if (duplicate is not null)
        {
            EditValidationMessage = string.Format(
                L("Str_EditKeyDuplicate", "The key \"{0}\" is repeated."),
                duplicate.Key);
            return;
        }

        EditValidationMessage = string.Empty;
        IsBusy = true;
        HasError = false;
        ResultMessage = string.Empty;
        BusyMessage = L("Str_SavingRemote", "Saving changes...");

        try
        {
            var currentName = EditOriginalName;

            // 1) Renombrar si el usuario cambio el nombre.
            if (!string.Equals(newName, EditOriginalName, StringComparison.Ordinal))
            {
                var renameResult = await _configManager
                    .RenameRemoteAsync(EditOriginalName, newName)
                    .ConfigureAwait(true);

                if (!renameResult.Success)
                {
                    ResultMessage = renameResult.Message;
                    HasError = true;
                    return;
                }

                currentName = newName;
            }

            // 2) Volcar el diccionario completo (todas las claves, incluidas
            //    las anadidas a mano). rclone cifra los secretos por nosotros.
            //
            //    SEGURIDAD: las claves sensibles (token, client_secret, etc.)
            //    NUNCA se reenvian. Si las enviaramos vacias o con el JSON
            //    crudo, romperiamos la autenticacion. rclone conserva el valor
            //    cifrado existente cuando la clave no aparece en el update.
            var parameters = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var option in EditOptions)
            {
                if (option.IsSensitive)
                {
                    continue;
                }

                parameters[option.Key.Trim()] = option.Value ?? string.Empty;
            }

            // 3) Credenciales OAuth propias (client_id / client_secret).
            //
            //    Se envian SIEMPRE que el backend las admita, incluso vacias:
            //    rclone interpreta un valor vacio como "borrar la clave" del
            //    .conf, que es justo lo que el usuario espera al dejar el campo
            //    en blanco. Si el campo conserva el valor precargado, se reenvia
            //    identico (rclone lo re-cifra sin cambios funcionales).
            if (CanEditOAuthCredentials)
            {
                parameters["client_id"] = EditClientId?.Trim() ?? string.Empty;
                parameters["client_secret"] = EditClientSecret?.Trim() ?? string.Empty;
            }

            // 4) Detectar si el usuario cambio las credenciales OAuth propias.
            //
            //    Un cambio en client_id/client_secret invalida los tokens de
            //    acceso existentes (estaban vinculados a las credenciales
            //    anteriores), por lo que rclone necesita reautorizar la cuenta
            //    para emitir tokens nuevos. Se avisa al usuario ANTES de tocar
            //    nada: si cancela, se aborta el guardado por completo.
            var credentialsChanged = CanEditOAuthCredentials
                && (!string.Equals(_originalClientId, EditClientId?.Trim() ?? string.Empty, StringComparison.Ordinal)
                    || !string.Equals(_originalClientSecret, EditClientSecret?.Trim() ?? string.Empty, StringComparison.Ordinal));

            if (credentialsChanged)
            {
                var confirmation = System.Windows.MessageBox.Show(
                    L("Str_OAuthReauthConfirmMessage",
                        "Changes to the API credentials were detected. The browser will open so you can sign in again and generate the new tokens. Do you want to continue?"),
                    L("Str_OAuthReauthConfirmTitle", "Reauthorization required"),
                    System.Windows.MessageBoxButton.YesNo,
                    System.Windows.MessageBoxImage.Warning,
                    System.Windows.MessageBoxResult.No);

                if (confirmation != System.Windows.MessageBoxResult.Yes)
                {
                    // El usuario cancelo: no se guarda nada y el panel sigue abierto.
                    Debug.WriteLine("[edit] Reautenticacion cancelada por el usuario; guardado abortado.");
                    return;
                }
            }

            // Traza de diagnostico: se imprime el comando exacto que se va a
            // ejecutar (con los secretos enmascarados) para verificar que las
            // variables NO viajan nulas antes de lanzar rclone.
            Debug.WriteLine(
                $"[edit] rclone config update {currentName} " +
                $"client_id={(string.IsNullOrEmpty(EditClientId) ? "<empty>" : "<set>")} " +
                $"client_secret={(string.IsNullOrEmpty(EditClientSecret) ? "<empty>" : $"<set:len={EditClientSecret.Length}>")} " +
                $"(+{parameters.Count} params)");

            var result = await _configManager
                .UpdateRemoteAsync(currentName, parameters)
                .ConfigureAwait(true);

            if (result.Success)
            {
                ResultMessage = string.Format(
                    L("Str_StatusRemoteUpdated", "Remote \"{0}\" updated."),
                    currentName);
                HasError = false;

                // 5) Si cambiaron las credenciales OAuth, forzar la reautorizacion.
                //
                //    Los tokens existentes estaban vinculados a las credenciales
                //    anteriores y quedan invalidados. rclone config reconnect abre
                //    el navegador, captura el token nuevo y lo guarda en el .conf.
                //    Se ejecuta DESPUES del update para que rclone use ya el
                //    client_id/client_secret nuevos al emitir el token.
                if (credentialsChanged)
                {
                    BusyMessage = L("Str_ReauthInProgress",
                        "Opening the browser to renew the token...");

                    Debug.WriteLine(
                        $"[edit] rclone config reconnect {currentName}: (credenciales cambiadas)");

                    var reconnectResult = await _configManager
                        .ReconnectRemoteAsync(currentName, line =>
                        {
                            if (!string.IsNullOrWhiteSpace(line))
                            {
                                BusyMessage = line.Trim();
                            }
                        })
                        .ConfigureAwait(true);

                    if (!reconnectResult.Success)
                    {
                        // El update ya se aplico; solo fallo la reautorizacion.
                        // Se informa al usuario sin revertir el guardado.
                        ResultMessage = reconnectResult.Message;
                        HasError = true;
                        IsEditOpen = false;
                        EditOptions.Clear();
                        OnPropertyChanged(nameof(IsEditVisible));
                        Load();
                        return;
                    }

                    Debug.WriteLine("[edit] Reautenticacion OAuth completada.");
                }

                IsEditOpen = false;
                EditOptions.Clear();
                OnPropertyChanged(nameof(IsEditVisible));
                Load();
            }
            else
            {
                ResultMessage = result.Message;
                HasError = true;
            }
        }
        finally
        {
            IsBusy = false;
            BusyMessage = string.Empty;
        }
    }

    // ---------------------------------------------------------------------
    // Helpers
    // ---------------------------------------------------------------------

    /// <summary>
    /// Descarga el esquema del backend efectivo desde rclone y regenera el
    /// formulario dinamico del paso 3. Si rclone no esta disponible o el
    /// backend no expone opciones, la UI degrada a una nota informativa en
    /// lugar de romperse.
    /// </summary>
    private async Task LoadWizardOptionsAsync()
    {
        var provider = EffectiveProvider;
        if (provider is null || provider.IsAdvanced)
        {
            WizardOptions.Clear();
            HasNoWizardOptions = true;
            return;
        }

        // Crypt no usa el formulario dinamico del esquema: el paso 3 muestra
        // campos propios (remoto subyacente, contrasena, ofuscacion). Se evita
        // descargar el esquema para no pintar opciones irrelevantes.
        if (provider.RequiresCrypt)
        {
            WizardOptions.Clear();
            HasNoWizardOptions = false;
            return;
        }

        IsLoadingOptions = true;
        try
        {
            var schema = await _configManager
                .GetProviderSchemaAsync(provider.Type)
                .ConfigureAwait(true);

            WizardOptions.Clear();
            foreach (var option in ProviderOptionModel.FromSchema(schema))
            {
                WizardOptions.Add(option);
            }

            HasNoWizardOptions = WizardOptions.Count == 0;
        }
        finally
        {
            IsLoadingOptions = false;
        }
    }

    /// <summary>
    /// Construye los parametros clave/valor a partir del formulario dinamico
    /// del paso 3. Solo se envian las opciones con valor no vacio, de modo que
    /// rclone aplique sus propios valores por defecto al resto.
    ///
    /// BUGFIX (OAuth client_secret): se normaliza cada valor antes de enviarlo.
    /// Los secretos copiados de la consola del proveedor suelen llegar con
    /// comillas envolventes ("GOCSPX-...") o con espacios/saltos de linea; si se
    /// reenvian tal cual, rclone recibe un secreto distinto del real y el
    /// intercambio OAuth falla con "client_secret is missing" o "invalid_client".
    /// Aqui se recortan espacios y se eliminan las comillas envolventes, sin
    /// tocar las comillas internas que formen parte legitima del valor.
    /// </summary>
    private Dictionary<string, string> BuildParameters(RemoteProvider provider)
    {
        var parameters = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        // Crypt no usa el formulario dinamico del esquema: sus parametros
        // (remote, password, filename_encryption) se recogen en campos propios
        // del paso 3. Se ensamblan aqui para que el resto del flujo de creacion
        // (CreateRemoteAsync) los reciba igual que cualquier otro backend.
        if (provider.RequiresCrypt)
        {
            var cryptRemote = NormalizeOptionValue(CryptRemote);
            if (!string.IsNullOrWhiteSpace(cryptRemote))
            {
                parameters["remote"] = cryptRemote;
            }

            var cryptPassword = NormalizeOptionValue(CryptPassword);
            if (!string.IsNullOrWhiteSpace(cryptPassword))
            {
                parameters["password"] = cryptPassword;
            }

            var filenameEncryption = NormalizeOptionValue(CryptFilenameEncryption);
            if (!string.IsNullOrWhiteSpace(filenameEncryption))
            {
                parameters["filename_encryption"] = filenameEncryption;
            }

            return parameters;
        }

        foreach (var option in WizardOptions)
        {
            var value = NormalizeOptionValue(option.Value);
            if (string.IsNullOrWhiteSpace(value))
            {
                continue;
            }

            parameters[option.Name] = value;
        }

        return parameters;
    }

    /// <summary>
    /// Normaliza el valor introducido por el usuario para un parametro de
    /// rclone: recorta espacios exteriores y elimina comillas simples o dobles
    /// que envuelvan TODO el valor (patron habitual al copiar credenciales).
    /// No altera comillas internas ni el contenido significativo.
    /// </summary>
    private static string NormalizeOptionValue(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return string.Empty;
        }

        var value = raw.Trim();

        // Eliminar comillas envolventes balanceadas ("..." o '...').
        while (value.Length >= 2)
        {
            var first = value[0];
            var last = value[value.Length - 1];

            var wrappedInDouble = first == '"' && last == '"';
            var wrappedInSingle = first == '\'' && last == '\'';

            if (!wrappedInDouble && !wrappedInSingle)
            {
                break;
            }

            value = value.Substring(1, value.Length - 2).Trim();
        }

        return value;
    }

    /// <summary>Valida el nombre del remoto y actualiza el mensaje de error.</summary>
    private bool ValidateRemoteName()
    {
        var name = RemoteName?.Trim() ?? string.Empty;

        if (string.IsNullOrWhiteSpace(name))
        {
            ValidationMessage = L("Str_RemoteNameRequired", "Please enter a remote name.");
            return false;
        }

        if (!RemoteNameRegex.IsMatch(name))
        {
            ValidationMessage = L("Str_RemoteNameInvalid",
                "The name cannot contain spaces or the characters \\ / : * ? \" < > |");
            return false;
        }

        if (_configManager.RemoteExists(name))
        {
            ValidationMessage = L("Str_RemoteNameExists", "A remote with that name already exists.");
            return false;
        }

        ValidationMessage = string.Empty;
        return true;
    }

    /// <summary>Refresca las propiedades dependientes del idioma activo.</summary>
    private void RefreshLocalizedText()
    {
        OnPropertyChanged(nameof(WizardStepTitle));
        OnPropertyChanged(nameof(AuthenticateButtonText));
        OnPropertyChanged(nameof(AuthenticateHintText));
    }

    /// <summary>
    /// Validacion en tiempo real del paso 1: se dispara con cada pulsacion y
    /// actualiza el mensaje de error y el indicador que bloquea el boton NEXT.
    /// </summary>
    partial void OnRemoteNameChanged(string value)
    {
        var name = value?.Trim() ?? string.Empty;

        if (string.IsNullOrWhiteSpace(name))
        {
            ValidationMessage = string.Empty;
            HasNameError = false;
            return;
        }

        if (!RemoteNameRegex.IsMatch(name))
        {
            ValidationMessage = L("Str_RemoteNameInvalid",
                "Only letters, digits, hyphen and underscore are allowed.");
            HasNameError = true;
            return;
        }

        if (_configManager.RemoteExists(name))
        {
            ValidationMessage = L("Str_RemoteNameExists",
                "Error: A remote with this name already exists.");
            HasNameError = true;
            return;
        }

        ValidationMessage = string.Empty;
        HasNameError = false;
    }

    /// <summary>Atajo de localizacion con fallback.</summary>
    private string L(string key, string fallback) => _localization.Get(key, fallback);

    partial void OnSelectedProviderChanged(RemoteProvider? value)
    {
        NotifyEffectiveProviderChanged();
    }

    partial void OnSelectedAdvancedProviderChanged(RemoteProvider? value)
    {
        NotifyEffectiveProviderChanged();
    }

    /// <summary>
    /// Notifica a la vista todos los indicadores que dependen del proveedor
    /// efectivo (tarjeta directa o desplegable avanzado).
    /// </summary>
    private void NotifyEffectiveProviderChanged()
    {
        OnPropertyChanged(nameof(IsAdvancedProviderSelected));
        OnPropertyChanged(nameof(EffectiveProvider));
        OnPropertyChanged(nameof(ProviderRequiresOAuth));
        OnPropertyChanged(nameof(ProviderRequiresKeys));
        OnPropertyChanged(nameof(ProviderRequiresUserPassword));
        OnPropertyChanged(nameof(ProviderRequiresCrypt));
        OnPropertyChanged(nameof(ProviderRequiresNoCredentials));
        OnPropertyChanged(nameof(IsOAuthFlow));
        OnPropertyChanged(nameof(IsDirectFlow));
        OnPropertyChanged(nameof(AuthenticateButtonText));
        OnPropertyChanged(nameof(AuthenticateHintText));
    }
}

/// <summary>
/// Elemento de la lista de remotos mostrado como tarjeta en el editor.
/// </summary>
public sealed class RemoteCardItem
{
    public RemoteCardItem(RemoteEntry entry)
    {
        Name = entry.Name;
        Type = entry.Type;
        RootFolder = entry.RootFolder ?? string.Empty;
        RawKeys = new Dictionary<string, string>(entry.RawKeys, StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>Nombre del remoto.</summary>
    public string Name { get; }

    /// <summary>Tipo de backend rclone (drive, s3, ...).</summary>
    public string Type { get; }

    /// <summary>Carpeta raiz opcional.</summary>
    public string RootFolder { get; }

    /// <summary>
    /// Todos los pares clave/valor del bloque del remoto en rclone.conf.
    /// Es la fuente del editor avanzado: permite mostrar y modificar
    /// cualquier parametro del backend sin hardcodear campos.
    /// </summary>
    public Dictionary<string, string> RawKeys { get; }

    /// <summary>Glifo del proveedor para la tarjeta.</summary>
    public string Glyph => RemoteProvider.All
        .FirstOrDefault(p => string.Equals(p.Type, Type, StringComparison.OrdinalIgnoreCase))
        ?.Glyph ?? "\uE753";

    /// <summary>Nombre legible del proveedor.</summary>
    public string ProviderName => RemoteProvider.All
        .FirstOrDefault(p => string.Equals(p.Type, Type, StringComparison.OrdinalIgnoreCase))
        ?.DisplayName ?? Type;
}

/// <summary>
/// Par clave/valor editable del editor avanzado de un remoto.
///
/// Cada remoto de rclone.conf es esencialmente un diccionario; esta clase
/// representa una fila de ese diccionario para poder mostrarla y editarla
/// en la UI sin hardcodear los campos de cada uno de los 70+ backends.
///
/// Ademas de la clave y el valor, transporta la informacion del esquema del
/// backend (ayuda, valores validos, sensibilidad) para que la vista pueda
/// renderizar un ComboBox cuando corresponda y un tooltip informativo.
/// </summary>
public sealed partial class ConfigOptionItem : ObservableObject
{
    /// <summary>Nombre de la clave (client_id, chunk_size, endpoint...).</summary>
    [ObservableProperty]
    private string _key;

    /// <summary>Valor actual de la clave.</summary>
    [ObservableProperty]
    private string _value;

    /// <summary>
    /// True cuando la clave es estructural (type) y no debe poder renombrarse
    /// ni eliminarse desde el editor, para no romper el remoto.
    /// </summary>
    [ObservableProperty]
    private bool _isProtected;

    /// <summary>
    /// True cuando la clave es un secreto gestionado por rclone (token,
    /// client_secret...). Estas claves se ocultan del editor: jamas deben
    /// modificarse a mano porque rclone las cifra y las renueva via OAuth.
    /// </summary>
    [ObservableProperty]
    private bool _isSensitive;

    /// <summary>Texto de ayuda del esquema, mostrado como tooltip informativo.</summary>
    [ObservableProperty]
    private string _help = string.Empty;

    /// <summary>
    /// Valores validos declarados por el esquema. Si contiene elementos, la
    /// vista debe ofrecer un ComboBox en lugar de un TextBox libre.
    /// </summary>
    public ObservableCollection<ProviderOptionExample> Examples { get; } = new();

    /// <summary>True cuando el esquema declara una lista cerrada de valores validos.</summary>
    public bool HasExamples => Examples.Count > 0;

    /// <summary>True cuando hay texto de ayuda que mostrar en el tooltip.</summary>
    public bool HasHelp => !string.IsNullOrWhiteSpace(Help);

    /// <summary>
    /// True cuando la fila puede editarse libremente. Las claves sensibles y
    /// las protegidas quedan bloqueadas.
    /// </summary>
    public bool IsEditable => !IsProtected && !IsSensitive;

    /// <summary>True cuando la clave es de solo lectura (protegida o sensible).</summary>
    public bool IsReadOnly => IsProtected || IsSensitive;

    public ConfigOptionItem(string key, string value, bool isProtected = false)
    {
        _key = key;
        _value = value;
        _isProtected = isProtected;
    }

    /// <summary>
    /// Aplica la informacion del esquema del backend a esta fila: ayuda,
    /// valores validos y marca de sensibilidad.
    /// </summary>
    public void ApplySchema(ProviderOption? option)
    {
        if (option is null)
        {
            return;
        }

        Help = option.Help ?? string.Empty;

        // Una clave sensible (token, client_secret...) queda bloqueada por
        // completo: rclone la cifra y solo se renueva via OAuth, nunca a mano.
        IsSensitive = option.Sensitive || option.IsPassword;
        if (IsSensitive)
        {
            IsProtected = true;
        }

        Examples.Clear();
        foreach (var example in option.Examples)
        {
            Examples.Add(example);
        }

        OnPropertyChanged(nameof(HasExamples));
        OnPropertyChanged(nameof(HasHelp));
        OnPropertyChanged(nameof(IsEditable));
        OnPropertyChanged(nameof(IsReadOnly));
    }
}
