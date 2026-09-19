using System;
using System.Diagnostics;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using RcloneCommanderAdvanced.Services.Abstractions;

namespace RcloneCommanderAdvanced.ViewModels;

/// <summary>
/// ViewModel de la ventana de pre-flight (Fase 1).
///
/// Se muestra cuando falta alguna dependencia critica (rclone.exe o WinFsp)
/// y ofrece botones para abrir la web oficial de descarga correspondiente.
/// No contiene logica de montaje: es puramente informativa y de accion.
/// </summary>
public sealed partial class DependencyWarningViewModel : ObservableObject
{
    private readonly ILocalizationService _localization;

    public DependencyWarningViewModel(ILocalizationService localization)
    {
        _localization = localization;
    }

    /// <summary>True si falta rclone.exe.</summary>
    [ObservableProperty]
    private bool _isRcloneMissing;

    /// <summary>True si falta WinFsp.</summary>
    [ObservableProperty]
    private bool _isWinFspMissing;

    /// <summary>Mensaje resumido del problema detectado.</summary>
    [ObservableProperty]
    private string _summary = string.Empty;

    /// <summary>URL de descarga de rclone (segun el idioma activo).</summary>
    public string RcloneDownloadUrl => "https://rclone.org/downloads/";

    /// <summary>URL de descarga de WinFsp.</summary>
    public string WinFspDownloadUrl => "https://winfsp.dev/rel/";

    /// <summary>Abre la pagina de descarga de rclone en el navegador.</summary>
    [RelayCommand]
    private void OpenRcloneDownload() => OpenUrl(RcloneDownloadUrl);

    /// <summary>Abre la pagina de descarga de WinFsp en el navegador.</summary>
    [RelayCommand]
    private void OpenWinFspDownload() => OpenUrl(WinFspDownloadUrl);

    /// <summary>Reconstruye el mensaje resumen a partir del estado actual.</summary>
    public void RefreshSummary()
    {
        if (IsRcloneMissing && IsWinFspMissing)
        {
            Summary = _localization.Get(
                "Str_DependencyMissingBoth",
                "rclone.exe and WinFsp are both missing. Install them to continue.");
        }
        else if (IsRcloneMissing)
        {
            Summary = _localization.Get(
                "Str_DependencyMissingRclone",
                "rclone.exe was not found. Install it to continue.");
        }
        else if (IsWinFspMissing)
        {
            Summary = _localization.Get(
                "Str_DependencyMissingWinFsp",
                "WinFsp was not found. It is required to mount drives.");
        }
        else
        {
            Summary = string.Empty;
        }
    }

    private static void OpenUrl(string url)
    {
        try
        {
            Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
        }
        catch (Exception)
        {
            // Si no se puede abrir el navegador, no hay nada mas que hacer aqui.
        }
    }
}
