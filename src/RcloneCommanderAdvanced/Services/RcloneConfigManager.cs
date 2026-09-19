using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using RcloneCommanderAdvanced.Models;
using RcloneCommanderAdvanced.Services.Abstractions;

namespace RcloneCommanderAdvanced.Services;

/// <summary>
/// Implementacion de <see cref="IRcloneConfigManager"/> que delega en el binario
/// oficial de rclone. Nunca escribe el INI a mano: usa
/// <c>rclone config create</c> y <c>rclone config delete</c>, que respetan el
/// cifrado de secretos y las particularidades de cada proveedor.
/// </summary>
public sealed class RcloneConfigManager : IRcloneConfigManager
{
    private readonly IRcloneLocator _locator;
    private readonly IRcloneConfigParser _parser;

    /// <summary>
    /// Cache en memoria de los esquemas de backend. La salida de
    /// <c>rclone config providers</c> supera los 900 KB, por lo que se consulta
    /// una sola vez por tipo de backend durante la vida del proceso.
    /// </summary>
    private readonly ConcurrentDictionary<string, ProviderSchema?> _schemaCache =
        new(StringComparer.OrdinalIgnoreCase);

    public RcloneConfigManager(IRcloneLocator locator, IRcloneConfigParser parser)
    {
        _locator = locator;
        _parser = parser;
    }

    /// <inheritdoc />
    public bool RemoteExists(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return false;
        }

