using System;
using System.Windows;
using System.Windows.Media;
using RcloneCommanderAdvanced.Helpers;
using RcloneCommanderAdvanced.ViewModels;

namespace RcloneCommanderAdvanced.Views;

/// <summary>
/// FEATURE Monitor de Trafico: ventana grafica de velocidad en tiempo real.
///
/// Se enlaza al <see cref="TrafficMonitorViewModel"/>, que a su vez consume
/// el historial circular del <see cref="RemoteCardViewModel"/> de la tarjeta.
/// La grafica es 100% nativa (Canvas + Polyline), sin dependencias NuGet.
///
/// El code-behind SOLO se ocupa de la sincronizacion de tamano del lienzo
/// (necesaria para normalizar las coordenadas) y del dibujo de la rejilla de
/// fondo. Toda la logica de datos vive en el ViewModel (MVVM estricto).
/// </summary>
public partial class TrafficMonitorWindow : Window
{
    private readonly TrafficMonitorViewModel _viewModel;

    public TrafficMonitorWindow(TrafficMonitorViewModel viewModel)
    {
        InitializeComponent();

        _viewModel = viewModel ?? throw new ArgumentNullException(nameof(viewModel));
        DataContext = viewModel;

        Title = $"Monitor de trafico - {viewModel.RemoteName}";

        // Barra de titulo oscura nativa (Windows 10/11).
        WindowThemeHelper.ApplyDarkTitleBar(
            this,
            captionColor: Color.FromRgb(0x13, 0x17, 0x1F),
            textColor: Color.FromRgb(0xE6, 0xED, 0xF3));

        // Sincroniza el tamano inicial del area de dibujo y re-arma el sondeo
        // de telemetria por si el bucle no hubiera arrancado (BUGFIX Monitor
        // de Trafico: grafica congelada en "0 B/s").
        Loaded += (_, _) =>
        {
            _viewModel.Source.EnsureTelemetry();
            SyncChartSize();
        };

        // Libera las suscripciones del ViewModel al cerrar.
        Closed += (_, _) => _viewModel.Dispose();
    }

    /// <summary>
    /// Propaga el tamano real del host de la grafica al ViewModel para que
    /// la normalizacion de puntos use el area correcta.
    /// </summary>
    private void OnChartHostSizeChanged(object sender, SizeChangedEventArgs e) => SyncChartSize();

    private void SyncChartSize()
    {
        var width = ChartHost.ActualWidth;
        var height = ChartHost.ActualHeight;

        if (width <= 0 || height <= 0)
        {
            return;
        }

        _viewModel.ChartWidth = width;
        _viewModel.ChartHeight = height;

        DrawGridLines(width, height);
    }

    /// <summary>
    /// Dibuja cuatro lineas horizontales de referencia repartidas por el
    /// alto del lienzo, para dar sensacion de escala sin coste alguno.
    /// </summary>
    private void DrawGridLines(double width, double height)
    {
        var lines = new[] { GridLine1, GridLine2, GridLine3, GridLine4 };

        for (var i = 0; i < lines.Length; i++)
        {
            var y = height * (i + 1) / (lines.Length + 1);

            lines[i].X1 = 0;
            lines[i].X2 = width;
            lines[i].Y1 = y;
            lines[i].Y2 = y;
        }
    }
}
