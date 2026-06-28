using System;
using CommunityToolkit.Mvvm.ComponentModel;

namespace PoproshaykaBot.Wpf.ViewModels.Tiles;

public sealed partial class ObsSourceMeterViewModel : ObservableObject
{
    [ObservableProperty]
    private string _displayName = "—";

    [ObservableProperty]
    private string _detail = string.Empty;

    [ObservableProperty]
    private string? _statusSeverity;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(LevelSeverity))]
    private double _level;

    public ObsSourceMeterViewModel(string sourceName)
    {
        SourceName = sourceName;
        DisplayName = string.IsNullOrWhiteSpace(sourceName) ? "—" : sourceName.Trim();
    }

    public string SourceName { get; }

    public bool Muted { get; set; }

    public bool Found { get; set; }

    public string? LevelSeverity => Level switch
    {
        >= 0.9D => "Error",
        >= 0.68D => "Warning",
        _ => "Success",
    };

    public void ShowActive(string displayName, double? volumeDecibels)
    {
        SetName(displayName);
        StatusSeverity = "Success";
        Detail = volumeDecibels.HasValue
            ? $"включён · {volumeDecibels.Value:0.#} дБ"
            : "включён";
    }

    public void ShowMuted(string displayName, double? volumeDecibels)
    {
        SetName(displayName);
        StatusSeverity = "Error";
        Detail = volumeDecibels.HasValue
            ? $"выключен · {volumeDecibels.Value:0.#} дБ"
            : "выключен";

        Level = 0D;
    }

    public void ShowMissing(string displayName)
    {
        SetName(displayName);
        StatusSeverity = "Warning";
        Detail = "источник не найден";
        Level = 0D;
    }

    public void ApplyLevel(double level)
    {
        Level = Math.Clamp(level, 0D, 1D);
    }

    private void SetName(string displayName)
    {
        DisplayName = string.IsNullOrWhiteSpace(displayName) ? "—" : displayName.Trim();
    }
}
