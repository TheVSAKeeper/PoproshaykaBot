using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MahApps.Metro.IconPacks;
using PoproshaykaBot.Core.Chat;
using PoproshaykaBot.Core.Chat.Commands;
using PoproshaykaBot.Core.Chat.History;
using PoproshaykaBot.Core.Infrastructure.Events;
using PoproshaykaBot.Core.Infrastructure.Events.Chat;
using PoproshaykaBot.Core.Infrastructure.Events.Streaming;
using PoproshaykaBot.Core.Platforms;
using PoproshaykaBot.Core.Statistics;
using PoproshaykaBot.Wpf.Bootstrap;
using PoproshaykaBot.Wpf.Infrastructure;
using PoproshaykaBot.Wpf.Infrastructure.Platforms;
using PoproshaykaBot.Wpf.ViewModels.Tiles.Chat;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Windows;

namespace PoproshaykaBot.Wpf.ViewModels.Tiles;

public sealed partial class MultiplatformChatTileViewModel : DashboardTileViewModel, IUserLinkSource, IDisposable
{
    public const string TileTypeId = "multiplatform-chat";
    public const int MaxFeedItems = 250;
    public const double DefaultFeedFontSize = 13.0;
    public const double MinFeedFontSize = 11.0;
    public const double MaxFeedFontSize = 24.0;
    public const double FontSizeStep = 1.0;

    private readonly ChatHistoryManager _chatHistory;
    private readonly IChatHistorySettingsProvider? _chatSettingsProvider;
    private readonly IChatPlatformRegistry? _platformRegistry;
    private readonly IUiDispatcher? _uiDispatcher;
    private readonly ISettingsStore? _uiSettings;
    private readonly BroadcastHistoryStore? _broadcastHistoryStore;
    private readonly List<IDisposable> _subscriptions = [];
    private readonly List<ChatFeedItemViewModel> _allItems = [];
    private readonly ChatFeedHistoryBoundaryItemViewModel _historyBoundaryItem = new();
    private readonly Dictionary<string, ChatFeedStreamDividerItemViewModel> _streamDividers = new(StringComparer.Ordinal);
    private IReadOnlyList<BroadcastRecord> _knownBroadcasts = [];

    private double _feedFontSize;
    private bool _suppressPersist;

