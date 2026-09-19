using System;
using System.Diagnostics;
using System.IO;
using RcloneCommanderAdvanced.Services.Abstractions;

namespace RcloneCommanderAdvanced.Services;

/// <summary>
/// FASE 4: implementacion del soporte de rclone.conf cifrado.
///
/// Deteccion: rclone cifra el archivo con la cabecera <c>RCLONE_ENCRYPT_V0</c>
/// en la primera linea. Es una comprobacion barata (leer solo el inicio del
/// archivo) que no requiere lanzar ningun proceso.
///
/// Validacion: se ejecuta <c>rclone listremotes</c> con la variable de entorno
/// <c>RCLONE_CONFIG_PASS</c> fijada a nivel de PROCESO
/// (<see cref="EnvironmentVariableTarget.Process"/>). Si la contrasena es
/// correcta, rclone devuelve la lista de remotos y codigo de salida 0. Si es
/// incorrecta, falla y se limpia la variable inmediatamente.
///
/// SEGURIDAD: la contrasena NUNCA se persiste en disco ni se escribe en la
/// linea de comandos (seria visible en el Administrador de tareas). Solo vive
/// en memoria y en la variable de entorno del propio proceso.
/// </summary>
public sealed class ConfigEncryptionService : IConfigEncryptionService
{
    /// <summary>Cabecera que rclone escribe en la primera linea de un .conf cifrado.</summary>
    private const string EncryptionHeader = "RCLONE_ENCRYPT_V0";

    /// <summary>Nombre de la variable de entorno que rclone usa para descifrar.</summary>
    private const string PasswordEnvVar = "RCLONE_CONFIG_PASS";

    private readonly IRcloneConfigParser _configParser;
    private readonly IRcloneLocator _rcloneLocator;

    /// <summary>Contrasena valida en memoria (nunca se escribe en disco).</summary>
    private string? _password;

    public ConfigEncryptionService(
        IRcloneConfigParser configParser,
        IRcloneLocator rcloneLocator)
    {
        _configParser = configParser;
        _rcloneLocator = rcloneLocator;
    }

    /// <inheritdoc />
    public bool HasPassword => !string.IsNullOrEmpty(_password);

    /// <inheritdoc />
    public bool IsConfigEncrypted()
    {
        try
        {
            var path = _configParser.ResolvedConfigPath;
            if (!File.Exists(path))
            {
                return false;
            }

            // Leemos solo las primeras lineas: la cabecera esta al principio.
            using var reader = new StreamReader(path);
            for (var i = 0; i < 3 && !reader.EndOfStream; i++)
            {
                var line = reader.ReadLine();
                if (line is null)
                {
                    break;
                }

                if (line.TrimStart().StartsWith(EncryptionHeader, StringComparison.Ordinal))
                {
                    return true;
                }
            }

            return false;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Si no podemos leerlo, asumimos que no esta cifrado para no
            // bloquear el arranque con un falso positivo.
            return false;
        }
    }

    /// <inheritdoc />
    public bool TryUnlock(string password)
    {
        if (string.IsNullOrEmpty(password))
        {
            return false;
        }

        var rclonePath = _rcloneLocator.ResolveRclonePath();
        if (rclonePath is null)
        {
            return false;
        }

        // Fijamos la contrasena SOLO para este proceso. Nunca global.
        Environment.SetEnvironmentVariable(
            PasswordEnvVar, password, EnvironmentVariableTarget.Process);

        try
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = rclonePath,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            startInfo.ArgumentList.Add("listremotes");
            startInfo.ArgumentList.Add("--config");
            startInfo.ArgumentList.Add(_configParser.ResolvedConfigPath);

            using var process = Process.Start(startInfo);
            if (process is null)
            {
                ClearPassword();
                return false;
            }

            // Drenamos ambos flujos para evitar bloqueos por buffer lleno.
            var stdout = process.StandardOutput.ReadToEnd();
            var stderr = process.StandardError.ReadToEnd();

            if (!process.WaitForExit(15000))
            {
                try
                {
                    process.Kill(entireProcessTree: true);
                }
                catch (InvalidOperationException)
                {
                    // El proceso ya termino.
                }

                ClearPassword();
                return false;
            }

            // rclone devuelve 0 y la lista de remotos si la contrasena es
            // correcta. Un error de descifrado produce codigo != 0 y un
            // mensaje que menciona la contrasena en stderr.
            var success = process.ExitCode == 0 &&
                          !stderr.Contains("password", StringComparison.OrdinalIgnoreCase);

            if (success)
            {
                _password = password;
                return true;
            }

            // Contrasena incorrecta: limpiamos de inmediato.
            ClearPassword();
            return false;
        }
        catch (Exception ex) when (ex is IOException or InvalidOperationException
                                       or System.ComponentModel.Win32Exception)
        {
            ClearPassword();
            return false;
        }
    }

    /// <inheritdoc />
    public void ApplyPasswordToEnvironment()
    {
        if (string.IsNullOrEmpty(_password))
        {
            return;
        }

        Environment.SetEnvironmentVariable(
            PasswordEnvVar, _password, EnvironmentVariableTarget.Process);
    }

    /// <inheritdoc />
    public void ClearPassword()
    {
        _password = null;
        Environment.SetEnvironmentVariable(
            PasswordEnvVar, null, EnvironmentVariableTarget.Process);
    }
}
