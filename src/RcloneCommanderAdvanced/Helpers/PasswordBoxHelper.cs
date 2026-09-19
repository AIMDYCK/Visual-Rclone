using System.Windows;
using System.Windows.Controls;

namespace RcloneCommanderAdvanced.Helpers;

/// <summary>
/// Propiedad adjunta que permite enlazar (binding) el contenido de un
/// <see cref="PasswordBox"/> sin escribir logica en el code-behind.
///
/// WPF no expone <c>Password</c> como DependencyProperty a proposito: guardar
/// el texto plano en el sistema de propiedades lo dejaria residente en memoria
/// y visible para cualquier binding. Este helper mantiene la comodidad del
/// binding MVVM sincronizando el valor en ambos sentidos mediante los eventos
/// <c>PasswordChanged</c> y <c>BoundPasswordChanged</c>, y ademas evita bucles
/// de reentrada con una bandera interna.
/// </summary>
public static class PasswordBoxHelper
{
    /// <summary>Bandera interna para evitar reentradas al sincronizar.</summary>
    private static readonly DependencyProperty UpdatingProperty =
        DependencyProperty.RegisterAttached(
            "Updating",
            typeof(bool),
            typeof(PasswordBoxHelper),
            new PropertyMetadata(false));

    /// <summary>Propiedad adjunta que transporta el valor enlazable.</summary>
    public static readonly DependencyProperty BoundPasswordProperty =
        DependencyProperty.RegisterAttached(
            "BoundPassword",
            typeof(string),
            typeof(PasswordBoxHelper),
            new FrameworkPropertyMetadata(
                string.Empty,
                FrameworkPropertyMetadataOptions.BindsTwoWayByDefault,
                OnBoundPasswordChanged));

    public static string GetBoundPassword(DependencyObject obj)
        => (string)obj.GetValue(BoundPasswordProperty);

    public static void SetBoundPassword(DependencyObject obj, string value)
        => obj.SetValue(BoundPasswordProperty, value);

    private static bool GetUpdating(DependencyObject obj)
        => (bool)obj.GetValue(UpdatingProperty);

    private static void SetUpdating(DependencyObject obj, bool value)
        => obj.SetValue(UpdatingProperty, value);

    private static void OnBoundPasswordChanged(
        DependencyObject d,
        DependencyPropertyChangedEventArgs e)
    {
        if (d is not PasswordBox passwordBox)
        {
            return;
        }

        // Se engancha una sola vez al evento del control.
        passwordBox.PasswordChanged -= OnPasswordChanged;
        passwordBox.PasswordChanged += OnPasswordChanged;

        if (GetUpdating(passwordBox))
        {
            return;
        }

        var newValue = e.NewValue as string ?? string.Empty;
        if (!string.Equals(passwordBox.Password, newValue, StringComparison.Ordinal))
        {
            passwordBox.Password = newValue;
        }
    }

    private static void OnPasswordChanged(object sender, RoutedEventArgs e)
    {
        if (sender is not PasswordBox passwordBox)
        {
            return;
        }

        SetUpdating(passwordBox, true);
        SetBoundPassword(passwordBox, passwordBox.Password);
        SetUpdating(passwordBox, false);
    }
}
