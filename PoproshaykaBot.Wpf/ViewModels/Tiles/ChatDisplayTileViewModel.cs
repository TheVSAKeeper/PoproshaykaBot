using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using KeepShell.ViewModels;
using MahApps.Metro.IconPacks;
using Microsoft.Extensions.Logging;
using PoproshaykaBot.Core.Infrastructure;
using PoproshaykaBot.Core.Settings;
using PoproshaykaBot.Core.Twitch.Auth;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Windows.Input;

namespace PoproshaykaBot.Wpf.ViewModels.Tiles;

public sealed partial class ChatDisplayTileViewModel : DashboardTileViewModel
{
    public const string HideClutterScript = """
                                            (function () {
                                                const selectors = [
                                                    '[data-a-target="consent-banner"]',
                                                    '.consent-banner',
                                                    '.tw-callout-message',
                                                    '[class*="channelLeaderboardHeader"]',
                                                    '[class*="channelLeaderboardBottomIconContainer"]',
                                                    '[class*="community-highlight"]',
                                                ];
                                                const wrapperClasses = ['tw-transition', 'tw-callout', 'consent-banner'];
                                                const findWrapper = (el) => {
                                                    let node = el;
                                                    for (let i = 0; i < 15 && node; i++) {
                                                        if (node.classList && wrapperClasses.some((c) => node.classList.contains(c))) {
                                                            return node;
                                                        }
                                                        node = node.parentElement;
                                                    }
                                                    return el;
                                                };
                                                const hideAll = () => {
                                                    const consentAccept = document.querySelector('[data-a-target="consent-banner-accept"]');
                                                    if (consentAccept) {
                                                        consentAccept.click();
                                                    }
                                                    for (const selector of selectors) {
                                                        document.querySelectorAll(selector).forEach((el) => {
                                                            const wrapper = findWrapper(el);
                                                            if (wrapper.dataset.poproshaykaHidden !== '1') {
                                                                wrapper.dataset.poproshaykaHidden = '1';
                                                                wrapper.style.setProperty('display', 'none', 'important');
                                                            }
                                                        });
                                                    }
                                                };
                                                hideAll();
                                                if (document.readyState === 'loading') {
                                                    document.addEventListener('DOMContentLoaded', hideAll, { once: true });
                                                }
                                                const startObserver = () => {
                                                    if (document.documentElement) {
                                                        new MutationObserver(hideAll).observe(document.documentElement, { childList: true, subtree: true });
                                                    } else {
                                                        setTimeout(startObserver, 0);
                                                    }
                                                };
                                                startObserver();
                                            })();
                                            """;

    private const string WebView2RuntimeDownloadUrl = "https://go.microsoft.com/fwlink/p/?LinkId=2124703";
    private const double DefaultZoom = 0.75;
    private const double MinZoom = 0.25;
    private const double MaxZoom = 5.0;

    private static readonly string ZoomFilePath = AppPaths.Combine("chat-zoom.txt");

    private readonly SettingsManager _settings;
    private readonly IShellLauncher _shellLauncher;

    private readonly ToolbarItemViewModel _reloadAction;
    private readonly ToolbarItemViewModel _resetZoomAction;
    private readonly ToolbarItemViewModel _resetSessionAction;

    [ObservableProperty]
    private double _zoomFactor;

