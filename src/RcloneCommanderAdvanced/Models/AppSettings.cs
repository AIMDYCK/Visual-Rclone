using System;
using System.Collections.Generic;

namespace RcloneCommanderAdvanced.Models;

/// <summary>
/// Modelo raiz persistido en appsettings.json (carpeta de datos del usuario).
/// Contiene TODA la configuracion dinamica: nada esta hardcodeado en el codigo.
/// </summary>
public sealed class AppSettings
{
    /// <summary>Version del esquema para futuras migraciones.</summary>
    public int SchemaVersion { get; set; } = 1;

    /// <summary>Ruta al ejecutable de rclone. Vacio = autodeteccion (PATH / rutas comunes).</summary>
    public string RcloneExecutablePath { get; set; } = string.Empty;

    /// <summary>Ruta al archivo rclone.conf. Vacio = %APPDATA%\rclone\rclone.conf.</summary>
    public string RcloneConfigPath { get; set; } = string.Empty;

    /// <summary>Puerto base para el servidor RC. 0 = asignacion dinamica automatica.</summary>
    public int RcPortBase { get; set; }

    /// <summary>Puerto final del rango dinamico. 0 = sin limite (se calcula automaticamente).</summary>
    public int RcPortRangeEnd { get; set; }

    /// <summary>Usuario del servidor RC (opcional).</summary>
    public string RcUser { get; set; } = string.Empty;

    /// <summary>Password del servidor RC (opcional, se guarda ofuscado).</summary>
    public string RcPassword { get; set; } = string.Empty;

    /// <summary>Hash del PIN maestro. Nunca se guarda el PIN en claro.</summary>
    public string MasterPinHash { get; set; } = string.Empty;

    /// <summary>Salt criptografico usado para derivar el hash del PIN.</summary>
    public string MasterPinSalt { get; set; } = string.Empty;

    /// <summary>Indica si el primer arranque ya fue completado.</summary>
    public bool IsFirstRunCompleted { get; set; }

    /// <summary>
    /// Frase de recordatorio opcional definida por el usuario al crear el PIN.
    /// Se muestra en la pantalla de bloqueo para ayudarle a recordarlo.
    /// </summary>
    public string PinReminderPhrase { get; set; } = string.Empty;

    /// <summary>
    /// Fecha (UTC) hasta la que este equipo se considera "de confianza" y no
    /// se exige el PIN. Null = no hay confianza activa.
    /// </summary>
    public DateTime? TrustedDeviceUntil { get; set; }

    /// <summary>
    /// True cuando la confianza es permanente (el usuario eligio "siempre").
    /// Tiene prioridad sobre <see cref="TrustedDeviceUntil"/>.
    /// </summary>
    public bool TrustedDeviceForever { get; set; }

    /// <summary>
    /// Huella del equipo de confianza. Si el hardware/usuario cambia, la
    /// confianza se invalida automaticamente.
    /// </summary>
    public string TrustedDeviceFingerprint { get; set; } = string.Empty;

    /// <summary>Montar automaticamente los remotos habilitados al iniciar la app.</summary>
    public bool AutoMountOnStartup { get; set; }

    /// <summary>Minimizar a la bandeja del sistema al cerrar la ventana.</summary>
    public bool MinimizeToTrayOnClose { get; set; }

    /// <summary>
    /// FASE 2: iniciar la aplicacion automaticamente con Windows, arrancando
    /// minimizada en la bandeja del sistema (acceso directo en la carpeta
    /// Startup con el argumento --minimized).
    /// </summary>
    public bool StartWithWindows { get; set; }

    /// <summary>
    /// FASE 3: tamano maximo de la cache VFS en disco para los perfiles que
    /// usan cache (p. ej. Alto Rendimiento). Se inyecta como
    /// <c>--vfs-cache-max-size</c> en la linea de comandos de rclone.
    ///
    /// Valor por defecto "50G": rclone se autolimpia al alcanzar el limite y
    /// se elimina el riesgo de llenar el disco C: hasta bloquear el equipo.
    /// Acepta sufijos de rclone (K, M, G, T). Vacio o invalido = se usa 50G.
    /// </summary>
    public string VfsCacheMaxSize { get; set; } = "50G";

    /// <summary>Intervalo en segundos del monitor de salud de montajes.</summary>
    public int HealthCheckIntervalSeconds { get; set; } = 15;

    /// <summary>
    /// Codigo de cultura de la interfaz (ej: "en-US", "es-ES", "fr-FR", "de-DE").
    /// Vacio = idioma por defecto (en-US).
    /// </summary>
    public string Language { get; set; } = string.Empty;

    /// <summary>Lista de remotos configurados por el usuario (letras, perfiles, flags).</summary>
    public List<RemoteEntry> Remotes { get; set; } = new();

    /// <summary>Letras de unidad reservadas que la app nunca ofrecera.</summary>
    public List<string> ReservedDriveLetters { get; set; } = new() { "A", "B", "C" };
}
