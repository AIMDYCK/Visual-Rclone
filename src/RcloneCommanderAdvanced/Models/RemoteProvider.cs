using System.Collections.Generic;

namespace RcloneCommanderAdvanced.Models;

/// <summary>
/// Describe un proveedor de almacenamiento soportado por el asistente de
/// creacion de remotos. El <see cref="Type"/> es el identificador de backend
/// que espera rclone (primer argumento de <c>rclone config create</c>).
/// </summary>
public sealed class RemoteProvider
{
    /// <summary>
    /// Identificador especial usado por la tarjeta "Otros / Avanzado". No es un
    /// backend real de rclone: al seleccionarla se muestra un desplegable con
    /// el resto de backends disponibles.
    /// </summary>
    public const string AdvancedSentinelType = "__advanced__";

    /// <summary>Identificador de backend rclone (ej: "drive", "onedrive", "s3").</summary>
    public string Type { get; init; } = string.Empty;

    /// <summary>Nombre comercial mostrado en la UI (ej: "Google Drive").</summary>
    public string DisplayName { get; init; } = string.Empty;

    /// <summary>Glifo Segoe MDL2 Assets usado como icono del proveedor.</summary>
    public string Glyph { get; init; } = string.Empty;

    /// <summary>
    /// Indica si el proveedor requiere credenciales OAuth (Client ID/Secret)
    /// y un paso de autenticacion en el navegador.
    /// </summary>
    public bool RequiresOAuth { get; init; }

    /// <summary>
    /// Indica si el proveedor necesita credenciales de tipo clave/secret
    /// (por ejemplo S3) en lugar de OAuth.
    /// </summary>
    public bool RequiresKeys { get; init; }

    /// <summary>
    /// Indica si el proveedor se autentica con usuario y contrasena
    /// (FTP, SFTP, WebDAV, HTTP...). En ese caso no se piden Client ID/Secret.
    /// </summary>
    public bool RequiresUserPassword { get; init; }

    /// <summary>
    /// Indica si el proveedor no requiere ninguna credencial interactiva
    /// (por ejemplo un remoto local o un endpoint HTTP publico).
    /// </summary>
    public bool RequiresNoCredentials { get; init; }

    /// <summary>
    /// Indica si el proveedor es la capa de cifrado (crypt). En ese caso el
    /// asistente pide el remoto subyacente, la ruta, la contrasena y el modo
    /// de ofuscacion de nombres de archivo en lugar de credenciales normales.
    /// </summary>
    public bool RequiresCrypt { get; init; }

    /// <summary>True cuando esta tarjeta representa la opcion "Otros / Avanzado".</summary>
    public bool IsAdvanced => string.Equals(Type, AdvancedSentinelType, System.StringComparison.Ordinal);

    /// <summary>
    /// Catalogo de proveedores ofrecidos como tarjetas directas por el asistente.
    /// </summary>
    public static IReadOnlyList<RemoteProvider> All { get; } = new List<RemoteProvider>
    {
        new()
        {
            Type = "drive",
            DisplayName = "Google Drive",
            Glyph = "\uE8F1", // Cloud
            RequiresOAuth = true
        },
        new()
        {
            Type = "onedrive",
            DisplayName = "OneDrive",
            Glyph = "\uE753", // Cloud (alternate)
            RequiresOAuth = true
        },
        new()
        {
            Type = "dropbox",
            DisplayName = "Dropbox",
            Glyph = "\uE7C3", // Folder
            RequiresOAuth = true
        },
        new()
        {
            Type = "mega",
            DisplayName = "Mega",
            Glyph = "\uE8F1", // Cloud
            RequiresUserPassword = true
        },
        new()
        {
            Type = "box",
            DisplayName = "Box",
            Glyph = "\uE7C3", // Folder
            RequiresOAuth = true
        },
        new()
        {
            Type = "ftp",
            DisplayName = "FTP",
            Glyph = "\uE968", // Server
            RequiresUserPassword = true
        },
        new()
        {
            Type = "sftp",
            DisplayName = "SFTP",
            Glyph = "\uE72E", // Lock
            RequiresUserPassword = true
        },
        new()
        {
            Type = "webdav",
            DisplayName = "WebDAV",
            Glyph = "\uE774", // Globe
            RequiresUserPassword = true
        },
        new()
        {
            Type = "local",
            DisplayName = "Local / Network",
            Glyph = "\uE8B7", // Hard drive
            RequiresNoCredentials = true
        },
        new()
        {
            Type = "s3",
            DisplayName = "Amazon S3 / Compatible",
            Glyph = "\uE753", // Cloud
            RequiresKeys = true
        },
        new()
        {
            Type = "crypt",
            DisplayName = "Encrypted (Crypt)",
            Glyph = "\uE72E", // Lock
            RequiresCrypt = true
        },
        new()
        {
            Type = AdvancedSentinelType,
            DisplayName = "Other / Advanced",
            Glyph = "\uE712", // More (...)
            RequiresNoCredentials = true
        }
    };