    [ObservableProperty]
    private PlatformFilterOption _selectedPlatformFilter = PlatformFilterOption.All;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasUnread))]
    private bool _isPinnedToBottom = true;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasUnread), nameof(UnreadButtonText))]
    private int _unreadCount;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(FeedLineHeight), nameof(FeedItemPadding))]
    private bool _isCompactMode;

    [ObservableProperty]
    private bool _showTimestamps;

    [ObservableProperty]
    private bool _showPlatformIcons;

    [ObservableProperty]
    private bool _showRoleBadges;

    [ObservableProperty]
    private bool _isAppearancePanelOpen;

    public MultiplatformChatTileViewModel(
        ChatHistoryManager chatHistory,
        IEventBus eventBus,
        IChatHistorySettingsProvider? chatSettingsProvider = null,
        IUiDispatcher? uiDispatcher = null,
        IChatPlatformRegistry? platformRegistry = null,
        ISettingsStore? uiSettings = null,
        BroadcastHistoryStore? broadcastHistoryStore = null)
        : base(TileTypeId, "Общий чат", minWidth: 280, minHeight: 220)
    {
        ArgumentNullException.ThrowIfNull(chatHistory);
        ArgumentNullException.ThrowIfNull(eventBus);

        _chatHistory = chatHistory;
        _chatSettingsProvider = chatSettingsProvider;
        _platformRegistry = platformRegistry;
        _uiDispatcher = uiDispatcher;
        _uiSettings = uiSettings;
        _broadcastHistoryStore = broadcastHistoryStore;
        PlatformOptions = PlatformFilterOption.ForRegistry(platformRegistry);

        _feedFontSize = LoadFontSize();
        _isCompactMode = LoadBool(SettingsKeys.ChatFeedCompactMode, defaultValue: false);
        _showTimestamps = LoadBool(SettingsKeys.ChatFeedShowTimestamps, defaultValue: true);
        _showPlatformIcons = LoadBool(SettingsKeys.ChatFeedShowPlatformIcons, defaultValue: true);
        _showRoleBadges = LoadBool(SettingsKeys.ChatFeedShowRoleBadges, defaultValue: true);

        HeaderActions.Add(new ToolbarItemViewModel(
            PackIconLucideKind.SlidersHorizontal,
            ToggleAppearancePanelCommand,
            "Настройки отображения ленты"));

        _subscriptions.Add(eventBus.SubscribeOnUi<ChatHistoryLoaded>(OnChatHistoryLoaded));
        _subscriptions.Add(eventBus.SubscribeOnUi<ChatMessageReceived>(OnChatMessageReceived));
        _subscriptions.Add(eventBus.SubscribeOnUi<ChatMessagesDeleted>(OnChatMessagesDeleted));
        _subscriptions.Add(eventBus.SubscribeOnUi<ChatHistoryCleared>(OnChatHistoryCleared));
        _subscriptions.Add(eventBus.SubscribeOnUi<ChatReadingGapDetected>(OnChatReadingGapDetected));
        _subscriptions.Add(eventBus.SubscribeOnUi<ActiveChannelChanged>(OnActiveChannelChanged));
        _subscriptions.Add(eventBus.SubscribeOnUi<YouTubeActiveBroadcastChanged>(_ => OnStreamContextChanged()));
        _subscriptions.Add(eventBus.SubscribeOnUi<StreamWentOnline>(_ => OnStreamContextChanged()));

        if (_chatSettingsProvider is not null)
        {
            _chatSettingsProvider.Changed += OnChatSettingsChanged;
        }

        LoadInitialHistory();
    }

    public event EventHandler<PlatformUserKey>? UserRequested;

    public event EventHandler? ScrollToBottomRequested;

    public override PackIconLucideKind Icon => PackIconLucideKind.MessagesSquare;

    public override bool FillsAvailableSpace => true;

    public override bool GrowsWithSpace => true;

    public IReadOnlyList<PlatformFilterOption> PlatformOptions { get; }

    public ObservableCollection<ChatFeedItemViewModel> Items { get; } = [];

    public bool IsEmpty => Items.Count == 0;

    public bool HasUnread => UnreadCount > 0 && !IsPinnedToBottom;

    public string UnreadButtonText => $"↓ Новые сообщения ({UnreadCount})";

    public double FeedFontSize
    {
        get => _feedFontSize;
        set
        {
            var normalized = ClampFontSize(value);
            if (!SetProperty(ref _feedFontSize, normalized))
            {
                return;
            }

            NotifyFontMetricsChanged();
            IncreaseFontSizeCommand.NotifyCanExecuteChanged();
            DecreaseFontSizeCommand.NotifyCanExecuteChanged();
            PersistSetting(SettingsKeys.ChatFeedFontSize, normalized.ToString("0.##", CultureInfo.InvariantCulture));
        }
    }

    public double FeedMetaFontSize => Math.Max(10.0, FeedFontSize - 2.0);

    public double FeedLineHeight => IsCompactMode
        ? Math.Round(FeedFontSize * 1.28)
        : Math.Round(FeedFontSize * 1.48);

    public Thickness FeedItemPadding => IsCompactMode
        ? new Thickness(2, 1, 2, 1)
        : new Thickness(2, 3, 2, 3);

    public double FeedPlatformIconSize => Math.Round(FeedFontSize * 1.05);

    public string FeedFontSizeText => string.Create(CultureInfo.InvariantCulture, $"{FeedFontSize:0} px");

    public bool CanDecreaseFontSize => FeedFontSize > MinFeedFontSize;

    public bool CanIncreaseFontSize => FeedFontSize < MaxFeedFontSize;

    public void UpdateScrollPosition(bool isAtBottom)
    {
        IsPinnedToBottom = isAtBottom;

        if (isAtBottom && UnreadCount != 0)
        {
            UnreadCount = 0;
        }
    }

    [RelayCommand]
    private void ScrollToBottom()
    {
        IsPinnedToBottom = true;
        UnreadCount = 0;
        ScrollToBottomRequested?.Invoke(this, EventArgs.Empty);
    }

    [RelayCommand(CanExecute = nameof(CanIncreaseFontSize))]
    private void IncreaseFontSize()
    {
        FeedFontSize = Math.Min(MaxFeedFontSize, FeedFontSize + FontSizeStep);
    }

    [RelayCommand(CanExecute = nameof(CanDecreaseFontSize))]
    private void DecreaseFontSize()
    {
        FeedFontSize = Math.Max(MinFeedFontSize, FeedFontSize - FontSizeStep);
    }

    [RelayCommand]
    private void ToggleAppearancePanel()
    {
        IsAppearancePanelOpen = !IsAppearancePanelOpen;
    }

    [RelayCommand]
    private void ResetAppearance()
    {
        _suppressPersist = true;
        try
        {
            FeedFontSize = DefaultFeedFontSize;
            IsCompactMode = false;
            ShowTimestamps = true;
            ShowPlatformIcons = true;
            ShowRoleBadges = true;
        }
        finally
        {
            _suppressPersist = false;
        }

        PersistAllAppearanceSettings();
    }

    public void Dispose()
    {
        if (_chatSettingsProvider is not null)
        {
            _chatSettingsProvider.Changed -= OnChatSettingsChanged;
        }

        foreach (var subscription in _subscriptions)
        {
            subscription.Dispose();
        }

        _subscriptions.Clear();
    }

    partial void OnSelectedPlatformFilterChanged(PlatformFilterOption value)
    {
        RebuildVisibleItems();
        ScrollToBottom();
    }

    partial void OnIsCompactModeChanged(bool value)
    {
        PersistBool(SettingsKeys.ChatFeedCompactMode, value);
    }

    partial void OnShowTimestampsChanged(bool value)
    {
        PersistBool(SettingsKeys.ChatFeedShowTimestamps, value);
    }

    partial void OnShowPlatformIconsChanged(bool value)
    {
        PersistBool(SettingsKeys.ChatFeedShowPlatformIcons, value);
    }

    partial void OnShowRoleBadgesChanged(bool value)
    {
        PersistBool(SettingsKeys.ChatFeedShowRoleBadges, value);
    }

    private double LoadFontSize()
    {
        var raw = _uiSettings?.GetStringValue(SettingsKeys.ChatFeedFontSize);
        if (double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed)
            && double.IsFinite(parsed))
        {
            return ClampFontSize(parsed);
        }

        return DefaultFeedFontSize;
    }

    private bool LoadBool(string key, bool defaultValue)
    {
        var raw = _uiSettings?.GetStringValue(key);
        return bool.TryParse(raw, out var parsed) ? parsed : defaultValue;
    }

    private void PersistBool(string key, bool value)
    {
        PersistSetting(key, value ? "true" : "false");
    }

    private void PersistSetting(string key, string value)
    {
        if (_suppressPersist)
        {
            return;
        }

        _uiSettings?.SetValue(key, value);
    }

    private void PersistAllAppearanceSettings()
    {
        PersistSetting(SettingsKeys.ChatFeedFontSize, FeedFontSize.ToString("0.##", CultureInfo.InvariantCulture));
        PersistBool(SettingsKeys.ChatFeedCompactMode, IsCompactMode);
        PersistBool(SettingsKeys.ChatFeedShowTimestamps, ShowTimestamps);
        PersistBool(SettingsKeys.ChatFeedShowPlatformIcons, ShowPlatformIcons);
        PersistBool(SettingsKeys.ChatFeedShowRoleBadges, ShowRoleBadges);
    }

    private void NotifyFontMetricsChanged()
    {
        OnPropertyChanged(nameof(FeedMetaFontSize));
        OnPropertyChanged(nameof(FeedLineHeight));
        OnPropertyChanged(nameof(FeedPlatformIconSize));
        OnPropertyChanged(nameof(FeedFontSizeText));
        OnPropertyChanged(nameof(CanDecreaseFontSize));
        OnPropertyChanged(nameof(CanIncreaseFontSize));
    }

    private static double ClampFontSize(double value) =>
        double.IsFinite(value)
            ? Math.Clamp(value, MinFeedFontSize, MaxFeedFontSize)
            : DefaultFeedFontSize;

    private void OnChatSettingsChanged(Core.Settings.Chat.ChatSettings _)
    {
        void Reload()
        {
            LoadInitialHistory();
            if (IsPinnedToBottom)
            {
                ScrollToBottomRequested?.Invoke(this, EventArgs.Empty);
            }
        }

        if (_uiDispatcher is not null)
        {
            _uiDispatcher.Invoke(Reload);
        }
        else
        {
            Reload();
        }
    }

    private void LoadInitialHistory()
    {
        _allItems.Clear();
        RefreshKnownStreamSessions();

        var showHistory = _chatSettingsProvider?.GetCurrent().ShowHistoryInApp ?? false;
        var history = showHistory ? _chatHistory.GetActiveChannelHistory() : _chatHistory.GetActiveHistory();

        foreach (var entry in history)
        {
            if ((entry.IsHistorical && !showHistory) || !ShouldDisplayEntry(entry))
            {
                continue;
            }

            _allItems.Add(CreateMessageItem(entry));
        }

        TrimAndEnsureHistoryBoundary();
        RebuildVisibleItems();
        UnreadCount = 0;
    }

    private void OnStreamContextChanged()
    {
        RefreshKnownStreamSessions();
        RebuildVisibleItems();
    }

    private void RefreshKnownStreamSessions()
    {
        if (_broadcastHistoryStore is null)
        {
            _knownBroadcasts = [];
            return;
        }

        try
        {
            _knownBroadcasts = _broadcastHistoryStore.LoadVisible().Broadcasts
                .Where(static broadcast => !broadcast.IsHidden)
                .OrderBy(static broadcast => broadcast.StartedAt)
                .ToArray();
        }
        catch
        {
            _knownBroadcasts = [];
        }
    }

    private void OnChatHistoryLoaded(ChatHistoryLoaded historyEvent)
    {
        var showHistory = _chatSettingsProvider?.GetCurrent().ShowHistoryInApp ?? false;
        var scopeMatches = showHistory
            ? _chatHistory.IsActiveChannel(historyEvent.Scope.Platform, historyEvent.Scope.ChannelId)
            : _chatHistory.IsActiveScope(historyEvent.Scope);

        if (!scopeMatches)
        {
            return;
        }

        var addedAny = false;

        foreach (var entry in historyEvent.AddedEntries)
        {
            if ((entry.IsHistorical && !showHistory) || !ShouldDisplayEntry(entry))
            {
                continue;
            }

            if (UpsertMessage(entry))
            {
                addedAny = true;
            }
        }

        if (!addedAny)
        {
            return;
        }

        SortMessagesChronologically();
        TrimAndEnsureHistoryBoundary();
        RebuildVisibleItems();

        if (IsPinnedToBottom)
        {
            ScrollToBottomRequested?.Invoke(this, EventArgs.Empty);
        }
    }

    private void OnActiveChannelChanged(ActiveChannelChanged _)
    {
        LoadInitialHistory();

        if (IsPinnedToBottom)
        {
            ScrollToBottomRequested?.Invoke(this, EventArgs.Empty);
        }
    }

    private void OnChatMessageReceived(ChatMessageReceived messageEvent)
    {
        var entry = messageEvent.HistoryEntry;
        var incomingScope = ChatHistoryScope.FromMessage(entry, messageEvent.Channel);
        if (!_chatHistory.IsActiveChannel(incomingScope.Platform, incomingScope.ChannelId)
            || !ShouldDisplayEntry(entry, messageEvent.CommandResponse))
        {
            return;
        }
        var key = ChatFeedMessageItemViewModel.CreateKey(entry);
        var existing = FindMessageByKey(key);

        if (existing is not null)
        {
            existing.MessageText = entry.Message;

            if (messageEvent.IsReplacement)
            {
                return;
            }

            return;
        }

        var item = CreateMessageItem(entry);
        InsertLiveMessage(item);
        TrimAndEnsureHistoryBoundary();
        RebuildVisibleItems();

        if (MatchesFilter(item))
        {
            if (IsPinnedToBottom)
            {
                ScrollToBottomRequested?.Invoke(this, EventArgs.Empty);
            }
            else
            {
                UnreadCount++;
            }
        }
    }

    private void OnChatMessagesDeleted(ChatMessagesDeleted deletedEvent)
    {
        var deletedIds = deletedEvent.DeletedMessageIds.Count > 0
            ? new HashSet<string>(deletedEvent.DeletedMessageIds, StringComparer.Ordinal)
            : null;
        var cutoffUtc = deletedEvent.CutoffUtc?.UtcDateTime;
        var removed = false;

        for (var i = _allItems.Count - 1; i >= 0; i--)
        {
            if (_allItems[i] is not ChatFeedMessageItemViewModel msg || !ScopeMatchesMessage(deletedEvent.Scope, msg))
            {
                continue;
            }

            var matchById = deletedIds is not null && deletedIds.Contains(msg.Key.MessageId);
            var matchByUser = !string.IsNullOrEmpty(deletedEvent.ModeratedUserId)
                && string.Equals(msg.UserId, deletedEvent.ModeratedUserId, StringComparison.Ordinal)
                && (cutoffUtc is null || msg.TimestampUtc <= cutoffUtc.Value);

            if (matchById || matchByUser)
            {
                _allItems.RemoveAt(i);
                removed = true;
            }
        }

        if (!removed)
        {
            return;
        }

        TrimAndEnsureHistoryBoundary();
        RebuildVisibleItems();
    }

    private void OnChatHistoryCleared(ChatHistoryCleared clearedEvent)
    {
        var cutoffUtc = (clearedEvent.ClearedAtUtc ?? DateTimeOffset.UtcNow).UtcDateTime;
        var channelFilter = string.IsNullOrWhiteSpace(clearedEvent.Channel) ? null : clearedEvent.Channel.Trim();
        var removed = false;

        for (var i = _allItems.Count - 1; i >= 0; i--)
        {
            if (ShouldClearItem(_allItems[i], clearedEvent.Platform, channelFilter, cutoffUtc))
            {
                _allItems.RemoveAt(i);
                removed = true;
            }
        }

        if (!removed)
        {
            return;
        }

        TrimAndEnsureHistoryBoundary();
        RebuildVisibleItems();

        if (Items.Count == 0)
        {
            UnreadCount = 0;
        }
    }

    private void OnChatReadingGapDetected(ChatReadingGapDetected gapEvent)
    {
        var lastGap = _allItems.OfType<ChatFeedGapItemViewModel>().LastOrDefault();
        if (lastGap is not null
            && lastGap.Platform == gapEvent.Platform
            && string.Equals(lastGap.LiveChatId, gapEvent.LiveChatId, StringComparison.Ordinal)
            && ReferenceEquals(_allItems[^1], lastGap))
        {
            return;
        }

        var gapItem = new ChatFeedGapItemViewModel(
            gapEvent.Platform,
            gapEvent.Channel,
            gapEvent.LiveChatId,
            gapEvent.DetectedAtUtc,
            PlatformVisuals.GetDisplayName(gapEvent.Platform, _platformRegistry));
        _allItems.Add(gapItem);
        TrimAndEnsureHistoryBoundary();
        RebuildVisibleItems();

        if (MatchesFilter(gapItem))
        {
            if (IsPinnedToBottom)
            {
                ScrollToBottomRequested?.Invoke(this, EventArgs.Empty);
            }
            else
            {
                UnreadCount++;
            }
        }
    }

    private ChatFeedMessageItemViewModel CreateMessageItem(ChatMessageData entry)
    {
        return new ChatFeedMessageItemViewModel(
            ChatFeedMessageItemViewModel.CreateKey(entry),
            entry.Channel,
            entry.Timestamp.Kind == DateTimeKind.Utc ? entry.Timestamp : entry.Timestamp.ToUniversalTime(),
            entry.DisplayName,
            entry.Message,
            entry.UserId,
            entry.Status,
            entry.IsHistorical,
            entry.IsBot,
            entry.IsFirstTime,
            entry.Color,
            key => UserRequested?.Invoke(this, key),
            PlatformVisuals.GetDisplayName(entry.Platform, _platformRegistry),
            entry.ChatSessionId,
            entry.BroadcastId,
            entry.StreamId,
            entry.StreamStartedAtUtc,
            entry.StreamTitle);
    }

    private bool ShouldDisplayEntry(ChatMessageData entry, CommandResponseMark? responseOverride = null)
    {
        var mark = responseOverride ?? entry.CommandResponse;
        if (mark is null)
        {
            return true;
        }

        var capabilities = _platformRegistry is not null && _platformRegistry.TryGet(entry.Platform, out var platform)
            ? platform.Capabilities
            : (entry.Platform == StreamingPlatform.Twitch ? PlatformCapabilities.Whisper : PlatformCapabilities.None);

        return CommandResponseRouting.WillDeliverToChat(mark.Target, capabilities);
    }

    private ChatFeedMessageItemViewModel? FindMessageByKey(ChatFeedMessageKey key)
    {
        foreach (var item in _allItems)
        {
            if (item is ChatFeedMessageItemViewModel msg && msg.Key == key)
            {
                return msg;
            }
        }

        return null;
    }

    private bool UpsertMessage(ChatMessageData entry)
    {
        var key = ChatFeedMessageItemViewModel.CreateKey(entry);
        if (FindMessageByKey(key) is { } existing)
        {
            existing.MessageText = entry.Message;
            return false;
        }

        _allItems.Add(CreateMessageItem(entry));
        return true;
    }

    private void InsertLiveMessage(ChatFeedMessageItemViewModel item)
    {
        for (var i = _allItems.Count - 1; i >= 0; i--)
        {
            if (_allItems[i] is ChatFeedMessageItemViewModel existing && existing.TimestampUtc <= item.TimestampUtc)
            {
                _allItems.Insert(i + 1, item);
                return;
            }

            if (_allItems[i] is ChatFeedGapItemViewModel gap && gap.DetectedAtUtc.UtcDateTime <= item.TimestampUtc)
            {
                _allItems.Insert(i + 1, item);
                return;
            }
        }

        _allItems.Insert(0, item);
    }

    private void SortMessagesChronologically()
    {
        _allItems.RemoveAll(static item => item is ChatFeedHistoryBoundaryItemViewModel);
        _allItems.Sort(static (left, right) => GetOrderTicks(left).CompareTo(GetOrderTicks(right)));
    }

    private static long GetOrderTicks(ChatFeedItemViewModel item) => item switch
    {
        ChatFeedMessageItemViewModel msg => msg.TimestampUtc.Ticks,
        ChatFeedGapItemViewModel gap => gap.DetectedAtUtc.UtcDateTime.Ticks,
        _ => long.MaxValue,
    };

    private void TrimAndEnsureHistoryBoundary()
    {
        _allItems.RemoveAll(static item => item is ChatFeedHistoryBoundaryItemViewModel);

        while (_allItems.Count > MaxFeedItems)
        {
            _allItems.RemoveAt(0);
        }

        var lastHistoricalIndex = -1;
        for (var i = 0; i < _allItems.Count; i++)
        {
            if (_allItems[i] is ChatFeedMessageItemViewModel { IsHistorical: true })
            {
                lastHistoricalIndex = i;
            }
        }

        if (lastHistoricalIndex >= 0)
        {
            _allItems.Insert(lastHistoricalIndex + 1, _historyBoundaryItem);
            while (_allItems.Count > MaxFeedItems)
            {
                _allItems.RemoveAt(0);
            }
        }
    }

    private void RebuildVisibleItems()
    {
        var filter = SelectedPlatformFilter?.Platform;
        var hasHistoricalVisible = false;
        var filtered = new List<ChatFeedItemViewModel>(_allItems.Count);

        foreach (var item in _allItems)
        {
            if (item is ChatFeedHistoryBoundaryItemViewModel)
            {
                if (hasHistoricalVisible)
                {
                    filtered.Add(item);
                }

                continue;
            }

            if (filter is not null && item.PlatformFilterKey != filter)
            {
                continue;
            }

            if (item is ChatFeedMessageItemViewModel { IsHistorical: true })
            {
                hasHistoricalVisible = true;
            }

            filtered.Add(item);
        }

        var target = BuildItemsWithStreamDividers(filtered);
        SyncVisibleItems(target);
        OnPropertyChanged(nameof(IsEmpty));
    }

    private List<ChatFeedItemViewModel> BuildItemsWithStreamDividers(List<ChatFeedItemViewModel> filtered)
    {
        var messageCount = 0;
        var distinctPartitions = new HashSet<string>(StringComparer.Ordinal);
        var hasExplicitStreamOrPastDate = false;
        var todayLocal = DateTime.Now.Date;
        var partitions = new Dictionary<ChatFeedMessageItemViewModel, (string Key, string Label, bool IsStream)>(ReferenceEqualityComparer.Instance);

        foreach (var item in filtered)
        {
            if (item is not ChatFeedMessageItemViewModel msg)
            {
                continue;
            }

            messageCount++;
            var partition = ResolveMessagePartition(msg);
            partitions[msg] = partition;
            distinctPartitions.Add(partition.Key);

            if (partition.IsStream || msg.TimestampUtc.ToLocalTime().Date != todayLocal)
            {
                hasExplicitStreamOrPastDate = true;
            }
        }

        if (messageCount == 0 || (distinctPartitions.Count <= 1 && !hasExplicitStreamOrPastDate))
        {
            return filtered;
        }

        var target = new List<ChatFeedItemViewModel>(filtered.Count + distinctPartitions.Count);
        var occurrenceByKey = new Dictionary<string, int>(StringComparer.Ordinal);
        string? currentPartitionKey = null;

        foreach (var item in filtered)
        {
            if (item is ChatFeedMessageItemViewModel msg
                && partitions.TryGetValue(msg, out var partition)
                && !string.Equals(currentPartitionKey, partition.Key, StringComparison.Ordinal))
            {
                currentPartitionKey = partition.Key;
                var occurrence = occurrenceByKey.TryGetValue(partition.Key, out var count) ? count : 0;
                occurrenceByKey[partition.Key] = occurrence + 1;
                target.Add(GetOrCreateDivider($"{partition.Key}#{occurrence}", partition.Label, partition.IsStream));
            }

            target.Add(item);
        }

        while (target.Count > MaxFeedItems)
        {
            target.RemoveAt(0);
        }

        while (target.Count > 0 && target[0] is ChatFeedHistoryBoundaryItemViewModel)
        {
            target.RemoveAt(0);
        }

        while (target.Count > 0
            && target[0] is ChatFeedStreamDividerItemViewModel
            && (target.Count == 1 || target[1] is not ChatFeedMessageItemViewModel))
        {
            target.RemoveAt(0);
        }

        if (target.Count > 0 && target[0] is ChatFeedMessageItemViewModel)
        {
            if (target.Count >= MaxFeedItems)
            {
                target.RemoveAt(0);
            }

            while (target.Count > 0 && target[0] is ChatFeedHistoryBoundaryItemViewModel)
            {
                target.RemoveAt(0);
            }

            if (target.Count > 0
                && target[0] is ChatFeedMessageItemViewModel headMsg
                && partitions.TryGetValue(headMsg, out var headPartition))
            {
                target.Insert(0, GetOrCreateDivider($"{headPartition.Key}#head", headPartition.Label, headPartition.IsStream));
            }
        }

        return target;
    }

    private ChatFeedStreamDividerItemViewModel GetOrCreateDivider(string key, string label, bool isStream)
    {
        if (_streamDividers.TryGetValue(key, out var existing)
            && string.Equals(existing.Label, label, StringComparison.Ordinal))
        {
            return existing;
        }

        var created = new ChatFeedStreamDividerItemViewModel(key, label, isStream);
        _streamDividers[key] = created;
        return created;
    }

    private (string Key, string Label, bool IsStream) ResolveMessagePartition(ChatFeedMessageItemViewModel msg)
    {
        if (msg.BroadcastId is { } broadcastId)
        {
            var startLocal = (msg.StreamStartedAtUtc ?? msg.TimestampUtc).ToLocalTime();
            return ($"broadcast:{broadcastId:N}", FormatStreamLabel(startLocal, msg.StreamTitle), true);
        }

        if (!string.IsNullOrWhiteSpace(msg.StreamId))
        {
            var startLocal = (msg.StreamStartedAtUtc ?? msg.TimestampUtc).ToLocalTime();
            return ($"stream:{msg.Platform}:{msg.StreamId}", FormatStreamLabel(startLocal, msg.StreamTitle), true);
        }

        if (TryMatchKnownBroadcast(msg, out var matchedBroadcast, out var matchedPlatform))
        {
            var startedAt = matchedPlatform.StartedAt != default ? matchedPlatform.StartedAt : matchedBroadcast.StartedAt;
            var title = !string.IsNullOrWhiteSpace(matchedPlatform.Title) ? matchedPlatform.Title : matchedBroadcast.Title;
            var label = FormatStreamLabel(startedAt.ToLocalTime().DateTime, title);
            return ($"broadcast:{matchedBroadcast.Id:N}", label, true);
        }

        if (!string.IsNullOrWhiteSpace(msg.ChatSessionId))
        {
            var local = msg.TimestampUtc.ToLocalTime();
            return ($"session:{msg.Platform}:{msg.ChatSessionId}", $"Эфир {local.ToString("dd.MM", UiCulture.Russian)}", true);
        }

        var localDate = msg.TimestampUtc.ToLocalTime().Date;
        return ($"date:{localDate:yyyy-MM-dd}", localDate.ToString("d MMMM yyyy", UiCulture.Russian), false);
    }

    private bool TryMatchKnownBroadcast(
        ChatFeedMessageItemViewModel msg,
        out BroadcastRecord matchedBroadcast,
        out BroadcastPlatformRecord matchedPlatform)
    {
        matchedBroadcast = null!;
        matchedPlatform = null!;

        if (_knownBroadcasts.Count == 0)
        {
            return false;
        }

        var msgOffset = new DateTimeOffset(DateTime.SpecifyKind(msg.TimestampUtc, DateTimeKind.Utc));
        var grace = TimeSpan.FromMinutes(5);

        foreach (var broadcast in _knownBroadcasts)
        {
            foreach (var platform in broadcast.Platforms)
            {
                if (platform.Platform != msg.Platform || !ChannelMatchesPlatformRecord(msg, platform))
                {
                    continue;
                }

                var start = (platform.StartedAt != default ? platform.StartedAt : broadcast.StartedAt) - grace;
                var rawEnd = platform.EndedAt > platform.StartedAt
                    ? platform.EndedAt
                    : (broadcast.EndedAt > broadcast.StartedAt ? broadcast.EndedAt : platform.StartedAt);
                var end = rawEnd + grace;
                if (msgOffset >= start && msgOffset <= end)
                {
                    matchedBroadcast = broadcast;
                    matchedPlatform = platform;
                    return true;
                }
            }
        }

        return false;
    }

    private static bool ChannelMatchesPlatformRecord(ChatFeedMessageItemViewModel msg, BroadcastPlatformRecord platform)
    {
        if (!string.IsNullOrWhiteSpace(platform.ChannelId) && !string.IsNullOrWhiteSpace(msg.Key.ChannelId))
        {
            return string.Equals(platform.ChannelId, msg.Key.ChannelId, StringComparison.OrdinalIgnoreCase);
        }

        if (!string.IsNullOrWhiteSpace(platform.Channel) && !string.IsNullOrWhiteSpace(msg.Channel))
        {
            return string.Equals(platform.Channel, msg.Channel, StringComparison.OrdinalIgnoreCase);
        }

        return false;
    }

    private static string FormatStreamLabel(DateTime startLocal, string? title)
    {
        var stamp = startLocal.ToString("dd.MM · HH:mm", UiCulture.Russian);
        var trimmedTitle = title?.Trim();
        return string.IsNullOrEmpty(trimmedTitle)
            ? $"Стрим {stamp}"
            : $"Стрим {stamp} – {trimmedTitle}";
    }

    private void SyncVisibleItems(List<ChatFeedItemViewModel> target)
    {
        if (target.Count == 0)
        {
            Items.Clear();
            return;
        }

        var targetSet = new HashSet<ChatFeedItemViewModel>(target, ReferenceEqualityComparer.Instance);
        for (var i = Items.Count - 1; i >= 0; i--)
        {
            if (!targetSet.Contains(Items[i]))
            {
                Items.RemoveAt(i);
            }
        }

        for (var i = 0; i < target.Count; i++)
        {
            var expected = target[i];
            if (i >= Items.Count)
            {
                Items.Add(expected);
                continue;
            }

            if (ReferenceEquals(Items[i], expected))
            {
                continue;
            }

            var existingIndex = Items.IndexOf(expected);
            if (existingIndex > i)
            {
                Items.Move(existingIndex, i);
            }
            else
            {
                Items.Insert(i, expected);
            }
        }

        while (Items.Count > target.Count)
        {
            Items.RemoveAt(Items.Count - 1);
        }
    }

    private bool MatchesFilter(ChatFeedItemViewModel item)
    {
        var filter = SelectedPlatformFilter?.Platform;
        return filter is null || item.PlatformFilterKey is null || item.PlatformFilterKey == filter;
    }

    private static bool ShouldClearItem(
        ChatFeedItemViewModel item,
        StreamingPlatform platform,
        string? channelFilter,
        DateTime cutoffUtc)
    {
        return item switch
        {
            ChatFeedMessageItemViewModel msg => msg.Platform == platform
                && msg.TimestampUtc <= cutoffUtc
                && (channelFilter is null
                    || string.Equals(msg.Key.ChannelId, channelFilter, StringComparison.OrdinalIgnoreCase)
                    || string.Equals(msg.Channel, channelFilter, StringComparison.OrdinalIgnoreCase)),
            ChatFeedGapItemViewModel gap => gap.Platform == platform
                && gap.DetectedAtUtc.UtcDateTime <= cutoffUtc
                && (channelFilter is null || string.Equals(gap.Channel, channelFilter, StringComparison.OrdinalIgnoreCase)),
            _ => false,
        };
    }

    private static bool ScopeMatchesMessage(ChatHistoryScope scope, ChatFeedMessageItemViewModel message)
    {
        if (message.Platform != scope.Platform)
        {
            return false;
        }

        if (!string.IsNullOrWhiteSpace(scope.SessionScopeId)
            && !string.IsNullOrWhiteSpace(message.ChatSessionId)
            && !string.Equals(message.ChatSessionId, scope.SessionScopeId, StringComparison.Ordinal))
        {
            return false;
        }

        if (string.IsNullOrWhiteSpace(scope.ChannelId) && string.IsNullOrWhiteSpace(scope.ChannelName))
        {
            return true;
        }

        return (!string.IsNullOrWhiteSpace(scope.ChannelId)
                && (string.Equals(message.Key.ChannelId, scope.ChannelId, StringComparison.OrdinalIgnoreCase)
                    || string.Equals(message.Channel, scope.ChannelId, StringComparison.OrdinalIgnoreCase)))
            || (!string.IsNullOrWhiteSpace(scope.ChannelName)
                && (string.Equals(message.Key.ChannelId, scope.ChannelName, StringComparison.OrdinalIgnoreCase)
                    || string.Equals(message.Channel, scope.ChannelName, StringComparison.OrdinalIgnoreCase)));
    }
}
