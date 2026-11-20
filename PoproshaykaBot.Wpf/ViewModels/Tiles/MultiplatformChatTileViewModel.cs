using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MahApps.Metro.IconPacks;
using PoproshaykaBot.Core.Chat;
using PoproshaykaBot.Core.Chat.Commands;
using PoproshaykaBot.Core.Chat.History;
using PoproshaykaBot.Core.Infrastructure.Events;
using PoproshaykaBot.Core.Infrastructure.Events.Chat;
using PoproshaykaBot.Core.Platforms;
using PoproshaykaBot.Wpf.Infrastructure;
using PoproshaykaBot.Wpf.Infrastructure.Platforms;
using PoproshaykaBot.Wpf.ViewModels.Tiles.Chat;
using System.Collections.ObjectModel;

namespace PoproshaykaBot.Wpf.ViewModels.Tiles;

public sealed partial class MultiplatformChatTileViewModel : DashboardTileViewModel, IUserLinkSource, IDisposable
{
    public const string TileTypeId = "multiplatform-chat";
    public const int MaxFeedItems = 250;

    private readonly ChatHistoryManager _chatHistory;
    private readonly IChatHistorySettingsProvider? _chatSettingsProvider;
    private readonly IChatPlatformRegistry? _platformRegistry;
    private readonly IUiDispatcher? _uiDispatcher;
    private readonly List<IDisposable> _subscriptions = [];
    private readonly List<ChatFeedItemViewModel> _allItems = [];
    private readonly ChatFeedHistoryBoundaryItemViewModel _historyBoundaryItem = new();

    [ObservableProperty]
    private PlatformFilterOption _selectedPlatformFilter = PlatformFilterOption.All;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasUnread))]
    private bool _isPinnedToBottom = true;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasUnread), nameof(UnreadButtonText))]
    private int _unreadCount;

    public MultiplatformChatTileViewModel(
        ChatHistoryManager chatHistory,
        IEventBus eventBus,
        IChatHistorySettingsProvider? chatSettingsProvider = null,
        IUiDispatcher? uiDispatcher = null,
        IChatPlatformRegistry? platformRegistry = null)
        : base(TileTypeId, "Общий чат", minWidth: 280, minHeight: 220)
    {
        ArgumentNullException.ThrowIfNull(chatHistory);
        ArgumentNullException.ThrowIfNull(eventBus);

        _chatHistory = chatHistory;
        _chatSettingsProvider = chatSettingsProvider;
        _platformRegistry = platformRegistry;
        _uiDispatcher = uiDispatcher;
        PlatformOptions = platformRegistry is null
            ? PlatformFilterOption.AllOptions
            : [PlatformFilterOption.All, .. platformRegistry.All.Select(static platform => new PlatformFilterOption(platform.Id, platform.DisplayName))];

        _subscriptions.Add(eventBus.SubscribeOnUi<ChatHistoryLoaded>(OnChatHistoryLoaded));
        _subscriptions.Add(eventBus.SubscribeOnUi<ChatMessageReceived>(OnChatMessageReceived));
        _subscriptions.Add(eventBus.SubscribeOnUi<ChatMessagesDeleted>(OnChatMessagesDeleted));
        _subscriptions.Add(eventBus.SubscribeOnUi<ChatHistoryCleared>(OnChatHistoryCleared));
        _subscriptions.Add(eventBus.SubscribeOnUi<ChatReadingGapDetected>(OnChatReadingGapDetected));

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

        var showHistory = _chatSettingsProvider?.GetCurrent().ShowHistoryInApp ?? false;
        var history = _chatHistory.GetActiveHistory();

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

    private void OnChatHistoryLoaded(ChatHistoryLoaded historyEvent)
    {
        if (!_chatHistory.IsActiveScope(historyEvent.Scope))
        {
            return;
        }

        var showHistory = _chatSettingsProvider?.GetCurrent().ShowHistoryInApp ?? false;
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

    private void OnChatMessageReceived(ChatMessageReceived messageEvent)
    {
        var entry = messageEvent.HistoryEntry;
        if (!ShouldDisplayEntry(entry, messageEvent.CommandResponse))
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
            PlatformVisuals.GetDisplayName(entry.Platform, _platformRegistry));
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
        var target = new List<ChatFeedItemViewModel>(_allItems.Count);

        foreach (var item in _allItems)
        {
            if (item is ChatFeedHistoryBoundaryItemViewModel)
            {
                if (hasHistoricalVisible)
                {
                    target.Add(item);
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

            target.Add(item);
        }

        SyncVisibleItems(target);
        OnPropertyChanged(nameof(IsEmpty));
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
