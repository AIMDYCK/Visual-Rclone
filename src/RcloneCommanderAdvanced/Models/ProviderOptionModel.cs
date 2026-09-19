using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;

namespace RcloneCommanderAdvanced.Models;

/// <summary>
/// Opcion editable del asistente de creacion de remotos.
///
/// A diferencia de <see cref="ProviderOption"/> (que es un DTO inmutable con el
/// esquema crudo de rclone), esta clase es BINDABLE: expone <see cref="Value"/>
/// con notificacion de cambios para que el usuario escriba en el formulario
/// dinamico del Step 3 y el ViewModel pueda leer el resultado.
///
/// El tipo de control a renderizar se deduce de las propiedades calculadas
/// (<see cref="IsChoice"/>, <see cref="IsBoolean"/>, <see cref="IsSecret"/>),
/// de modo que la vista no necesita conocer los detalles de cada backend.
/// </summary>
public sealed partial class ProviderOptionModel : ObservableObject
{
    /// <summary>Nombre de la clave tal como la espera rclone (client_id, user, pass...).</summary>
    public string Name { get; }

    /// <summary>Texto de ayuda del esquema, usado como tooltip informativo.</summary>
    public string Help { get; }

    /// <summary>Tipo declarado por rclone: string, int, bool, SizeSuffix, Duration...</summary>
    public string Type { get; }

    /// <summary>Indica si rclone considera la opcion obligatoria para que el remoto funcione.</summary>
    public bool IsRequired { get; }

    /// <summary>Indica si rclone clasifica la opcion como avanzada.</summary>
    public bool IsAdvanced { get; }

    /// <summary>Valor por defecto declarado por el esquema (puede estar vacio).</summary>
    public string DefaultValue { get; }

    /// <summary>Valores validos declarados por el esquema (para renderizar un ComboBox).</summary>
    public ObservableCollection<ProviderOptionExample> Examples { get; } = new();

    /// <summary>
    /// Valor actual introducido por el usuario. Es la unica propiedad mutable y
    /// la que el ViewModel recolecta al crear el remoto.
    /// </summary>
    [ObservableProperty]
    private string _value = string.Empty;

    public ProviderOptionModel(ProviderOption option)
    {
        Name = option.Name;
        Help = option.Help ?? string.Empty;
        Type = option.Type ?? string.Empty;
        IsRequired = option.Required;
        IsAdvanced = option.Advanced;
        DefaultValue = option.DefaultValue ?? string.Empty;

        foreach (var example in option.Examples)
        {
            Examples.Add(example);
        }

        // Arrancar con el valor por defecto del esquema, si lo declara.
        _value = DefaultValue;
    }

    /// <summary>True cuando el esquema ofrece una lista cerrada de valores validos.</summary>
    public bool IsChoice => Examples.Count > 0;

    /// <summary>True cuando el valor es booleano (se renderiza como CheckBox).</summary>
    public bool IsBoolean => string.Equals(Type, "bool", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// True cuando el valor es un secreto (contrasena, token, client_secret...).
    /// Se renderiza con un control que ofusca el texto.
    /// </summary>
    public bool IsSecret =>
        string.Equals(Type, "password", StringComparison.OrdinalIgnoreCase) ||
        Name.Contains("pass", StringComparison.OrdinalIgnoreCase) ||
        Name.Contains("secret", StringComparison.OrdinalIgnoreCase) ||
        Name.Contains("token", StringComparison.OrdinalIgnoreCase);

    /// <summary>True cuando hay texto de ayuda que mostrar en el tooltip.</summary>
    public bool HasHelp => !string.IsNullOrWhiteSpace(Help);

    /// <summary>
    /// True cuando el campo debe mostrarse como TextBox simple: ni lista de
    /// valores, ni booleano, ni secreto.
    /// </summary>
    public bool IsPlainText => !IsChoice && !IsBoolean && !IsSecret;

    /// <summary>
    /// True cuando el valor booleano actual esta activo. Se usa para enlazar el
    /// CheckBox sin exponer la conversion string/bool en la vista.
    /// </summary>
    public bool BoolValue
    {
        get => string.Equals(Value, "true", StringComparison.OrdinalIgnoreCase);
        set => Value = value ? "true" : "false";
    }

    partial void OnValueChanged(string value)
    {
        // El CheckBox enlaza a BoolValue; hay que notificar cuando cambia el
        // valor subyacente para que la casilla refleje el estado real.
        OnPropertyChanged(nameof(BoolValue));
    }

    /// <summary>
    /// True cuando la opcion tiene un valor listo para enviar a rclone. Las
    /// opciones obligatorias sin valor bloquean el avance del asistente.
    /// </summary>
    public bool HasValue => !string.IsNullOrWhiteSpace(Value);

    /// <summary>
    /// Crea la lista de opciones editables a partir del esquema de un backend,
    /// excluyendo las claves que el asistente gestiona por su cuenta
    /// (<c>type</c>) y las puramente informativas.
    /// </summary>
    public static IReadOnlyList<ProviderOptionModel> FromSchema(ProviderSchema? schema)
    {
        if (schema is null)
        {
            return Array.Empty<ProviderOptionModel>();
        }

        return schema.Options.Values
            .Where(o => !string.Equals(o.Name, "type", StringComparison.OrdinalIgnoreCase))
            .OrderBy(o => o.Advanced)
            .ThenBy(o => o.Name, StringComparer.OrdinalIgnoreCase)
            .Select(o => new ProviderOptionModel(o))
            .ToList();
    }
}
