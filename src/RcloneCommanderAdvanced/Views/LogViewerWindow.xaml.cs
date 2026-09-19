using System.Collections.Specialized;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using RcloneCommanderAdvanced.Helpers;
using RcloneCommanderAdvanced.ViewModels;

namespace RcloneCommanderAdvanced.Views;

/// <summary>
/// Feature 3: consola de diagnostico por unidad.
///
/// Muestra en tiempo real las ultimas lineas de stdout/stderr capturadas por
/// el MountProcessManager para el remoto seleccionado. La ventana se enlaza
/// directamente al <see cref="RemoteCardViewModel"/> de la tarjeta, por lo que
/// el buffer <c>LogLines</c> se actualiza solo (ObservableCollection).
/// </summary>
public partial class LogViewerWindow : Window
{
    private readonly RemoteCardViewModel _card;

    public LogViewerWindow(RemoteCardViewModel card)
    {
        InitializeComponent();

        _card = card ?? throw new System.ArgumentNullException(nameof(card));
        DataContext = card;

        Title = $"Consola de diagnostico - {card.Name}";

        // Auto-scroll al final cuando llegan nuevas lineas.
        card.LogLines.CollectionChanged += OnLogLinesChanged;

        // Barra de titulo oscura nativa (Windows 10/11).
        WindowThemeHelper.ApplyDarkTitleBar(
            this,
            captionColor: Color.FromRgb(0x13, 0x17, 0x1F),
            textColor: Color.FromRgb(0xE6, 0xED, 0xF3));

        Closed += (_, _) => card.LogLines.CollectionChanged -= OnLogLinesChanged;
    }

    private void OnLogLinesChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.Action != NotifyCollectionChangedAction.Add)
        {
            return;
        }

        // Desplazar la consola al ultimo mensaje.
        var listBox = FindListBox(this);
        if (listBox is not null && listBox.Items.Count > 0)
        {
            listBox.ScrollIntoView(listBox.Items[listBox.Items.Count - 1]);
        }
    }

    /// <summary>Busca el primer ListBox descendiente en el arbol visual.</summary>
    private static ListBox? FindListBox(DependencyObject parent)
    {
        var count = VisualTreeHelper.GetChildrenCount(parent);

        for (var i = 0; i < count; i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);

            if (child is ListBox listBox)
            {
                return listBox;
            }

            var result = FindListBox(child);
            if (result is not null)
            {
                return result;
            }
        }

        return null;
    }

    private void OnCloseClick(object sender, RoutedEventArgs e) => Close();
}
