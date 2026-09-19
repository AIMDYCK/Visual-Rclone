using System.Collections.Generic;
using RcloneCommanderAdvanced.Models;

namespace RcloneCommanderAdvanced.Services.Abstractions;

/// <summary>
/// Contrato del servicio que lee y parsea el archivo rclone.conf del usuario.
/// </summary>
public interface IRcloneConfigParser
{
    /// <summary>
    /// Ruta efectiva del rclone.conf que se esta utilizando
    /// (resuelta desde la configuracion o por autodeteccion).
    /// </summary>
    string ResolvedConfigPath { get; }

    /// <summary>
    /// Parsea el archivo rclone.conf y devuelve la lista de remotos detectados.
    /// No lanza excepcion si el archivo no existe: devuelve una lista vacia.
    /// </summary>
    IReadOnlyList<RemoteEntry> Parse();

    /// <summary>
    /// Parsea un contenido INI en memoria. Expuesto para pruebas unitarias.
    /// </summary>
    IReadOnlyList<RemoteEntry> ParseContent(string iniContent);

    /// <summary>
    /// Indica si el archivo rclone.conf existe y es legible.
    /// </summary>
    bool ConfigFileExists();
}
