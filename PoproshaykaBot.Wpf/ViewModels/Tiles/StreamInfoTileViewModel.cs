using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MahApps.Metro.IconPacks;
using Microsoft.Extensions.Logging;
using PoproshaykaBot.Core.Infrastructure.Events;
using PoproshaykaBot.Core.Infrastructure.Events.Streaming;
using PoproshaykaBot.Core.Streaming;
using PoproshaykaBot.Wpf.Infrastructure;
using System.Windows.Media.Imaging;
using PoproshaykaBot.Wpf.Bootstrap;

namespace PoproshaykaBot.Wpf.ViewModels.Tiles;

public sealed partial class StreamInfoTileViewModel : DashboardTileViewModel, IDisposable
{
    private readonly IStreamStatus _stream;
    private readonly ILogger<StreamInfoTileViewModel> _logger;
    private readonly List<IDisposable> _subs = [];
    private readonly IUiTimer _refreshTimer;
    private readonly IShellLauncher _shellLauncher;
    private readonly ToolbarItemViewModel _openChannelToolbarItem;
    private string? _lastThumbnailRawUrl;
    private bool _refreshTimerRunning;

    [ObservableProperty]
    private StreamStatus _status = StreamStatus.Unknown;

    [ObservableProperty]
    private string _streamTitle = string.Empty;

    [ObservableProperty]
    private string _game = "–";

    [ObservableProperty]
    private int _viewerCount;

    [ObservableProperty]
    private TimeSpan _uptime = TimeSpan.Zero;

    [ObservableProperty]
    private BitmapImage? _thumbnail;

    [ObservableProperty]
    private string? _channelLogin;

    [ObservableProperty]
    private bool _isRefreshing;

    [ObservableProperty]
    private DateTime _lastUpdated = DateTime.Now;

    public StreamInfoTileViewModel(
        IStreamStatus stream,
        IEventBus bus,
        ILogger<StreamInfoTileViewModel> logger,
        IUiDispatcher uiDispatcher,
        IShellLauncher shellLauncher)
        : base("stream-info", "Информация о стриме", maxWidth: 420, maxHeight: 400, minHeight: 214)
    {
        _stream = stream;
        _logger = logger;
        _shellLauncher = shellLauncher;

        _refreshTimer = uiDispatcher.CreateTimer(TimeSpan.FromSeconds(30), OnRefreshTimerTick);

        _openChannelToolbarItem = new ToolbarItemViewModel(
            PackIconLucideKind.ExternalLink,
            OpenChannelCommand,
            toolTip: "Открыть канал на Twitch")
        {
            IsVisible = false,
        };

        HeaderActions.Add(new ToolbarItemViewModel(
            PackIconLucideKind.RefreshCw,
            RefreshCommand,
            toolTip: "Обновить данные стрима"));
        HeaderActions.Add(_openChannelToolbarItem);

        _subs.Add(bus.SubscribeOnUi<StreamWentOnline>(_ => ApplyCurrentStatus()));
        _subs.Add(bus.SubscribeOnUi<StreamWentOffline>(_ => ApplyCurrentStatus()));
        _subs.Add(bus.SubscribeOnUi<StreamMetadataResolved>(_ => ApplyCurrentStatus()));
        _subs.Add(bus.SubscribeOnUi<ChannelUpdated>(_ => ApplyCurrentStatus()));

        ApplyCurrentStatus();
    }

    public override PackIconLucideKind Icon => PackIconLucideKind.Radio;

    public override bool SizesToContent => true;

    [RelayCommand(CanExecute = nameof(CanRefresh))]
    private async Task RefreshAsync()
    {
        IsRefreshing = true;
        RefreshCommand.NotifyCanExecuteChanged();
        try
        {
            await _stream.RefreshLiveSnapshotAsync();
            ApplyCurrentStatus();
        }
        catch (Exception ex)
        {
            _logger.StreamInfoUpdateFailed(ex);
        }
        finally
        {
            IsRefreshing = false;
            RefreshCommand.NotifyCanExecuteChanged();
        }
    }

    private bool CanRefresh() => !IsRefreshing;

    [RelayCommand]
    private void OpenChannel()
    {
        if (string.IsNullOrWhiteSpace(ChannelLogin))
        {
            return;
        }

        _shellLauncher.Open($"https://twitch.tv/{ChannelLogin}");
    }

    private void ApplyCurrentStatus()
    {
        var info = _stream.CurrentStream;
        Status = _stream.CurrentStatus;
        LastUpdated = DateTime.Now;

        if (Status == StreamStatus.Online && info is { IsBare: false })
        {
            StreamTitle = info.Title;
            Game = string.IsNullOrWhiteSpace(info.GameName) ? "–" : info.GameName;
            ViewerCount = info.ViewerCount;

            var elapsed = DateTime.UtcNow - info.StartedAt;
            Uptime = elapsed < TimeSpan.Zero ? TimeSpan.Zero : elapsed;

            LoadThumbnail(info.ThumbnailUrl);

            ChannelLogin = info.UserLogin;
            _openChannelToolbarItem.IsVisible = !string.IsNullOrWhiteSpace(info.UserLogin);
        }
        else
        {
            StreamTitle = Status switch
            {
                StreamStatus.Offline => "Стрим завершён",
                StreamStatus.Online => "Загрузка данных...",
                _ => "Статус не определён",
            };
            Game = "–";
            ViewerCount = 0;
            Uptime = TimeSpan.Zero;
            Thumbnail = null;
            _lastThumbnailRawUrl = null;
            ChannelLogin = null;
            _openChannelToolbarItem.IsVisible = false;
        }

        UpdateTimer();
    }

    private void UpdateTimer()
    {
        if (Status == StreamStatus.Online)
        {
            if (!_refreshTimerRunning)
            {
                _refreshTimer.Start();
                _refreshTimerRunning = true;
            }
        }
        else if (_refreshTimerRunning)
        {
            _refreshTimer.Stop();
            _refreshTimerRunning = false;
        }
    }

    private async void OnRefreshTimerTick()
    {
        if (_stream.CurrentStatus != StreamStatus.Online)
        {
            return;
        }

        try
        {
            await _stream.RefreshLiveSnapshotAsync();
            ApplyCurrentStatus();
        }
        catch (Exception ex)
        {
            _logger.StreamInfoAutoUpdateFailed(ex);
        }
    }

    private void LoadThumbnail(string raw)
    {
        if (string.IsNullOrEmpty(raw))
        {
            Thumbnail = null;
            _lastThumbnailRawUrl = null;
            return;
        }

        if (raw == _lastThumbnailRawUrl)
        {
            return;
        }

        _lastThumbnailRawUrl = raw;

        const int decodeWidth = 320;
        var height = (int)Math.Round(decodeWidth * 9.0 / 16.0);
        var resolved = raw
            .Replace("{width}", decodeWidth.ToString())
            .Replace("{height}", height.ToString());

        var uri = new Uri(resolved, UriKind.Absolute);
        var bmp = new BitmapImage();
        bmp.BeginInit();
        bmp.UriSource = uri;
        bmp.DecodePixelWidth = decodeWidth;
        bmp.CreateOptions = BitmapCreateOptions.None;
        bmp.CacheOption = BitmapCacheOption.Default;
        bmp.EndInit();
        Thumbnail = bmp;
    }

    public void Dispose()
    {
        _refreshTimer.Stop();
        _refreshTimerRunning = false;

        foreach (var sub in _subs)
        {
            sub.Dispose();
        }
        _subs.Clear();
    }
}
