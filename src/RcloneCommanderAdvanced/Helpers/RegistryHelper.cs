using System;
using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace RcloneCommanderAdvanced.Helpers;

/// <summary>
/// Utilidades de registro de Windows para ocultar letras de unidad en el
/// Explorador mediante las claves de politica <c>NoDrives</c> y
/// <c>NoViewOnDrive</c>.
///
/// <para>
/// Ambas claves son DWORD de 32 bits donde cada bit representa una letra de
/// unidad: A=bit 0 (valor 1), B=bit 1 (2), C=bit 2 (4), D=bit 3 (8),
/// E=bit 4 (16), F=bit 5 (32), ... Z=bit 25 (33554432).
/// </para>
///
/// <para>
/// Diferencia clave entre las dos politicas:
/// <list type="bullet">
///   <item><description><c>NoDrives</c>: oculta la letra de unidad SOLO para
///   unidades locales/fisicas. NO oculta unidades de red.</description></item>
///   <item><description><c>NoViewOnDrive</c>: oculta la letra de unidad tanto
///   para unidades locales COMO para unidades de red. Es la politica que
///   realmente funciona con montajes de rclone en modo red
///   (<c>--network-mode</c>).</description></item>
/// </list>
/// Por eso escribimos SIEMPRE ambas claves: asi la unidad queda oculta sin
/// importar el modo de montaje elegido por el usuario.
/// </para>
///
/// <para>
/// Las claves se escriben en la rama del usuario actual (HKCU), por lo que NO
/// requieren privilegios de administrador:
/// <c>HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Policies\Explorer</c>.
/// </para>
/// </summary>
public static class RegistryHelper
{
    private const string ExplorerPolicyPath =
        @"Software\Microsoft\Windows\CurrentVersion\Policies\Explorer";

    private const string NoDrivesValueName = "NoDrives";
    private const string NoViewOnDriveValueName = "NoViewOnDrive";

    // SHChangeNotify: notifica al shell que algo cambio para que refresque.
    private const int SHCNE_ASSOCCHANGED = 0x08000000;
    private const int SHCNE_DRIVEADD = 0x00000100;
    private const int SHCNE_DRIVEREMOVED = 0x00000080;
    private const int SHCNF_IDLIST = 0x0000;

    // Broadcast de cambio de configuracion: hace que el Explorador relea las
    // politicas de unidad sin necesidad de reiniciar sesion.
    private static readonly IntPtr HWND_BROADCAST = new(0xFFFF);
    private const int WM_SETTINGCHANGE = 0x001A;
    private const int WM_WININICHANGE = 0x001A;

    [DllImport("shell32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    private static extern void SHChangeNotify(
        int wEventId, uint uFlags, IntPtr dwItem1, IntPtr dwItem2);

    [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    private static extern IntPtr SendMessageTimeout(
        IntPtr hWnd,
        int msg,
        IntPtr wParam,
        string lParam,
        uint fuFlags,
        uint uTimeout,
        out IntPtr lpdwResult);

    private const uint SMTO_ABORTIFHUNG = 0x0002;

    /// <summary>
    /// Convierte una letra de unidad (ej: "F" o "F:") en su mascara de bit
    /// dentro de los valores <c>NoDrives</c> / <c>NoViewOnDrive</c>.
    /// Devuelve 0 si la letra no es valida.
    /// </summary>
    public static uint GetDriveBitMask(string? driveLetter)
    {
        if (string.IsNullOrWhiteSpace(driveLetter))
        {
            return 0u;
        }

        var letter = char.ToUpperInvariant(driveLetter.Trim()[0]);

        if (letter < 'A' || letter > 'Z')
        {
            return 0u;
        }

        var index = letter - 'A';
        return 1u << index;
    }

    /// <summary>
    /// Lee el valor <c>NoDrives</c> actual. Devuelve 0 si no existe.
    /// </summary>
    public static uint GetNoDrives() => ReadDword(NoDrivesValueName);

    /// <summary>
    /// Lee el valor <c>NoViewOnDrive</c> actual. Devuelve 0 si no existe.
    /// </summary>
    public static uint GetNoViewOnDrive() => ReadDword(NoViewOnDriveValueName);

    private static uint ReadDword(string valueName)
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(ExplorerPolicyPath, writable: false);
            var raw = key?.GetValue(valueName);

            return raw switch
            {
                int i => unchecked((uint)i),
                uint u => u,
                long l => unchecked((uint)l),
                _ => 0u
            };
        }
        catch (Exception)
        {
            // Si el registro no es accesible, asumimos que no hay bits ocultos.
            return 0u;
        }
    }

