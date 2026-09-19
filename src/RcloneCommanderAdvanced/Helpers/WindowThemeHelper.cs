using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;

namespace RcloneCommanderAdvanced.Helpers;

/// <summary>
/// Aplica el tema oscuro nativo de Windows 10/11 a la barra de titulo
/// (caption bar) de una ventana WPF usando la API DwmSetWindowAttribute.
///
/// Se usa P/Invoke en lugar de WindowChrome para conservar intactos los
/// botones nativos (minimizar / maximizar / cerrar), el arrastre, el
/// doble clic para maximizar y los Snap Layouts de Windows 11.
/// </summary>
public static class WindowThemeHelper
{
    // Atributo DWMWA_USE_IMMERSIVE_DARK_MODE.
    // Windows 10 20H1+ (build 19041) y Windows 11 usan el valor 20.
    // Windows 10 1809/1903/1909 usan el valor 19.
    private const int DWMWA_USE_IMMERSIVE_DARK_MODE = 20;
    private const int DWMWA_USE_IMMERSIVE_DARK_MODE_BEFORE_20H1 = 19;

    // DWMWA_CAPTION_COLOR (Windows 11 build 22000+): color solido de la barra.
    private const int DWMWA_CAPTION_COLOR = 35;
    // DWMWA_TEXT_COLOR (Windows 11 build 22000+): color del texto de la barra.
    private const int DWMWA_TEXT_COLOR = 36;

    [DllImport("dwmapi.dll", PreserveSig = true)]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int attrValue, int attrSize);

    /// <summary>
    /// Activa el modo oscuro de la barra de titulo. Debe invocarse cuando la
    /// ventana ya tiene un handle nativo (evento SourceInitialized o Loaded).
    /// </summary>
    /// <param name="window">Ventana WPF a tematizar.</param>
    /// <param name="captionColor">
    /// Color opcional de fondo de la barra (solo Windows 11). Si es null se
    /// deja el tono oscuro por defecto del sistema.
    /// </param>
    /// <param name="textColor">
    /// Color opcional del texto/titulo de la barra (solo Windows 11).
    /// </param>
    public static void ApplyDarkTitleBar(
        Window window,
        Color? captionColor = null,
        Color? textColor = null)
    {
        if (window is null)
        {
            return;
        }

        // Si aun no hay handle, enganchamos SourceInitialized para aplicarlo
        // en cuanto la ventana nativa exista.
        var handle = new WindowInteropHelper(window).Handle;
        if (handle == IntPtr.Zero)
        {
            void OnSourceInitialized(object? sender, EventArgs e)
            {
                window.SourceInitialized -= OnSourceInitialized;
                ApplyToHandle(new WindowInteropHelper(window).Handle, captionColor, textColor);
            }

            window.SourceInitialized += OnSourceInitialized;
            return;
        }

        ApplyToHandle(handle, captionColor, textColor);
    }

    private static void ApplyToHandle(IntPtr hwnd, Color? captionColor, Color? textColor)
    {
        if (hwnd == IntPtr.Zero)
        {
            return;
        }

        try
        {
            // 1) Modo oscuro inmersion. Probamos primero el valor moderno (20)
            //    y, si falla, el heredado (19) para Windows 10 antiguo.
            int useDark = 1;
            int result = DwmSetWindowAttribute(
                hwnd,
                DWMWA_USE_IMMERSIVE_DARK_MODE,
                ref useDark,
                sizeof(int));

            if (result != 0)
            {
                DwmSetWindowAttribute(
                    hwnd,
                    DWMWA_USE_IMMERSIVE_DARK_MODE_BEFORE_20H1,
                    ref useDark,
                    sizeof(int));
            }

            // 2) Color solido de la barra (Windows 11). Si la API no lo soporta
            //    simplemente devuelve error y se ignora.
            if (captionColor.HasValue)
            {
                int caption = ToColorRef(captionColor.Value);
                DwmSetWindowAttribute(hwnd, DWMWA_CAPTION_COLOR, ref caption, sizeof(int));
            }

            // 3) Color del texto de la barra (Windows 11).
            if (textColor.HasValue)
            {
                int text = ToColorRef(textColor.Value);
                DwmSetWindowAttribute(hwnd, DWMWA_TEXT_COLOR, ref text, sizeof(int));
            }
        }
        catch (DllNotFoundException)
        {
            // dwmapi.dll no disponible (sistemas muy antiguos): se ignora.
        }
        catch (EntryPointNotFoundException)
        {
            // API no soportada en esta version de Windows: se ignora.
        }
    }

    /// <summary>
    /// Convierte un <see cref="Color"/> de WPF al formato COLORREF (0x00BBGGRR)
    /// que espera DWM.
    /// </summary>
    private static int ToColorRef(Color color)
    {
        return color.R | (color.G << 8) | (color.B << 16);
    }
}
