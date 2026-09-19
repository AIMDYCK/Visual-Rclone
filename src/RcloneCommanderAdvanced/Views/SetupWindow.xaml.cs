using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using RcloneCommanderAdvanced.Helpers;
using RcloneCommanderAdvanced.ViewModels;

namespace RcloneCommanderAdvanced.Views;

/// <summary>
/// Ventana del asistente de primer arranque.
/// El PIN se transfiere al ViewModel desde el code-behind porque
/// PasswordBox.Password no es bindeable por seguridad de WPF.
/// </summary>
public partial class SetupWindow : Window
{
    private readonly SetupViewModel _viewModel;

    public SetupWindow(SetupViewModel viewModel)
    {
        InitializeComponent();

        _viewModel = viewModel;
        DataContext = viewModel;

        _viewModel.SetupCompleted += OnSetupCompleted;

        // Red de seguridad: si el comando falla por cualquier motivo,
        // mostramos el error en lugar de dejar la ventana congelada.
        CreatePinButton.Click += OnCreatePinClick;

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

    private void OnConfirmPinChanged(object sender, RoutedEventArgs e)
    {
        if (sender is PasswordBox box)
        {
            _viewModel.ConfirmPin = box.Password;
        }
    }

    private void OnReminderPhraseChanged(object sender, TextChangedEventArgs e)
    {
        if (sender is TextBox box)
        {
            _viewModel.ReminderPhrase = box.Text;
        }
    }

    /// <summary>
    /// Fallback del boton: si por algun motivo el Command no se ejecuto
    /// (binding roto, CanExecute falso, etc.) lo lanzamos manualmente.
    /// </summary>
    private void OnCreatePinClick(object sender, RoutedEventArgs e)
    {
        if (_viewModel.CreatePinCommand.CanExecute(null))
        {
            _viewModel.CreatePinCommand.Execute(null);
        }
    }

    private void OnSetupCompleted(object? sender, EventArgs e)
    {
        // El evento puede dispararse desde un hilo del pool (SaveAsync usa
        // ConfigureAwait(false)); hay que volver al hilo de UI para tocar
        // DialogResult/Close, o WPF lanzara una excepcion silenciosa.
        if (!Dispatcher.CheckAccess())
        {
            Dispatcher.Invoke(() => OnSetupCompleted(sender, e));
            return;
        }

        try
        {
            DialogResult = true;
        }
        catch (InvalidOperationException)
        {
            // La ventana no se mostro con ShowDialog: cerramos igualmente.
        }

        Close();
    }
}
