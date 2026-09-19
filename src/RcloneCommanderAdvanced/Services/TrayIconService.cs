using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Imaging;
using Hardcodet.Wpf.TaskbarNotification;
using RcloneCommanderAdvanced.Services.Abstractions;

namespace RcloneCommanderAdvanced.Services;

/// <summary>
/// FASE 2: implementacion del icono de la bandeja del sistema basada en
/// <see cref="TaskbarIcon"/> (Hardcodet.NotifyIcon.Wpf).
///
/// El menu contextual se construye en codigo para no depender de recursos XAML
/// y se localiza mediante <see cref="ILocalizationService"/>.
///
/// El servicio NO conoce la logica de negocio: solo eleva eventos que App.xaml.cs
/// traduce en acciones (abrir panel, desmontar todo, salir).
/// </summary>
public sealed class TrayIconService : ITrayIconService
{
    private readonly ILocalizationService _localization;
    private TaskbarIcon? _icon;
    private bool _disposed;

    public TrayIconService(ILocalizationService localization)
    {
        _localization = localization;
    }

    /// <inheritdoc />
    public event EventHandler? OpenRequested;

    /// <inheritdoc />
    public event EventHandler? UnmountAllRequested;

    /// <inheritdoc />
    public event EventHandler? ExitRequested;

    /// <inheritdoc />
    public void Show()
    {
        if (_disposed)
        {
            return;
        }

        if (_icon is not null)
        {
            _icon.Visibility = Visibility.Visible;
            return;
        }

        _icon = new TaskbarIcon
        {
            ToolTipText = "Visual Rclone",
            IconSource = LoadTrayIcon(),
            ContextMenu = BuildContextMenu()
        };

        // Doble clic sobre el icono = abrir el panel.
        _icon.TrayMouseDoubleClick += (_, _) => OpenRequested?.Invoke(this, EventArgs.Empty);
    }

    /// <inheritdoc />
    public void Hide()
    {
        if (_icon is null)
        {
            return;
        }

        _icon.Visibility = Visibility.Collapsed;
    }

    /// <inheritdoc />
    public void ShowNotification(string title, string message)
    {
        try
        {
            _icon?.ShowBalloonTip(title, message, BalloonIcon.Info);
        }
        catch (Exception ex) when (ex is InvalidOperationException or NotSupportedException)
        {
            // La notificacion es best-effort.
        }
    }

    /// <summary>
    /// Construye el menu contextual de la bandeja con las tres acciones
    /// requeridas: Abrir Panel, Desmontar Todo y Salir completamente.
    /// </summary>
    private ContextMenu BuildContextMenu()
    {
        var menu = new ContextMenu();

        var openItem = new MenuItem
        {
            Header = _localization.Get("Str_TrayOpen", "Open Panel"),
            FontWeight = FontWeights.SemiBold
        };
        openItem.Click += (_, _) => OpenRequested?.Invoke(this, EventArgs.Empty);

        var unmountItem = new MenuItem
        {
            Header = _localization.Get("Str_TrayUnmountAll", "Unmount All")
        };
        unmountItem.Click += (_, _) => UnmountAllRequested?.Invoke(this, EventArgs.Empty);

        var exitItem = new MenuItem
        {
            Header = _localization.Get("Str_TrayExit", "Exit Completely")
        };
        exitItem.Click += (_, _) => ExitRequested?.Invoke(this, EventArgs.Empty);

        menu.Items.Add(openItem);
        menu.Items.Add(unmountItem);
        menu.Items.Add(new Separator());
        menu.Items.Add(exitItem);

        return menu;
    }

    /// <summary>
    /// Carga el icono de la bandeja desde el recurso embebido de la aplicacion.
    /// Si no esta disponible, se devuelve null y Windows usara el icono por
    /// defecto del ejecutable.
    /// </summary>
    private static BitmapSource? LoadTrayIcon()
    {
        try
        {
            var uri = new Uri("pack://application:,,,/Assets/app.ico", UriKind.Absolute);
            return new BitmapImage(uri);
        }
        catch (Exception ex) when (ex is NotSupportedException or System.IO.IOException
                                       or System.IO.FileNotFoundException or UriFormatException)
        {
            return null;
        }
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        if (_icon is not null)
        {
            _icon.Dispose();
            _icon = null;
        }
    }
}
