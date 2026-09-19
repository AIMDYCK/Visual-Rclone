using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Windows;
using System.Windows.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using RcloneCommanderAdvanced.Models;
using RcloneCommanderAdvanced.Services.Abstractions;

namespace RcloneCommanderAdvanced.ViewModels;

/// <summary>
/// FEATURE Monitor de Trafico: ViewModel de la ventana grafica.
///
/// Consume el historial circular <see cref="RemoteCardViewModel.TrafficHistory"/>
/// y lo transforma en dos cadenas de puntos (<c>Polyline.Points</c>) listas
/// para dibujarse sobre un <see cref="System.Windows.Controls.Canvas"/>.
///
/// La grafica es 100% nativa (sin dependencias NuGet): se normalizan los
/// valores de velocidad al area del lienzo y se generan las coordenadas
/// X/Y de cada serie. Los ejes se auto-ajustan al pico maximo observado
/// para que ningun pico se salga del marco.
/// </summary>
public sealed partial class TrafficMonitorViewModel : ObservableObject, IDisposable
{
    /// <summary>Margen interno del area de dibujo (px) para no tocar los bordes.</summary>
    private const double ChartPadding = 8.0;

    private readonly RemoteCardViewModel _source;
    private readonly ILocalizationService? _localization;

    /// <summary>Ancho logico del area de dibujo (se sincroniza con la vista).</summary>
    private double _chartWidth = 640.0;

    /// <summary>Alto logico del area de dibujo (se sincroniza con la vista).</summary>
    private double _chartHeight = 220.0;

    public TrafficMonitorViewModel(
        RemoteCardViewModel source,
        ILocalizationService? localization = null)
    {
        _source = source ?? throw new ArgumentNullException(nameof(source));
        _localization = localization;

        // Reaccionamos a cada nueva muestra y a los cambios de pico.
        _source.TrafficHistory.CollectionChanged += OnHistoryChanged;
        _source.PropertyChanged += OnSourcePropertyChanged;

        Rebuild();
    }

    /// <summary>
    /// BUGFIX Monitor de Trafico: tarjeta de origen. La vista la usa para
    /// re-armar el sondeo al abrirse (EnsureTelemetry) y garantizar que el
    /// flujo de datos este vivo aunque el bucle no hubiera arrancado antes.
    /// </summary>
    public RemoteCardViewModel Source => _source;

    /// <summary>Nombre del remoto monitorizado.</summary>
    public string RemoteName => _source.Name;

    /// <summary>Etiqueta de la unidad (ej. "X:").</summary>
    public string DriveLabel => _source.DriveLabel;

    /// <summary>Puntos normalizados de la serie de bajada (verde).</summary>
    [ObservableProperty]
    private PointCollection _downloadPoints = new();

    /// <summary>Puntos normalizados de la serie de subida (azul).</summary>
    [ObservableProperty]
    private PointCollection _uploadPoints = new();

    /// <summary>Velocidad de bajada actual formateada.</summary>
    [ObservableProperty]
    private string _currentDownloadText = "0 B/s";

    /// <summary>Velocidad de subida actual formateada.</summary>
    [ObservableProperty]
    private string _currentUploadText = "0 B/s";

    /// <summary>Pico de bajada formateado.</summary>
    [ObservableProperty]
    private string _peakDownloadText = "0 B/s";

    /// <summary>Pico de subida formateado.</summary>
    [ObservableProperty]
    private string _peakUploadText = "0 B/s";

    /// <summary>Numero de transferencias activas.</summary>
    [ObservableProperty]
    private int _activeTransfers;

    /// <summary>Escala maxima del eje Y formateada (auto-ajustable).</summary>
    [ObservableProperty]
    private string _axisMaxText = "0 B/s";

    /// <summary>Etiqueta de estado (montado / detenido).</summary>
    [ObservableProperty]
    private string _statusText = string.Empty;

    /// <summary>
    /// DIAGNOSTICO: linea de estado tecnico que expone el puerto RC, si el
    /// bucle de sondeo esta activo, cuantos ciclos se han completado y el
    /// ultimo error. Permite ver EN LA UI por que no llegan datos.
    /// </summary>
    [ObservableProperty]
    private string _diagnosticText = string.Empty;