    /// <summary>
    /// Backends adicionales ofrecidos por el desplegable "Otros / Avanzado".
    /// No pretende ser exhaustivo (rclone soporta mas de 70), solo cubre los
    /// mas habituales para no abrumar al usuario.
    /// </summary>
    public static IReadOnlyList<RemoteProvider> Advanced { get; } = new List<RemoteProvider>
    {
        new() { Type = "pcloud", DisplayName = "pCloud", Glyph = "\uE753", RequiresOAuth = true },
        new() { Type = "b2", DisplayName = "Backblaze B2", Glyph = "\uE753", RequiresKeys = true },
        new() { Type = "azureblob", DisplayName = "Azure Blob Storage", Glyph = "\uE753", RequiresKeys = true },
        new() { Type = "swift", DisplayName = "OpenStack Swift", Glyph = "\uE753", RequiresUserPassword = true },
        new() { Type = "yandex", DisplayName = "Yandex Disk", Glyph = "\uE753", RequiresOAuth = true },
        new() { Type = "koofr", DisplayName = "Koofr", Glyph = "\uE753", RequiresUserPassword = true },
        new() { Type = "http", DisplayName = "HTTP (read-only)", Glyph = "\uE774", RequiresNoCredentials = true },
        new() { Type = "jottacloud", DisplayName = "Jottacloud", Glyph = "\uE753", RequiresOAuth = true },
        new() { Type = "googlecloudstorage", DisplayName = "Google Cloud Storage", Glyph = "\uE753", RequiresKeys = true },
        new() { Type = "sharepoint", DisplayName = "SharePoint", Glyph = "\uE753", RequiresOAuth = true },
        new() { Type = "sugarsync", DisplayName = "SugarSync", Glyph = "\uE753", RequiresUserPassword = true },
        new() { Type = "opendrive", DisplayName = "OpenDrive", Glyph = "\uE753", RequiresUserPassword = true },
        new() { Type = "hdfs", DisplayName = "HDFS", Glyph = "\uE968", RequiresUserPassword = true },
        new() { Type = "smb", DisplayName = "SMB / CIFS", Glyph = "\uE968", RequiresUserPassword = true },
        new() { Type = "uptobox", DisplayName = "Uptobox", Glyph = "\uE753", RequiresUserPassword = true },
        new() { Type = "premiumizeme", DisplayName = "premiumize.me", Glyph = "\uE753", RequiresOAuth = true },
        new() { Type = "putio", DisplayName = "put.io", Glyph = "\uE753", RequiresOAuth = true },
        new() { Type = "seafile", DisplayName = "Seafile", Glyph = "\uE753", RequiresUserPassword = true },
        new() { Type = "storj", DisplayName = "Storj", Glyph = "\uE753", RequiresKeys = true },
        new() { Type = "union", DisplayName = "Union (local)", Glyph = "\uE8B7", RequiresNoCredentials = true }
    };

    /// <summary>
    /// Busca un proveedor por su tipo en los catalogos directo y avanzado.
    /// Devuelve <c>null</c> si no se encuentra.
    /// </summary>
    public static RemoteProvider? FindByType(string? type)
    {
        if (string.IsNullOrWhiteSpace(type))
        {
            return null;
        }

        foreach (var provider in All)
        {
            if (string.Equals(provider.Type, type, System.StringComparison.OrdinalIgnoreCase))
            {
                return provider;
            }
        }

        foreach (var provider in Advanced)
        {
            if (string.Equals(provider.Type, type, System.StringComparison.OrdinalIgnoreCase))
            {
                return provider;
            }
        }

        return null;
    }
}
