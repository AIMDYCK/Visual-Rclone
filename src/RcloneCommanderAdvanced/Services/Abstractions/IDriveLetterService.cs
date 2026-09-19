using System.Collections.Generic;

namespace RcloneCommanderAdvanced.Services.Abstractions;

/// <summary>
/// Contrato del servicio que enumera y valida letras de unidad en Windows.
/// </summary>
public interface IDriveLetterService
{
    /// <summary>Letras de unidad actualmente en uso por el sistema (ej: "C", "D").</summary>
    IReadOnlyList<string> GetUsedDriveLetters();

    /// <summary>
    /// Letras disponibles para montar (de la D a la Z, excluyendo las usadas
    /// y las reservadas en la configuracion).
    /// </summary>
    IReadOnlyList<string> GetAvailableDriveLetters();

    /// <summary>Indica si una letra concreta esta libre.</summary>
    bool IsDriveLetterAvailable(string letter);

    /// <summary>Indica si la unidad existe y responde en el sistema.</summary>
    bool IsDriveReady(string letter);

    /// <summary>Devuelve la etiqueta de volumen de una unidad (o cadena vacia).</summary>
    string GetVolumeLabel(string letter);
}