    /// <summary>Indica si hay datos suficientes para dibujar la grafica.</summary>
    [ObservableProperty]
    private bool _hasData;

    /// <summary>Numero de muestras actualmente en el historial.</summary>
    [ObservableProperty]
    private int _sampleCount;

    /// <summary>
    /// Ancho del area de dibujo. La vista lo actualiza en SizeChanged para
    /// que la normalizacion use el tamano real del lienzo.
    /// </summary>
    public double ChartWidth
    {
        get => _chartWidth;
        set
        {
            if (SetProperty(ref _chartWidth, value))
            {
                Rebuild();
            }
        }
    }

    /// <summary>
    /// Alto del area de dibujo. La vista lo actualiza en SizeChanged para
    /// que la normalizacion use el tamano real del lienzo.
    /// </summary>
    public double ChartHeight
    {
        get => _chartHeight;
        set
        {
            if (SetProperty(ref _chartHeight, value))
            {
                Rebuild();
            }
        }
    }

    /// <summary>Etiqueta localizada con fallback.</summary>
    private string L(string key, string fallback) =>
        _localization?.Get(key, fallback) ?? fallback;

    private void OnHistoryChanged(object? sender, NotifyCollectionChangedEventArgs e) => RebuildOnUiThread();

    private void OnSourcePropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        // Solo nos interesan los cambios que afectan a la grafica o a las
        // etiquetas de cabecera.
        switch (e.PropertyName)
        {
            case nameof(RemoteCardViewModel.PeakDownloadBytesPerSecond):
            case nameof(RemoteCardViewModel.PeakUploadBytesPerSecond):
            case nameof(RemoteCardViewModel.DownloadBytesPerSecond):
            case nameof(RemoteCardViewModel.UploadBytesPerSecond):
            case nameof(RemoteCardViewModel.ActiveTransfers):
            case nameof(RemoteCardViewModel.State):
            // DIAGNOSTICO: refrescamos la linea tecnica cuando cambia el
            // puerto RC, el estado del bucle o el ultimo error.
            case nameof(RemoteCardViewModel.RcPort):
            case nameof(RemoteCardViewModel.IsTelemetryRunning):
            case nameof(RemoteCardViewModel.TelemetryTickCount):
            case nameof(RemoteCardViewModel.TelemetrySuccessCount):
            case nameof(RemoteCardViewModel.LastTelemetryError):
                RebuildOnUiThread();
                break;
        }
    }

    /// <summary>
    /// BUGFIX Monitor de Trafico: garantiza que <see cref="Rebuild"/> se
    /// ejecute en el hilo de UI. Aunque el bucle de telemetria ya marshalea
    /// sus actualizaciones, esta salvaguarda evita que una mutacion de
    /// <see cref="PointCollection"/> (Freezable) fuera del hilo de UI provoque
    /// una excepcion silenciosa que dejaria la grafica congelada.
    /// </summary>
    private void RebuildOnUiThread()
    {
        var dispatcher = Application.Current?.Dispatcher;

        if (dispatcher is null || dispatcher.CheckAccess())
        {
            Rebuild();
            return;
        }

        dispatcher.BeginInvoke(new Action(Rebuild));
    }

    /// <summary>
    /// Recalcula las series normalizadas y las etiquetas. Se ejecuta en el
    /// hilo de UI (el historial solo muta desde el dispatcher).
    /// </summary>
    private void Rebuild()
    {
        var samples = _source.TrafficHistory;

        SampleCount = samples.Count;
        HasData = samples.Count >= 2;

        CurrentDownloadText = FormatSpeed(_source.DownloadBytesPerSecond);
        CurrentUploadText = FormatSpeed(_source.UploadBytesPerSecond);
        PeakDownloadText = FormatSpeed(_source.PeakDownloadBytesPerSecond);
        PeakUploadText = FormatSpeed(_source.PeakUploadBytesPerSecond);
        ActiveTransfers = _source.ActiveTransfers;
        StatusText = _source.IsMounted
            ? L("Str_TrafficLive", "LIVE")
            : L("Str_TrafficIdle", "IDLE");

        // DIAGNOSTICO: componemos la linea tecnica que expone EN LA UI el
        // puerto RC consultado, si el bucle gira, cuantos ciclos han tenido
        // exito y el ultimo error. Es la herramienta clave para ver por que
        // la grafica se queda en "0 B/s".
        DiagnosticText = string.Format(
            CultureInfo.InvariantCulture,
            "RC POST http://127.0.0.1:{0}/core/stats | loop={1} | ticks={2} | ok={3} | samples={4}{5}",
            _source.RcPort,
            _source.IsTelemetryRunning ? "ON" : "OFF",
            _source.TelemetryTickCount,
            _source.TelemetrySuccessCount,
            samples.Count,
            string.IsNullOrWhiteSpace(_source.LastTelemetryError)
                ? string.Empty
                : " | " + _source.LastTelemetryError);

        if (!HasData)
        {
            DownloadPoints = new PointCollection();
            UploadPoints = new PointCollection();
            AxisMaxText = "0 B/s";
            return;
        }

        // Escala auto-ajustable: el maximo entre ambos picos y las muestras
        // actuales, con un 10% de holgura para que el pico no toque el borde.
        var maxValue = 0.0;

        foreach (var sample in samples)
        {
            if (sample.DownloadBytesPerSecond > maxValue)
            {
                maxValue = sample.DownloadBytesPerSecond;
            }

            if (sample.UploadBytesPerSecond > maxValue)
            {
                maxValue = sample.UploadBytesPerSecond;
            }
        }

        if (maxValue <= 0)
        {
            maxValue = 1; // evita division por cero con trafico nulo
        }

        var scaleMax = maxValue * 1.1;
        AxisMaxText = FormatSpeed(scaleMax);

        var usableWidth = Math.Max(1.0, _chartWidth - (ChartPadding * 2));
        var usableHeight = Math.Max(1.0, _chartHeight - (ChartPadding * 2));

        // El eje X se reparte entre las muestras disponibles. Si aun no se
        // ha llenado el buffer, las muestras ocupan todo el ancho igualmente
        // (grafica "elastica").
        var step = samples.Count > 1
            ? usableWidth / (samples.Count - 1)
            : usableWidth;

        var download = new PointCollection(samples.Count);
        var upload = new PointCollection(samples.Count);

        for (var i = 0; i < samples.Count; i++)
        {
            var x = ChartPadding + (step * i);

            var downY = ChartPadding + usableHeight -
                        (samples[i].DownloadBytesPerSecond / scaleMax * usableHeight);

            var upY = ChartPadding + usableHeight -
                      (samples[i].UploadBytesPerSecond / scaleMax * usableHeight);

            download.Add(new Point(x, Clamp(downY, ChartPadding, ChartPadding + usableHeight)));
            upload.Add(new Point(x, Clamp(upY, ChartPadding, ChartPadding + usableHeight)));
        }

        DownloadPoints = download;
        UploadPoints = upload;
    }

    private static double Clamp(double value, double min, double max) =>
        value < min ? min : value > max ? max : value;

    /// <summary>Formatea bytes/segundo a una cadena legible (B/s, KB/s, MB/s, GB/s).</summary>
    private static string FormatSpeed(double bytesPerSecond)
    {
        if (bytesPerSecond <= 0)
        {
            return "0 B/s";
        }

        string[] units = { "B/s", "KB/s", "MB/s", "GB/s", "TB/s" };
        var value = bytesPerSecond;
        var unit = 0;

        while (value >= 1024 && unit < units.Length - 1)
        {
            value /= 1024;
            unit++;
        }

        return unit == 0
            ? string.Format(CultureInfo.InvariantCulture, "{0:0} {1}", value, units[unit])
            : string.Format(CultureInfo.InvariantCulture, "{0:0.#} {1}", value, units[unit]);
    }

    /// <summary>Re-notifica las etiquetas dependientes del idioma.</summary>
    public void RefreshLocalization()
    {
        OnPropertyChanged(nameof(RemoteName));
        OnPropertyChanged(nameof(DriveLabel));
        Rebuild();
    }

    public void Dispose()
    {
        _source.TrafficHistory.CollectionChanged -= OnHistoryChanged;
        _source.PropertyChanged -= OnSourcePropertyChanged;
    }
}