    /// <summary>
    /// Indica si la letra indicada esta actualmente oculta en el Explorador
    /// (basta con que este oculta por cualquiera de las dos politicas).
    /// </summary>
    public static bool IsDriveHidden(string? driveLetter)
    {
        var mask = GetDriveBitMask(driveLetter);

        if (mask == 0u)
        {
            return false;
        }

        return (GetNoDrives() & mask) != 0u || (GetNoViewOnDrive() & mask) != 0u;
    }

    /// <summary>
    /// Oculta la letra de unidad indicada anadiendo su bit a <c>NoDrives</c>
    /// y a <c>NoViewOnDrive</c>, y notifica al shell para que el cambio sea
    /// inmediato. Se escriben ambas politicas porque <c>NoDrives</c> por si
    /// sola NO oculta unidades de red (caso de los montajes con
    /// <c>--network-mode</c>).
    /// </summary>
    /// <returns>true si la operacion se completo correctamente.</returns>
    public static bool HideDrive(string? driveLetter)
    {
        var mask = GetDriveBitMask(driveLetter);

        if (mask == 0u)
        {
            return false;
        }

        var okNoDrives = UpdateDword(NoDrivesValueName, current => current | mask);
        var okNoView = UpdateDword(NoViewOnDriveValueName, current => current | mask);

        NotifyShellChanged();
        return okNoDrives && okNoView;
    }

    /// <summary>
    /// Muestra (deja de ocultar) la letra de unidad indicada quitando su bit
    /// de <c>NoDrives</c> y de <c>NoViewOnDrive</c>, y notifica al shell.
    /// </summary>
    /// <returns>true si la operacion se completo correctamente.</returns>
    public static bool ShowDrive(string? driveLetter)
    {
        var mask = GetDriveBitMask(driveLetter);

        if (mask == 0u)
        {
            return false;
        }

        var okNoDrives = UpdateDword(NoDrivesValueName, current => current & ~mask);
        var okNoView = UpdateDword(NoViewOnDriveValueName, current => current & ~mask);

        NotifyShellChanged();
        return okNoDrives && okNoView;
    }

    /// <summary>
    /// Aplica una transformacion a un valor DWORD de politica, lo persiste y
    /// deja el registro limpio si el resultado es 0.
    /// </summary>
    private static bool UpdateDword(string valueName, Func<uint, uint> transform)
    {
        try
        {
            using var key = Registry.CurrentUser.CreateSubKey(ExplorerPolicyPath, writable: true);

            if (key is null)
            {
                return false;
            }

            var updated = transform(ReadDword(valueName));

            if (updated == 0u)
            {
                // Sin bits ocultos: eliminamos el valor para no dejar basura.
                key.DeleteValue(valueName, throwOnMissingValue: false);
            }
            else
            {
                key.SetValue(valueName, unchecked((int)updated), RegistryValueKind.DWord);
            }

            return true;
        }
        catch (Exception)
        {
            return false;
        }
    }

    /// <summary>
    /// Notifica al shell de Windows que la configuracion cambio para que el
    /// Explorador refresque las unidades visibles de inmediato.
    ///
    /// <para>
    /// Se combinan tres mecanismos porque ninguno es suficiente por si solo:
    /// <list type="number">
    ///   <item><description><c>SHChangeNotify(SHCNE_ASSOCCHANGED)</c> fuerza un
    ///   refresco general del shell.</description></item>
    ///   <item><description><c>SHChangeNotify(SHCNE_DRIVEREMOVED/DRIVEADD)</c>
    ///   simula la retirada y reaparicion de la unidad para que el arbol del
    ///   Explorador se reconstruya.</description></item>
    ///   <item><description>Broadcast de <c>WM_SETTINGCHANGE</c> para que las
    ///   politicas de grupo se relean sin cerrar sesion.</description></item>
    /// </list>
    /// </para>
    /// </summary>
    public static void NotifyShellChanged()
    {
        try
        {
            SHChangeNotify(SHCNE_ASSOCCHANGED, SHCNF_IDLIST, IntPtr.Zero, IntPtr.Zero);
            SHChangeNotify(SHCNE_DRIVEREMOVED, SHCNF_IDLIST, IntPtr.Zero, IntPtr.Zero);
            SHChangeNotify(SHCNE_DRIVEADD, SHCNF_IDLIST, IntPtr.Zero, IntPtr.Zero);
        }
        catch (Exception)
        {
            // La notificacion es best-effort.
        }

        try
        {
            SendMessageTimeout(
                HWND_BROADCAST,
                WM_SETTINGCHANGE,
                IntPtr.Zero,
                "Policy",
                SMTO_ABORTIFHUNG,
                1000,
                out _);
        }
        catch (Exception)
        {
            // Best-effort: si falla, el cambio se aplicara al reiniciar el
            // Explorador o al cerrar/iniciar sesion.
        }
    }
}
