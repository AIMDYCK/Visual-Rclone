using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using RcloneCommanderAdvanced.Helpers;
using RcloneCommanderAdvanced.ViewModels;

namespace RcloneCommanderAdvanced.Views;

/// <summary>
/// Ventana de bloqueo. Se muestra al arrancar la app y cada vez que el
/// usuario bloquea la sesion desde el dashboard.
/// </summary>
public partial class LockWindow : Window
{
    private readonly LockViewModel _viewModel;

    public LockWindow(LockViewModel viewModel)
    {
        InitializeComponent();

        _viewModel = viewModel;
        DataContext = viewModel;

        _viewModel.Unlocked += OnUnlocked;
        Loaded += (_, _) => PinBox.Focus();

        // Barra de titulo oscura nativa (Windows 10/11).
        WindowThemeHelper.ApplyDarkTitleBar(
            this,
            captionColor: Color.FromRgb(0x13, 0x17, 0x1F),
            textColor: Color.FromRgb(0xE6, 0xED, 0xF3));
    }

    private void OnPinChanged(object sender, RoutedEventArgs e)
    {
        if (sender is PasswordBox box)
        {
            _viewModel.Pin = box.Password;
        }
    }

    private void OnPinKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && _viewModel.UnlockCommand.CanExecute(null))
        {
            _viewModel.UnlockCommand.Execute(null);
        }
    }

    private void OnUnlocked(object? sender, System.EventArgs e)
    {
        // El evento puede dispararse desde un hilo del pool (VerifyPinAsync usa
        // ConfigureAwait(false)); hay que volver al hilo de UI para tocar
        // DialogResult/Close, o WPF lanzara una excepcion silenciosa.
        if (!Dispatcher.CheckAccess())
        {
            Dispatcher.Invoke(() => OnUnlocked(sender, e));
            return;
        }

        DialogResult = true;
        Close();
    }
}
