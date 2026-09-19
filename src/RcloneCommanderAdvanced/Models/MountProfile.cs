using System.Collections.Generic;
using RcloneCommanderAdvanced.Services.Abstractions;

namespace RcloneCommanderAdvanced.Models;

/// <summary>
/// Define un perfil de montaje: nombre legible, descripcion y los argumentos
/// que se inyectaran dinamicamente en la linea de comandos de rclone.
///
/// Los textos visibles NO estan hardcodeados: se resuelven en runtime desde
/// el <see cref="ILocalizationService"/> usando claves <c>Str_Profile*</c>.
/// Asi el mismo perfil se muestra en el idioma activo sin duplicar logica.
/// </summary>
public sealed class MountProfile
{
    /// <summary>Identificador del perfil.</summary>
    public MountProfileType Type { get; init; }

    /// <summary>Clave i18n del nombre visible en la UI.</summary>
    public string DisplayNameKey { get; init; } = string.Empty;

    /// <summary>Clave i18n de la descripcion corta (tooltip).</summary>
    public string DescriptionKey { get; init; } = string.Empty;

    /// <summary>Clave i18n de las ventajas del perfil.</summary>
    public string ProsKey { get; init; } = string.Empty;

    /// <summary>Clave i18n de los inconvenientes del perfil.</summary>
    public string ConsKey { get; init; } = string.Empty;

    /// <summary>Argumentos de rclone asociados al perfil.</summary>
    public IReadOnlyList<string> Arguments { get; init; } = new List<string>();

    /// <summary>
    /// Indica si el perfil monta la unidad en modo solo lectura.
    /// Se usa para pintar un badge de seguridad en la tarjeta.
    /// </summary>
    public bool IsReadOnly { get; init; }

    /// <summary>
    /// Indica si el perfil utiliza cache en disco (VFS cache distinto de "off").
    /// Se usa para advertir sobre consumo de SSD.
    /// </summary>
    public bool UsesDiskCache { get; init; }

    /// <summary>
    /// Catalogo estatico con los tres perfiles oficiales solicitados.
    /// Solo contiene datos tecnicos (argumentos/flags); los textos se
    /// resuelven por clave i18n.
    /// </summary>
    public static IReadOnlyList<MountProfile> Defaults { get; } = new List<MountProfile>
    {
        new MountProfile
        {
            Type = MountProfileType.SafeRead,
            DisplayNameKey = "Str_ProfileSafeReadName",
            DescriptionKey = "Str_ProfileSafeReadDescription",
            ProsKey = "Str_ProfileSafeReadPros",
            ConsKey = "Str_ProfileSafeReadCons",
            IsReadOnly = true,
            UsesDiskCache = false,
            Arguments = new List<string>
            {
                "--read-only",
                "--vfs-cache-mode", "off",
                "--dir-cache-time", "72h",
                // BUGFIX VFS: --fast-list NO es valido en "rclone mount"
                // (rclone emite "NOTICE: --fast-list does nothing on a mount").
                // Se elimino para no ensuciar el log ni confundir al usuario.
                // BUGFIX SYMLINKS: --links es OBLIGATORIO en Windows/WinFsp
                // cuando el remoto (Google Drive) contiene accesos directos.
                // Sin el, rclone aborta con:
                //   "ERROR : symlinks not supported without the --links flag"
                "--links"
            }
        },
        new MountProfile
        {
            Type = MountProfileType.HighPerformance,
            DisplayNameKey = "Str_ProfileHighPerformanceName",
            DescriptionKey = "Str_ProfileHighPerformanceDescription",
            ProsKey = "Str_ProfileHighPerformancePros",
            ConsKey = "Str_ProfileHighPerformanceCons",
            IsReadOnly = false,
            UsesDiskCache = true,
            // FASE 3: el limite de cache (--vfs-cache-max-size) NO se fija aqui.
            // Se inyecta dinamicamente desde AppSettings.VfsCacheMaxSize (50G por
            // defecto) para que el usuario pueda ajustarlo sin recompilar y para
            // garantizar que rclone siempre se autolimpie antes de llenar el SSD.
            Arguments = new List<string>
            {
                "--vfs-cache-mode", "full",
                "--vfs-read-chunk-size", "64M",
                "--vfs-read-chunk-size-limit", "off",
                "--vfs-write-back", "5s",
                // BUGFIX SYMLINKS: --links es obligatorio para que rclone
                // soporte los accesos directos de Google Drive en Windows.
                "--links"
            }
        },
        new MountProfile
        {
            Type = MountProfileType.BulkTransfer,
            DisplayNameKey = "Str_ProfileBulkTransferName",
            DescriptionKey = "Str_ProfileBulkTransferDescription",
            ProsKey = "Str_ProfileBulkTransferPros",
            ConsKey = "Str_ProfileBulkTransferCons",
            IsReadOnly = false,
            UsesDiskCache = false,
            Arguments = new List<string>
            {
                "--vfs-cache-mode", "writes",
                "--buffer-size", "128M",
                "--dir-cache-time", "1h",
                // BUGFIX SYMLINKS: --links es obligatorio para que rclone
                // soporte los accesos directos de Google Drive en Windows.
                "--links"
            }
        }
    };

    /// <summary>
    /// Devuelve el perfil oficial asociado a un tipo, o el perfil de
    /// Lectura Segura como valor por defecto seguro.
    /// </summary>
    public static MountProfile FromType(MountProfileType type)
    {
        foreach (var profile in Defaults)
        {
            if (profile.Type == type)
            {
                return profile;
            }
        }

        return Defaults[0];
    }

    /// <summary>Nombre visible resuelto en el idioma activo.</summary>
    public string GetDisplayName(ILocalizationService localization) =>
        localization.Get(DisplayNameKey, DisplayNameKey);

    /// <summary>Descripcion resuelta en el idioma activo.</summary>
    public string GetDescription(ILocalizationService localization) =>
        localization.Get(DescriptionKey, DescriptionKey);

    /// <summary>Ventajas resueltas en el idioma activo.</summary>
    public string GetPros(ILocalizationService localization) =>
        localization.Get(ProsKey, ProsKey);

    /// <summary>Inconvenientes resueltos en el idioma activo.</summary>
    public string GetCons(ILocalizationService localization) =>
        localization.Get(ConsKey, ConsKey);

    /// <summary>
    /// Texto combinado de pros y contras listo para mostrar en la UI,
    /// resuelto en el idioma activo.
    /// </summary>
    public string GetProsConsText(ILocalizationService localization)
    {
        var pros = localization.Get("Str_Pros", "Pros");
        var cons = localization.Get("Str_Cons", "Cons");

        return $"\u2714 {pros}: {GetPros(localization)}\n" +
               $"\u2716 {cons}: {GetCons(localization)}";
    }
}
