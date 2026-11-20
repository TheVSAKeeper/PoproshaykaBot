using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PoproshaykaBot.Core.Platforms;
using PoproshaykaBot.Wpf.Infrastructure.Platforms;
using System.Windows.Input;

namespace PoproshaykaBot.Wpf.ViewModels.Tiles;

public sealed partial class ChatDisplayPaneViewModel(IShellLauncher shellLauncher, IChatPlatformRegistry? platformRegistry) : ObservableObject
{
    [ObservableProperty]
    private bool _isShown;

    [ObservableProperty]
    private bool _hasFallback;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(FallbackActionCommand), nameof(FallbackActionText))]
    private bool _runtimeMissing;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(FallbackActionCommand), nameof(FallbackActionText))]
    private Uri? _externalUri;

    [ObservableProperty]
    private string _fallbackHeading = string.Empty;

    [ObservableProperty]
    private string _fallbackDescription = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DisplayName))]
    private StreamingPlatform _platform;

    public string DisplayName => PlatformVisuals.GetDisplayName(Platform, platformRegistry);
    public Uri? ChannelUri { get; private set; }

    public ICommand? FallbackActionCommand => RuntimeMissing ? OpenRuntimeDownloadCommand
        : ExternalUri is not null ? OpenInBrowserCommand : null;

    public string? FallbackActionText => RuntimeMissing ? "Скачать WebView2 Runtime"
        : ExternalUri is not null ? "Открыть в браузере" : null;

    internal bool SetTarget(StreamingPlatform platform, Uri? channelUri, Uri? externalUri, bool shown)
    {
        ExternalUri = externalUri;
        var changed = IsShown != shown || Platform != platform || ChannelUri != channelUri;
        IsShown = shown;
        Platform = platform;
        ChannelUri = channelUri;
        if (changed && shown)
        {
            HasFallback = channelUri is null;
            RuntimeMissing = false;
            if (channelUri is null)
            {
                var twitch = platform == StreamingPlatform.Twitch;
                FallbackHeading = twitch ? "Канал не указан" : "Чат эфира недоступен";
                FallbackDescription = twitch ? "Укажите канал в настройках Twitch, чтобы открыть чат."
                    : "Чат появится, когда у выбранного канала начнётся эфир с доступным чатом.";
            }
        }

        return changed;
    }

    internal void ShowRuntimeMissingFallback()
    {
        HasFallback = true;
        RuntimeMissing = true;
        FallbackHeading = "Требуется Microsoft Edge WebView2 Runtime";
        FallbackDescription = "Установите его, чтобы открыть чат в приложении.";
    }

    internal void ShowGenericFallback()
    {
        HasFallback = true;
        RuntimeMissing = false;
        FallbackHeading = $"Не удалось открыть чат {DisplayName}";
        FallbackDescription = ExternalUri is not null ? "Откройте чат в браузере или попробуйте обновить его."
            : "Попробуйте обновить чат. Подробности – в журнале.";
    }

    [RelayCommand]
    private void OpenInBrowser()
    {
        if (ExternalUri is not null)
        {
            shellLauncher.Open(ExternalUri.AbsoluteUri);
        }
    }

    [RelayCommand]
    private void OpenRuntimeDownload() => shellLauncher.Open("https://go.microsoft.com/fwlink/p/?LinkId=2124703");
}
