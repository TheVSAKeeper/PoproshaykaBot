using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using KeepShell.ViewModels;
using MahApps.Metro.IconPacks;
using Microsoft.Extensions.Logging;
using PoproshaykaBot.Core.Infrastructure.Events;
using PoproshaykaBot.Core.Infrastructure.Events.Lifecycle;
using PoproshaykaBot.Core.Infrastructure.Events.Streaming;
using PoproshaykaBot.Core.Platforms;
using PoproshaykaBot.Core.Settings.Stores;
using PoproshaykaBot.Core.Streaming;
using PoproshaykaBot.Wpf.Bootstrap;
using PoproshaykaBot.Wpf.Infrastructure;
using PoproshaykaBot.Wpf.Infrastructure.Platforms;
using System.Globalization;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace PoproshaykaBot.Wpf.ViewModels.Tiles;

public sealed partial class StreamPreviewTileViewModel : DashboardTileViewModel, IDisposable
{
    public static readonly TimeSpan AutoRefreshInterval = TimeSpan.FromMinutes(5);

    private const int MinDecodeWidth = 320;
    private const int MaxDecodeWidth = 1920;
    private const int DecodeWidthStep = 64;
    private const double GrowthFactor = 1.5;

    private readonly IChatPlatformRegistry _platformRegistry;
    private readonly TilePlatformSelection _platformSelection;
    private readonly ILogger<StreamPreviewTileViewModel> _logger;
    private readonly IUiDispatcher _uiDispatcher;
    private readonly IReadOnlyList<IPlatformEnablementSource> _enablementSources;
    private readonly IUiTimer _refreshTimer;
    private readonly ToolbarItemViewModel _switchPlatformToolbarItem;
    private readonly List<IDisposable> _subs = [];
    private string? _lastThumbnailRawUrl;
    private BitmapImage? _pending;
    private long _cacheStamp;
    private bool _isOnScreen;
    private bool _refreshDue;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SelectedPlatformName))]
    private StreamingPlatform _selectedPlatform;

    [ObservableProperty]
    private bool _hasMultiplePlatforms;

    public ObservableCollection<StreamPreviewPlatformTabViewModel> PlatformTabs { get; } = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ContentFillsTile))]
    private BitmapImage? _thumbnail;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(EmptyHeading), nameof(EmptyDescription))]
    [NotifyCanExecuteChangedFor(nameof(RefreshFrameCommand))]
    private bool _isLoading;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(EmptyHeading), nameof(EmptyDescription))]
    private bool _loadFailed;

    public StreamPreviewTileViewModel(
        IChatPlatformRegistry platformRegistry,
        IEventBus bus,
        ILogger<StreamPreviewTileViewModel> logger,
        IUiDispatcher uiDispatcher,
        YouTubeSettingsStore? youtubeSettingsStore = null,
        IEnumerable<IPlatformEnablementSource>? enablementSources = null)
        : base("stream-preview", "Превью стрима", maxHeight: 280)
    {
        ArgumentNullException.ThrowIfNull(platformRegistry);
        ArgumentNullException.ThrowIfNull(bus);
        ArgumentNullException.ThrowIfNull(uiDispatcher);

        _platformRegistry = platformRegistry;
        _platformSelection = new TilePlatformSelection(platformRegistry);
        _logger = logger;
        _uiDispatcher = uiDispatcher;
        _enablementSources = MergeEnablementSources(enablementSources, youtubeSettingsStore);
        _refreshTimer = uiDispatcher.CreateTimer(AutoRefreshInterval, OnRefreshTimerTick);

        _switchPlatformToolbarItem = new ToolbarItemViewModel(
            PackIconLucideKind.ArrowLeftRight,
            SwitchPlatformCommand,
            toolTip: "Сменить площадку в превью стрима")
        {
            IsVisible = _platformSelection.HasMultiplePlatforms,
        };

        HeaderActions.Add(new ToolbarItemViewModel(
            PackIconLucideKind.RefreshCw,
            RefreshFrameCommand,
            toolTip: "Обновить кадр"));
        HeaderActions.Add(_switchPlatformToolbarItem);

        _subs.Add(bus.SubscribeOnUi<StreamWentOnline>(_ => OnPlatformStateChanged()));
        _subs.Add(bus.SubscribeOnUi<StreamWentOffline>(_ => OnPlatformStateChanged()));
        _subs.Add(bus.SubscribeOnUi<StreamMetadataResolved>(_ => OnPlatformStateChanged()));
        _subs.Add(bus.SubscribeOnUi<ChannelUpdated>(_ => OnPlatformStateChanged()));
        _subs.Add(bus.SubscribeOnUi<PlatformConnectionChanged>(_ => OnPlatformStateChanged()));
        _subs.Add(bus.SubscribeOnUi<YouTubeActiveBroadcastChanged>(_ => OnPlatformStateChanged()));

        foreach (var source in _enablementSources)
        {
            source.Changed += OnEnablementSourceChanged;
        }

        ApplyCurrentStatus();
    }

    public override PackIconLucideKind Icon => PackIconLucideKind.Image;

    public override bool SizesToContent => true;

    public override bool ContentFillsTile => Thumbnail is not null;

    public string SelectedPlatformName => _platformSelection.SelectedDisplayName;

    public int DecodeWidth { get; private set; } = MinDecodeWidth;

    public string EmptyHeading => (IsLoading, LoadFailed) switch
    {
        (true, _) => "Загружаю кадр",
        (_, true) => "Кадр не загрузился",
        _ => "Нет превью",
    };

    public string EmptyDescription => (IsLoading, LoadFailed) switch
    {
        (true, _) => "Кадр стрима появится через несколько секунд.",
        (_, true) => "Проверьте подключение к интернету и нажмите «Обновить кадр» в шапке плитки.",
        _ => "Кадр появится, когда канал выйдет в эфир.",
    };

    public void FitTo(double pixelWidth)
    {
        if (!double.IsFinite(pixelWidth) || pixelWidth <= DecodeWidth || DecodeWidth >= MaxDecodeWidth)
        {
            return;
        }

        var wanted = Math.Max(pixelWidth, DecodeWidth * GrowthFactor);
        var rounded = (int)Math.Ceiling(wanted / DecodeWidthStep) * DecodeWidthStep;

        DecodeWidth = Math.Min(rounded, MaxDecodeWidth);

        if (_lastThumbnailRawUrl is { } raw && !IsLoading)
        {
            Decode(raw);
        }
    }

    public void SetOnScreen(bool isOnScreen)
    {
        _isOnScreen = isOnScreen;

        if (isOnScreen && _refreshDue && !IsLoading)
        {
            _refreshDue = false;
            ReloadFresh();
        }
    }

    private void OnEnablementSourceChanged()
    {
        _uiDispatcher.Invoke(OnPlatformStateChanged);
    }

    private static IReadOnlyList<IPlatformEnablementSource> MergeEnablementSources(
        IEnumerable<IPlatformEnablementSource>? enablementSources,
        IPlatformEnablementSource? fallbackSource)
    {
        var map = new Dictionary<StreamingPlatform, IPlatformEnablementSource>();
        if (enablementSources is not null)
        {
            foreach (var source in enablementSources)
            {
                map[source.Platform] = source;
            }
        }

        if (fallbackSource is not null && !map.ContainsKey(fallbackSource.Platform))
        {
            map[fallbackSource.Platform] = fallbackSource;
        }

        return [.. map.Values];
    }

    public void Dispose()
    {
        foreach (var source in _enablementSources)
        {
            source.Changed -= OnEnablementSourceChanged;
        }

        foreach (var sub in _subs)
        {
            sub.Dispose();
        }

        _subs.Clear();
        _refreshTimer.Stop();
        DropPending();
    }

    [RelayCommand]
    private void SwitchPlatform()
    {
        if (_platformSelection.Cycle())
        {
            ApplyCurrentStatus();
        }
    }

    [RelayCommand]
    private void SelectPlatform(StreamingPlatform platform)
    {
        if (_platformSelection.Select(platform))
        {
            ApplyCurrentStatus();
        }
    }

    [RelayCommand(CanExecute = nameof(CanRefreshFrame))]
    private void RefreshFrame()
    {
        _refreshDue = false;
        ReloadFresh();
        RestartTimer();
    }

    private bool CanRefreshFrame() => _lastThumbnailRawUrl is not null && !IsLoading;

    private void OnRefreshTimerTick()
    {
        var stream = _platformSelection.GetSelectedPlatform()?.StreamStatus;
        if (_lastThumbnailRawUrl is null || stream is null || stream.CurrentStatus != StreamStatus.Online)
        {
            return;
        }

        if (!_isOnScreen || IsLoading)
        {
            _refreshDue = true;
            return;
        }

        ReloadFresh();
    }

    private void OnPlatformStateChanged()
    {
        _platformSelection.Refresh();
        ApplyCurrentStatus();
    }

    private void ReloadFresh()
    {
        if (_lastThumbnailRawUrl is not { } raw || IsLoading)
        {
            return;
        }

        _cacheStamp = Math.Max(DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(), _cacheStamp + 1);
        Decode(raw);
    }

    private void RestartTimer()
    {
        _refreshTimer.Stop();

        if (_lastThumbnailRawUrl is not null)
        {
            _refreshTimer.Start();
        }
    }

    private void ApplyCurrentStatus()
    {
        SelectedPlatform = _platformSelection.SelectedPlatform;
        HasMultiplePlatforms = _platformSelection.HasMultiplePlatforms;
        _switchPlatformToolbarItem.IsVisible = HasMultiplePlatforms;
        RefreshPlatformTabs();

        var stream = _platformSelection.GetSelectedPlatform()?.StreamStatus;
        if (stream is { CurrentStatus: StreamStatus.Online, CurrentStream: { IsBare: false } info })
        {
            LoadThumbnail(info.ThumbnailUrl);
            return;
        }

        ClearFrame();
    }

    private void RefreshPlatformTabs()
    {
        var available = _platformSelection.AvailablePlatforms;
        var needsRebuild = PlatformTabs.Count != available.Count;
        if (!needsRebuild)
        {
            for (var i = 0; i < available.Count; i++)
            {
                if (PlatformTabs[i].Platform != available[i])
                {
                    needsRebuild = true;
                    break;
                }
            }
        }

        if (needsRebuild)
        {
            PlatformTabs.Clear();
            foreach (var platformId in available)
            {
                var displayName = _platformRegistry.Find(platformId)?.DisplayName ?? PlatformVisuals.GetDisplayName(platformId);
                PlatformTabs.Add(new StreamPreviewPlatformTabViewModel(platformId, displayName));
            }
        }

        foreach (var tab in PlatformTabs)
        {
            var chatPlatform = _platformRegistry.Find(tab.Platform);
            tab.IsSelected = tab.Platform == SelectedPlatform;
            tab.IsOnline = chatPlatform?.StreamStatus.CurrentStatus == StreamStatus.Online;
        }
    }

    private void LoadThumbnail(string raw)
    {
        if (string.IsNullOrEmpty(raw))
        {
            ClearFrame();
            return;
        }

        if (raw == _lastThumbnailRawUrl)
        {
            return;
        }

        DropPending();
        _lastThumbnailRawUrl = raw;
        _cacheStamp = Math.Max(DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(), _cacheStamp + 1);
        Thumbnail = null;
        LoadFailed = false;
        HeaderStatus = null;
        RefreshFrameCommand.NotifyCanExecuteChanged();
        RestartTimer();

        Decode(raw);
    }

    private void ClearFrame()
    {
        DropPending();
        _refreshTimer.Stop();
        _refreshDue = false;
        _lastThumbnailRawUrl = null;
        _cacheStamp = 0;
        Thumbnail = null;
        LoadFailed = false;
        HeaderStatus = null;
        RefreshFrameCommand.NotifyCanExecuteChanged();
    }

    private void Decode(string raw)
    {
        var height = (int)Math.Round(DecodeWidth * 9.0 / 16.0);
        var resolved = raw
            .Replace("{width}", DecodeWidth.ToString(CultureInfo.InvariantCulture))
            .Replace("{height}", height.ToString(CultureInfo.InvariantCulture));

        if (!Uri.TryCreate(resolved, UriKind.Absolute, out var uri))
        {
            _logger.StreamPreviewFrameFailed(null, resolved);
            ShowFailure();
            return;
        }

        if (_cacheStamp > 0 && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps))
        {
            var stamp = "t=" + _cacheStamp.ToString(CultureInfo.InvariantCulture);
            var query = uri.Query.TrimStart('?');
            uri = new UriBuilder(uri) { Query = query.Length == 0 ? stamp : query + "&" + stamp }.Uri;
        }

        DropPending();

        var bmp = new BitmapImage();

        try
        {
            bmp.BeginInit();
            bmp.UriSource = uri;
            bmp.DecodePixelWidth = DecodeWidth;
            bmp.CreateOptions = _cacheStamp > 0 ? BitmapCreateOptions.IgnoreImageCache : BitmapCreateOptions.None;
            bmp.CacheOption = BitmapCacheOption.Default;
            bmp.EndInit();
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            _logger.StreamPreviewFrameFailed(ex, uri);
            ShowFailure();
            return;
        }

        if (!bmp.IsDownloading)
        {
            ShowFrame(bmp);
            return;
        }

        _pending = bmp;
        bmp.DownloadCompleted += OnPendingCompleted;
        bmp.DownloadFailed += OnPendingFailed;
        bmp.DecodeFailed += OnPendingFailed;
        IsLoading = true;
    }

    private void OnPendingCompleted(object? sender, EventArgs e)
    {
        if (sender is not BitmapImage bmp || !ReferenceEquals(bmp, _pending))
        {
            return;
        }

        DropPending();
        IsLoading = false;
        ShowFrame(bmp);

        if (bmp.DecodePixelWidth < DecodeWidth && _lastThumbnailRawUrl is { } raw)
        {
            Decode(raw);
        }
        else if (_refreshDue && _isOnScreen)
        {
            _refreshDue = false;
            ReloadFresh();
        }
    }

    private void OnPendingFailed(object? sender, ExceptionEventArgs e)
    {
        if (sender is not BitmapImage bmp || !ReferenceEquals(bmp, _pending))
        {
            return;
        }

        var uri = bmp.UriSource;
        DropPending();
        IsLoading = false;
        _logger.StreamPreviewFrameFailed(e.ErrorException, uri);
        ShowFailure();
    }

    private void ShowFrame(BitmapImage bmp)
    {
        Thumbnail = bmp;
        LoadFailed = false;
        HeaderStatus = null;
    }

    private void ShowFailure()
    {
        if (Thumbnail is null)
        {
            LoadFailed = true;
            return;
        }

        HeaderStatus = new(
            "кадр не обновлён",
            "Свежий кадр загрузить не удалось, показан прежний. Проверьте подключение к интернету.",
            StatusSeverity.Warning);
    }

    private void DropPending()
    {
        if (_pending is not { } bmp)
        {
            return;
        }

        bmp.DownloadCompleted -= OnPendingCompleted;
        bmp.DownloadFailed -= OnPendingFailed;
        bmp.DecodeFailed -= OnPendingFailed;
        _pending = null;
        IsLoading = false;
    }
}

public sealed partial class StreamPreviewPlatformTabViewModel(
    StreamingPlatform platform,
    string displayName) : ObservableObject
{
    public StreamingPlatform Platform { get; } = platform;

    public string DisplayName { get; } = displayName;

    [ObservableProperty]
    private bool _isSelected;

    [ObservableProperty]
    private bool _isOnline;
}
