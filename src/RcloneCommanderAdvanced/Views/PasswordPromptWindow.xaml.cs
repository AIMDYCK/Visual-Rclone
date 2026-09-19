using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using RcloneCommanderAdvanced.Helpers;
using RcloneCommanderAdvanced.ViewModels;

namespace RcloneCommanderAdvanced.Views;

/// <summary>
/// FASE 4: ventana modal que solicita la contrasena del rclone.conf cifrado.
///
/// Se muestra antes de cargar los remotos cuando el archivo esta cifrado.
/// La contrasena se valida contra rclone (via <see cref="PasswordPromptViewModel"/>)
/// y, si es correcta, se conserva en memoria para reinyectarla en cada
/// instancia de rclone. Si el usuario cancela, la aplicacion se cierra.
/// </summary>
public partial class PasswordPromptWindow : Window
{
    private readonly PasswordPromptViewModel _viewModel;

    public PasswordPromptWindow(PasswordPromptViewModel viewModel)
    {
        InitializeComponent();

        _viewModel = viewModel;
        DataContext = _viewModel;

        WindowThemeHelper.ApplyDarkTitleBar(
            this,
            captionColor: Color.FromRgb(0x13, 0x17, 0x1F),
            textColor: Color.FromRgb(0xE6, 0xED, 0xF3));

        // Foco inicial en el campo de contrasena.
        Loaded += (_, _) => PasswordInput.Focus();
    }

    /// <summary>
    /// El PasswordBox no expone una DependencyProperty enlazable por seguridad,
    /// asi que sincronizamos manualmente el valor con el ViewModel.
    /// </summary>
    private void PasswordInput_PasswordChanged(object sender, RoutedEventArgs e)
    {
        if (sender is System.Windows.Controls.PasswordBox box)
        {
            _viewModel.Password = box.Password;
        }
    }

    /// <summary>Permite confirmar con Enter desde el campo de contrasena.</summary>
    private void PasswordInput_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            e.Handled = true;
            TryUnlock();
        }
    }

    /// <summary>El usuario pulsa "Desbloquear": valida la contrasena.</summary>
    private void Unlock_Click(object sender, RoutedEventArgs e) => TryUnlock();

    /// <summary>El usuario cancela: cerramos sin desbloquear.</summary>
    private void Exit_Click(object sender, RoutedEventArgs e)
    {
        _viewModel.Reset();
        DialogResult = false;
        Close();
    }

    private void TryUnlock()
    {
        if (_viewModel.Validate())
        {
            DialogResult = true;
            Close();
        }
        else
        {
            // Contrasena incorrecta: limpiamos el campo y devolvemos el foco.
            PasswordInput.Clear();
            PasswordInput.Focus();
        }
    }
}