    [ObservableProperty]
    private bool _hasFallback;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(FallbackActionCommand))]
    private bool _showRuntimeDownload;

    [ObservableProperty]
    private string _fallbackHeading = string.Empty;

    [ObservableProperty]
    private string _fallbackDescription = string.Empty;

    public ChatDisplayTileViewModel(SettingsManager settings, ILogger<ChatDisplayTileViewModel> logger, IShellLauncher shellLauncher)
        : base("twitch-chat", "Чат")
    {
        _settings = settings;
        _shellLauncher = shellLauncher;
        Logger = logger;

        var twitch = settings.Current.Twitch;
        UserDataFolder = WebView2UserDataFolders.Resolve(twitch.ChatDisplayAccount);
        AccountLabel = twitch.ChatDisplayAccount == TwitchOAuthRole.Broadcaster ? "стримера" : "бота";

        var channel = twitch.Channel?.Trim();

        if (!string.IsNullOrEmpty(channel))
        {
            ChannelUri = new Uri($"https://www.twitch.tv/popout/{Uri.EscapeDataString(channel)}/chat?popout=");
        }

        _zoomFactor = LoadSavedZoom();

        _reloadAction = new(PackIconLucideKind.RefreshCw, ReloadCommand, "Перезагрузить страницу чата");
        _resetZoomAction = new(PackIconLucideKind.Search, ResetZoomCommand, "Сбросить масштаб к значению по умолчанию");
        _resetSessionAction = new(PackIconLucideKind.Trash2, ResetSessionCommand, "Очистить куки и кэш активного аккаунта (бот или стример)");

        HeaderActions.Add(_reloadAction);
        HeaderActions.Add(_resetZoomAction);
        HeaderActions.Add(new(PackIconLucideKind.ExternalLink, OpenInBrowserCommand, "Открыть текущую страницу в системном браузере"));
        HeaderActions.Add(_resetSessionAction);
    }

    public event Action? ReloadRequested;

    public event Action? ResetSessionRequested;

    public ILogger Logger { get; }

    public string UserDataFolder { get; }

    public string AccountLabel { get; }

    public Uri? ChannelUri { get; }

    public ICommand? FallbackActionCommand => ShowRuntimeDownload ? OpenRuntimeDownloadCommand : null;

    public void ShowNoChannelFallback()
    {
        HasFallback = true;
        ShowRuntimeDownload = false;
        FallbackHeading = "Канал не указан";
        FallbackDescription = "Укажите канал в настройках Twitch, чтобы открыть чат.";
    }

    public void ShowRuntimeMissingFallback()
    {
        HasFallback = true;
        ShowRuntimeDownload = true;
        FallbackHeading = "Требуется Microsoft Edge WebView2 Runtime";
        FallbackDescription = "Установите его, чтобы увидеть чат Twitch.";
        DisableWebViewActions();
    }

    public void ShowGenericFallback()
    {
        HasFallback = true;
        ShowRuntimeDownload = false;
        FallbackHeading = "Не удалось открыть чат Twitch";
        FallbackDescription = "Подробности – в логах.";
        DisableWebViewActions();
    }

    partial void OnZoomFactorChanged(double value)
    {
        SaveZoom(value);
    }

    private void DisableWebViewActions()
    {
        _reloadAction.IsEnabled = false;
        _resetZoomAction.IsEnabled = false;
        _resetSessionAction.IsEnabled = false;
    }

    [RelayCommand]
    private void Reload()
    {
        ReloadRequested?.Invoke();
    }

    [RelayCommand]
    private void ResetZoom()
    {
        ZoomFactor = DefaultZoom;
    }

    [RelayCommand]
    private void ResetSession()
    {
        ResetSessionRequested?.Invoke();
    }

    [RelayCommand]
    private void OpenInBrowser()
    {
        if (ChannelUri is null)
        {
            return;
        }

        _shellLauncher.Open(ChannelUri.ToString());
    }

    [RelayCommand]
    private void OpenRuntimeDownload()
    {
        _shellLauncher.Open(WebView2RuntimeDownloadUrl);
    }

    private double LoadSavedZoom()
    {
        try
        {
            if (!File.Exists(ZoomFilePath))
            {
                return DefaultZoom;
            }

            var text = File.ReadAllText(ZoomFilePath).Trim();

            if (double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var zoom)
                && zoom >= MinZoom && zoom <= MaxZoom)
            {
                return zoom;
            }
        }
        catch (Exception exception)
        {
            Logger.LogWarning(exception, "Не удалось прочитать сохранённый масштаб чата");
        }

        return DefaultZoom;
    }

    private void SaveZoom(double zoom)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(ZoomFilePath)!);
            File.WriteAllText(ZoomFilePath, zoom.ToString("F3", CultureInfo.InvariantCulture));
        }
        catch (Exception exception)
        {
            Logger.LogWarning(exception, "Не удалось сохранить масштаб чата");
        }
    }
}
