using System;
using System.Collections.Generic;
using System.Linq;

namespace RcloneCommanderAdvanced.Models;

/// <summary>
/// Esquema de un backend (proveedor) de rclone, tal como lo devuelve
/// <c>rclone config providers</c> en formato JSON.
///
/// Se usa para enriquecer el editor avanzado: mostrar la ayuda de cada
/// parametro, ofrecer listas desplegables cuando el esquema define valores
/// validos y detectar que claves son sensibles (por ejemplo <c>token</c>).
/// </summary>
public sealed class ProviderSchema
{
    /// <summary>Prefijo del backend, por ejemplo <c>drive</c> o <c>s3</c>.</summary>
    public string Prefix { get; init; } = string.Empty;

    /// <summary>Nombre tecnico del backend (normalmente igual al prefijo).</summary>
    public string Name { get; init; } = string.Empty;

    /// <summary>Descripcion legible del backend.</summary>
    public string Description { get; init; } = string.Empty;

    /// <summary>Opciones del backend indexadas por nombre (sin distinguir mayusculas).</summary>
    public IReadOnlyDictionary<string, ProviderOption> Options { get; init; }
        = new Dictionary<string, ProviderOption>(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Devuelve la opcion cuyo nombre coincide, o <c>null</c> si el esquema no
    /// la declara (por ejemplo claves heredadas o personalizadas).
    /// </summary>
    public ProviderOption? FindOption(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return null;
        }

        return Options.TryGetValue(name.Trim(), out var option) ? option : null;
    }
}

/// <summary>
/// Una opcion concreta dentro del esquema de un backend.
/// </summary>
public sealed class ProviderOption
{
    /// <summary>Nombre de la clave tal como aparece en <c>rclone.conf</c>.</summary>
    public string Name { get; init; } = string.Empty;

    /// <summary>Texto de ayuda largo que describe el parametro.</summary>
    public string Help { get; init; } = string.Empty;

    /// <summary>Tipo declarado por rclone: <c>string</c>, <c>int</c>, <c>bool</c>, <c>SizeSuffix</c>, <c>Duration</c>, etc.</summary>
    public string Type { get; init; } = string.Empty;

    /// <summary>Indica si rclone considera la clave un secreto (token, client_secret...).</summary>
    public bool Sensitive { get; init; }

    /// <summary>Indica si rclone almacena el valor cifrado como contrasena.</summary>
    public bool IsPassword { get; init; }

    /// <summary>Indica si el parametro es obligatorio para que el remoto funcione.</summary>
    public bool Required { get; init; }

    /// <summary>Indica si rclone lo clasifica como opcion avanzada.</summary>
    public bool Advanced { get; init; }

    /// <summary>Valor por defecto declarado por rclone.</summary>
    public string DefaultValue { get; init; } = string.Empty;

    /// <summary>Valores validos declarados por el esquema (puede estar vacio).</summary>
    public IReadOnlyList<ProviderOptionExample> Examples { get; init; }
        = Array.Empty<ProviderOptionExample>();

    /// <summary>Indica si el esquema ofrece una lista cerrada de valores validos.</summary>
    public bool HasExamples => Examples.Count > 0;

    /// <summary>Indica si el esquema aporta texto de ayuda utilizable como tooltip.</summary>
    public bool HasHelp => !string.IsNullOrWhiteSpace(Help);

    /// <summary>
    /// Indica si la opcion debe presentarse como lista desplegable en lugar de
    /// un cuadro de texto libre.
    /// </summary>
    public bool IsChoice => HasExamples;

    /// <summary>Valores validos como cadenas simples, utiles para enlazar a un ComboBox.</summary>
    public IReadOnlyList<string> ExampleValues
        => Examples.Select(e => e.Value).ToList();
}

/// <summary>
/// Un valor valido concreto declarado por el esquema de un backend.
/// </summary>
public sealed class ProviderOptionExample
{
    /// <summary>Valor aceptado por rclone.</summary>
    public string Value { get; init; } = string.Empty;

    /// <summary>Descripcion del valor, util como tooltip dentro de la lista.</summary>
    public string Help { get; init; } = string.Empty;

    /// <summary>Texto legible para mostrar en la interfaz.</summary>
    public string Display => string.IsNullOrWhiteSpace(Help)
        ? Value
        : $"{Value} - {Help.Split('\n')[0].Trim()}";

    /// <inheritdoc />
    public override string ToString() => Value;
}