        return _parser.Parse()
            .Any(r => string.Equals(r.Name, name, StringComparison.OrdinalIgnoreCase));
    }

    /// <inheritdoc />
    public async Task<RcloneConfigOperationResult> CreateRemoteAsync(
        string name,
        string type,
        IReadOnlyDictionary<string, string>? parameters = null,
        Action<string>? onOutput = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return RcloneConfigOperationResult.Fail("Remote name is required.");
        }

        if (string.IsNullOrWhiteSpace(type))
        {
            return RcloneConfigOperationResult.Fail("Provider type is required.");
        }

        var arguments = new List<string>
        {
            "config", "create", name, type
        };

        if (parameters is not null)
        {
            foreach (var kvp in parameters)
            {
                if (string.IsNullOrWhiteSpace(kvp.Key) || string.IsNullOrWhiteSpace(kvp.Value))
                {
                    continue;
                }

                // rclone espera pares clave=valor como argumentos posicionales.
                //
                // El valor se envia como argumento INDEPENDIENTE dentro de
                // ArgumentList: .NET se encarga del escapado de comillas y
                // caracteres especiales al construir la linea de comandos, por
                // lo que NO hay que envolverlo manualmente entre comillas (eso
                // enviaria las comillas como parte del secreto y romperia OAuth).
                arguments.Add($"{kvp.Key}={kvp.Value}");
            }
        }

        // Traza de diagnostico: se registran las CLAVES enviadas y si cada una
        // lleva valor, pero NUNCA los valores (podrian ser secretos). Esto
        // permite verificar en el log que client_id/client_secret viajan de
        // verdad sin filtrar credenciales a la UI.
        if (onOutput is not null && parameters is not null)
        {
            foreach (var kvp in parameters)
            {
                var hasValue = !string.IsNullOrWhiteSpace(kvp.Value);
                onOutput($"[config] {kvp.Key} = {(hasValue ? "<set>" : "<empty>")}");
            }
        }

        arguments.Add("--config");
        arguments.Add(_parser.ResolvedConfigPath);

        return await RunRcloneAsync(arguments, onOutput, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<RcloneConfigOperationResult> RenameRemoteAsync(
        string oldName,
        string newName,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(oldName) || string.IsNullOrWhiteSpace(newName))
        {
            return RcloneConfigOperationResult.Fail("Both the current and the new name are required.");
        }

        if (!RemoteExists(oldName))
        {
            return RcloneConfigOperationResult.Fail(
                $"Remote \"{oldName}\" does not exist.");
        }

        if (string.Equals(oldName, newName, StringComparison.OrdinalIgnoreCase))
        {
            // Nada que hacer: el nombre no cambio.
            return RcloneConfigOperationResult.Ok("Name unchanged.");
        }

        if (RemoteExists(newName))
        {
            return RcloneConfigOperationResult.Fail(
                $"A remote named \"{newName}\" already exists.");
        }

        var arguments = new List<string>
        {
            "config", "rename", oldName, newName,
            "--config", _parser.ResolvedConfigPath
        };

        return await RunRcloneAsync(arguments, null, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<RcloneConfigOperationResult> UpdateRemoteAsync(
        string name,
        IReadOnlyDictionary<string, string>? parameters = null,
        Action<string>? onOutput = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return RcloneConfigOperationResult.Fail("Remote name is required.");
        }

        if (!RemoteExists(name))
        {
            return RcloneConfigOperationResult.Fail(
                $"Remote \"{name}\" does not exist.");
        }

        var arguments = new List<string>
        {
            "config", "update", name
        };

        if (parameters is not null)
        {
            foreach (var kvp in parameters)
            {
                if (string.IsNullOrWhiteSpace(kvp.Key))
                {
                    continue;
                }

                // rclone espera pares clave=valor como argumentos posicionales.
                // A diferencia de create, aqui SI permitimos valor vacio para
                // poder limpiar un parametro existente.
                arguments.Add($"{kvp.Key}={kvp.Value}");
            }
        }

        arguments.Add("--config");
        arguments.Add(_parser.ResolvedConfigPath);

        return await RunRcloneAsync(arguments, onOutput, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<RcloneConfigOperationResult> DeleteRemoteAsync(
        string name,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return RcloneConfigOperationResult.Fail("Remote name is required.");
        }

        var arguments = new List<string>
        {
            "config", "delete", name,
            "--config", _parser.ResolvedConfigPath
        };

        return await RunRcloneAsync(arguments, null, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<RcloneConfigOperationResult> ReconnectRemoteAsync(
        string name,
        Action<string>? onOutput = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return RcloneConfigOperationResult.Fail("Remote name is required.");
        }

        if (!RemoteExists(name))
        {
            return RcloneConfigOperationResult.Fail($"The remote '{name}' does not exist.");
        }

        // rclone exige la sintaxis "remote:" (con dos puntos) para reconnect.
        var arguments = new List<string>
        {
            "config", "reconnect", $"{name}:",
            "--config", _parser.ResolvedConfigPath
        };

        return await RunRcloneAsync(arguments, onOutput, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<RcloneConfigOperationResult> TestRemoteAsync(
        string name,
        Action<string>? onOutput = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return RcloneConfigOperationResult.Fail("Remote name is required.");
        }

        if (!RemoteExists(name))
        {
            return RcloneConfigOperationResult.Fail($"The remote '{name}' does not exist.");
        }

        // rclone exige la sintaxis "remote:" (con dos puntos) para operar sobre el remoto.
        // "lsd" es una operacion de solo lectura muy ligera que fuerza la autenticacion
        // y el contacto con el endpoint, validando de facto las credenciales.
        var arguments = new List<string>
        {
            "lsd", $"{name}:",
            "--config", _parser.ResolvedConfigPath
        };

        return await RunRcloneAsync(arguments, onOutput, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<ProviderSchema?> GetProviderSchemaAsync(
        string type,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(type))
        {
            return null;
        }

        var key = type.Trim();
        if (_schemaCache.TryGetValue(key, out var cached))
        {
            return cached;
        }

        var schema = await LoadProviderSchemaAsync(key, cancellationToken).ConfigureAwait(false);
        _schemaCache[key] = schema;
        return schema;
    }

    /// <summary>
    /// Ejecuta <c>rclone config providers</c>, localiza el backend solicitado y
    /// construye su <see cref="ProviderSchema"/>. Devuelve <c>null</c> ante
    /// cualquier fallo para que la UI degrade a edicion simple sin romperse.
    /// </summary>
    private async Task<ProviderSchema?> LoadProviderSchemaAsync(
        string type,
        CancellationToken cancellationToken)
    {
        var rclonePath = _locator.ResolveRclonePath();
        if (rclonePath is null)
        {
            return null;
        }

        var startInfo = new ProcessStartInfo
        {
            FileName = rclonePath,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8
        };

        startInfo.ArgumentList.Add("config");
        startInfo.ArgumentList.Add("providers");

        try
        {
            using var process = new Process { StartInfo = startInfo, EnableRaisingEvents = true };

            var stdout = new StringBuilder();
            process.OutputDataReceived += (_, e) =>
            {
                if (e.Data is not null)
                {
                    stdout.AppendLine(e.Data);
                }
            };

            if (!process.Start())
            {
                return null;
            }

            process.BeginOutputReadLine();
            process.BeginErrorReadLine();

            await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);

            if (process.ExitCode != 0)
            {
                return null;
            }

            return ParseProviderSchema(stdout.ToString(), type);
        }
        catch (OperationCanceledException)
        {
            return null;
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception or IOException or JsonException)
        {
            return null;
        }
    }

    /// <summary>
    /// Extrae del JSON de <c>rclone config providers</c> la entrada cuyo
    /// <c>Prefix</c> coincide con el tipo solicitado.
    /// </summary>
    private static ProviderSchema? ParseProviderSchema(string json, string type)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return null;
        }

        using var document = JsonDocument.Parse(json);
        if (document.RootElement.ValueKind != JsonValueKind.Array)
        {
            return null;
        }

        foreach (var element in document.RootElement.EnumerateArray())
        {
            var prefix = GetString(element, "Prefix");
            var name = GetString(element, "Name");

            if (!string.Equals(prefix, type, StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(name, type, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var options = new Dictionary<string, ProviderOption>(StringComparer.OrdinalIgnoreCase);

            if (element.TryGetProperty("Options", out var optionsElement) &&
                optionsElement.ValueKind == JsonValueKind.Array)
            {
                foreach (var optionElement in optionsElement.EnumerateArray())
                {
                    var optionName = GetString(optionElement, "Name");
                    if (string.IsNullOrWhiteSpace(optionName))
                    {
                        continue;
                    }

                    var examples = new List<ProviderOptionExample>();
                    if (optionElement.TryGetProperty("Examples", out var examplesElement) &&
                        examplesElement.ValueKind == JsonValueKind.Array)
                    {
                        foreach (var exampleElement in examplesElement.EnumerateArray())
                        {
                            examples.Add(new ProviderOptionExample
                            {
                                Value = GetString(exampleElement, "Value"),
                                Help = GetString(exampleElement, "Help")
                            });
                        }
                    }

                    options[optionName] = new ProviderOption
                    {
                        Name = optionName,
                        Help = GetString(optionElement, "Help"),
                        Type = GetString(optionElement, "Type"),
                        Sensitive = GetBool(optionElement, "Sensitive"),
                        IsPassword = GetBool(optionElement, "IsPassword"),
                        Required = GetBool(optionElement, "Required"),
                        Advanced = GetBool(optionElement, "Advanced"),
                        DefaultValue = GetString(optionElement, "DefaultStr"),
                        Examples = examples
                    };
                }
            }

            return new ProviderSchema
            {
                Prefix = prefix,
                Name = name,
                Description = GetString(element, "Description"),
                Options = options
            };
        }

        return null;
    }

    /// <summary>Lee una propiedad de texto del JSON tolerando ausencias y nulos.</summary>
    private static string GetString(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out var property))
        {
            return string.Empty;
        }

        return property.ValueKind switch
        {
            JsonValueKind.String => property.GetString() ?? string.Empty,
            JsonValueKind.Null => string.Empty,
            _ => property.ToString()
        };
    }

    /// <summary>Lee una propiedad booleana del JSON tolerando ausencias.</summary>
    private static bool GetBool(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out var property))
        {
            return false;
        }

        return property.ValueKind == JsonValueKind.True;
    }

    /// <summary>
    /// Ejecuta rclone con los argumentos indicados, capturando stdout/stderr.
    /// El proceso se lanza sin ventana (CreateNoWindow) para no mostrar la
    /// consola negra; la salida se reenvia al callback para alimentar los
    /// indicadores de progreso de la UI durante el flujo OAuth.
    /// </summary>
    private async Task<RcloneConfigOperationResult> RunRcloneAsync(
        IReadOnlyList<string> arguments,
        Action<string>? onOutput,
        CancellationToken cancellationToken)
    {
        var rclonePath = _locator.ResolveRclonePath();
        if (rclonePath is null)
        {
            return RcloneConfigOperationResult.Fail(
                "rclone.exe not found. Configure its path in Settings.");
        }

        var startInfo = new ProcessStartInfo
        {
            FileName = rclonePath,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8
        };

        foreach (var arg in arguments)
        {
            startInfo.ArgumentList.Add(arg);
        }

        // Traza de diagnostico (FIX client_secret is missing): se imprime la
        // linea de comandos COMPLETA antes de lanzar el proceso para verificar
        // que client_id/client_secret viajan realmente. Los valores de claves
        // sensibles se enmascaran en la traza para no filtrar credenciales al
        // log, pero se indica su LONGITUD para confirmar que NO van vacios.
        var traceLine = BuildDiagnosticCommandLine(arguments);
        Debug.WriteLine($"[rclone] {traceLine}");
        Console.WriteLine($"[rclone] {traceLine}");
        onOutput?.Invoke($"[rclone] {traceLine}");

        var stdout = new StringBuilder();
        var stderr = new StringBuilder();

        try
        {
            using var process = new Process { StartInfo = startInfo, EnableRaisingEvents = true };

            process.OutputDataReceived += (_, e) =>
            {
                if (e.Data is null)
                {
                    return;
                }

                stdout.AppendLine(e.Data);
                onOutput?.Invoke(e.Data);
            };

            process.ErrorDataReceived += (_, e) =>
            {
                if (e.Data is null)
                {
                    return;
                }

                stderr.AppendLine(e.Data);
                onOutput?.Invoke(e.Data);
            };

            if (!process.Start())
            {
                return RcloneConfigOperationResult.Fail("Could not start the rclone process.");
            }

            process.BeginOutputReadLine();
            process.BeginErrorReadLine();

            await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);

            var outText = stdout.ToString().Trim();
            var errText = stderr.ToString().Trim();

            if (process.ExitCode == 0)
            {
                return RcloneConfigOperationResult.Ok(
                    string.IsNullOrWhiteSpace(outText) ? "Operation completed." : outText,
                    outText,
                    errText);
            }

            var message = string.IsNullOrWhiteSpace(errText) ? outText : errText;
            return RcloneConfigOperationResult.Fail(
                string.IsNullOrWhiteSpace(message) ? $"rclone exited with code {process.ExitCode}." : message,
                process.ExitCode,
                outText,
                errText);
        }
        catch (OperationCanceledException)
        {
            return RcloneConfigOperationResult.Fail("Operation cancelled.");
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception or IOException)
        {
            return RcloneConfigOperationResult.Fail($"Failed to run rclone: {ex.Message}");
        }
    }

    /// <summary>
    /// Construye una representacion legible de la linea de comandos para
    /// diagnostico. Los valores de claves potencialmente sensibles
    /// (client_secret, password, token, etc.) se enmascaran, pero se muestra su
    /// LONGITUD para confirmar que NO viajan vacios. El resto de argumentos se
    /// muestran tal cual para verificar el ensamblado.
    /// </summary>
    private static string BuildDiagnosticCommandLine(IReadOnlyList<string> arguments)
    {
        var builder = new StringBuilder("rclone");

        foreach (var arg in arguments)
        {
            builder.Append(' ');

            var separatorIndex = arg.IndexOf('=');
            if (separatorIndex <= 0)
            {
                builder.Append(arg);
                continue;
            }

            var key = arg.Substring(0, separatorIndex);
            var value = arg.Substring(separatorIndex + 1);

            if (IsSensitiveKey(key))
            {
                // Nunca se imprime el secreto: solo su longitud, para verificar
                // que el binding del formulario SI entrego un valor.
                builder.Append(key)
                       .Append("=<masked:len=")
                       .Append(value.Length)
                       .Append('>');
            }
            else
            {
                builder.Append(key).Append('=').Append(value);
            }
        }

        return builder.ToString();
    }

    /// <summary>Determina si una clave de configuracion es sensible.</summary>
    private static bool IsSensitiveKey(string key)
    {
        return key.Contains("secret", StringComparison.OrdinalIgnoreCase)
            || key.Contains("password", StringComparison.OrdinalIgnoreCase)
            || key.Contains("pass", StringComparison.OrdinalIgnoreCase)
            || key.Contains("token", StringComparison.OrdinalIgnoreCase)
            || key.Contains("key", StringComparison.OrdinalIgnoreCase);
    }
}
