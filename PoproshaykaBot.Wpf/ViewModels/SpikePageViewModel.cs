using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PoproshaykaBot.Core.Settings;

namespace PoproshaykaBot.Wpf.ViewModels;

public sealed partial class SpikePageViewModel : ObservableObject, IPageHeader
{
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsLight))]
    [NotifyPropertyChangedFor(nameof(IsDark))]
    private string _currentTheme = "light";

    public SpikePageViewModel(SettingsManager settings)
    {
        var twitch = settings.Current.Twitch;

        CoreStatus = string.IsNullOrWhiteSpace(twitch.ClientId)
            ? $"Core DI поднят. ClientId не настроен, HTTP-порт {twitch.HttpServerPort}."
            : $"Core DI поднят. ClientId настроен, HTTP-порт {twitch.HttpServerPort}.";
    }

    public string PageTitle => "Обзор (WPF spike)";

    public string? PageDescription => "Каркас KeepShell поверх неизменного PoproshaykaBot.Core.";

    public string CoreStatus { get; }

    public bool IsLight => string.Equals(CurrentTheme, "light", StringComparison.OrdinalIgnoreCase);

    public bool IsDark => string.Equals(CurrentTheme, "dark", StringComparison.OrdinalIgnoreCase);

    [RelayCommand]
    private void ApplyTheme(string key)
    {
        ThemeManager.Apply(key);
        CurrentTheme = key;
    }
}
