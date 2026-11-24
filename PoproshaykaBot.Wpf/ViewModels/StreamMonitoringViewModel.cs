using CommunityToolkit.Mvvm.ComponentModel;
using PoproshaykaBot.Core.Infrastructure.Events;
using PoproshaykaBot.Core.Infrastructure.Events.Streaming;
using PoproshaykaBot.Core.Platforms;
using PoproshaykaBot.Core.Platforms.Auth;
using PoproshaykaBot.Core.Streaming;
using PoproshaykaBot.Wpf.Infrastructure;

namespace PoproshaykaBot.Wpf.ViewModels;

public sealed partial class StreamMonitoringViewModel : ObservableObject, IDisposable
{
    private readonly IChatPlatformRegistry _platformRegistry;
    private readonly Dictionary<StreamingPlatform, PlatformMonitoringGroupViewModel> _groupsByPlatform;
    private readonly IDisposable _subscription;
    private readonly IDisposable _channelSubscription;

    [ObservableProperty]
    private bool _isVisible;

    [ObservableProperty]
    private bool _showPlatformIcons;

    public StreamMonitoringViewModel(
        IEventBus eventBus,
        IChatPlatformRegistry platformRegistry)
    {
        ArgumentNullException.ThrowIfNull(eventBus);

        _platformRegistry = platformRegistry;

        var groups = platformRegistry.All
            .Select(static platform => new PlatformMonitoringGroupViewModel(
                platform.Id,
                platform.DisplayName,
                !platform.Capabilities.HasFlag(PlatformCapabilities.ChannelMetadataEdit)))
            .ToArray();

        Groups = groups;
        _groupsByPlatform = groups.ToDictionary(static group => group.Platform);
        _subscription = eventBus.SubscribeOnUi<StreamMonitoringStatusChanged>(Apply);
        _channelSubscription = eventBus.SubscribeOnUi<ChannelMonitoringStateChanged>(ApplyChannelState);
    }

    public IReadOnlyList<PlatformMonitoringGroupViewModel> Groups { get; }

    public void Dispose()
    {
        _subscription.Dispose();
        _channelSubscription.Dispose();
    }

    private void Apply(StreamMonitoringStatusChanged statusEvent)
    {
        if (!_groupsByPlatform.TryGetValue(statusEvent.Platform, out var group))
        {
            return;
        }

        var resetOnDisconnect = statusEvent.Status == StreamMonitoringStatus.Disconnected
            && _platformRegistry.Find(statusEvent.Platform) is { IsEnabled: false };

        group.Apply(statusEvent.Role, statusEvent.Status, statusEvent.Detail, resetOnDisconnect);
        RefreshVisibility();
    }

    private void ApplyChannelState(ChannelMonitoringStateChanged evt)
    {
        if (!_groupsByPlatform.TryGetValue(evt.Platform, out var group))
        {
            return;
        }

        group.ApplyChannelSnapshot(evt.Snapshot);
        RefreshVisibility();
    }

    private void RefreshVisibility()
    {
        var visibleCount = 0;
        var hasSecondaryVisible = false;
        var primaryPlatform = Groups.Count > 0 ? Groups[0].Platform : (StreamingPlatform?)null;

        foreach (var item in Groups)
        {
            if (!item.IsVisible)
            {
                continue;
            }

            visibleCount++;

            if (Groups.Count > 1 && item.Platform != primaryPlatform)
            {
                hasSecondaryVisible = true;
            }
        }

        IsVisible = visibleCount > 0;
        ShowPlatformIcons = visibleCount > 1 || hasSecondaryVisible;
    }
}

public sealed partial class PlatformMonitoringGroupViewModel : ObservableObject
{
    private readonly Dictionary<ChannelKey, ChannelMonitoringSnapshot> _channelSnapshots = [];
    private readonly bool _singleActiveRole;

    [ObservableProperty]
    private bool _isVisible;

    public PlatformMonitoringGroupViewModel(
        StreamingPlatform platform,
        string displayName,
        bool singleActiveRole = false)
    {
        Platform = platform;
        DisplayName = displayName;
        _singleActiveRole = singleActiveRole;
    }

    public StreamingPlatform Platform { get; }

    public string DisplayName { get; }

    public StreamMonitoringIndicatorViewModel Bot { get; } = new("Бот");

    public StreamMonitoringIndicatorViewModel Broadcaster { get; } = new("Стример");

    public StreamMonitoringIndicatorViewModel Channels { get; } = new("Каналы");

    public void Apply(
        PlatformAuthRole role,
        StreamMonitoringStatus status,
        string? detail,
        bool resetWhenDisconnected = false)
    {
        var indicator = role switch
        {
            PlatformAuthRole.Broadcaster => Broadcaster,
            _ => Bot,
        };

        var other = role == PlatformAuthRole.Broadcaster ? Bot : Broadcaster;
        var suppressSupersededDisconnect = _singleActiveRole
            && status == StreamMonitoringStatus.Disconnected
            && other.IsReported
            && other.Severity != StatusSeverity.None;

        if ((resetWhenDisconnected || suppressSupersededDisconnect) && status == StreamMonitoringStatus.Disconnected)
        {
            indicator.Reset();
        }
        else
        {
            indicator.Apply(status, detail);
            if (_singleActiveRole && status != StreamMonitoringStatus.Disconnected)
            {
                other.Reset();
            }
        }

        IsVisible = Bot.IsReported || Broadcaster.IsReported || Channels.IsReported;
    }

    public void ApplyChannelSnapshot(ChannelMonitoringSnapshot snapshot)
    {
        if (snapshot.Availability == ChannelMonitoringAvailability.Disabled)
        {
            _channelSnapshots.Remove(snapshot.Channel);
        }
        else
        {
            _channelSnapshots[snapshot.Channel] = snapshot;
        }

        if (_channelSnapshots.Count == 0)
        {
            Channels.Reset();
            IsVisible = Bot.IsReported || Broadcaster.IsReported;
            return;
        }

        var unavailable = _channelSnapshots.Values
            .Where(static s => s.Availability == ChannelMonitoringAvailability.Unavailable)
            .ToArray();
        var degraded = _channelSnapshots.Values
            .Where(static s => s.Availability == ChannelMonitoringAvailability.Degraded)
            .ToArray();

        if (unavailable.Length > 0)
        {
            var details = string.Join("; ", unavailable.Select(FormatSnapshotIssue));
            Channels.ApplyCustom($"Каналы: недоступно ({unavailable.Length})", StatusSeverity.Error, details);
        }
        else if (degraded.Length > 0)
        {
            var details = string.Join("; ", degraded.Select(FormatSnapshotIssue));
            Channels.ApplyCustom($"Каналы: ограничено ({degraded.Length})", StatusSeverity.Warning, details);
        }
        else if (_channelSnapshots.Count > 1)
        {
            var names = string.Join(", ", _channelSnapshots.Values.Select(static s => s.ChannelDisplayName));
            Channels.ApplyCustom($"Каналы: {_channelSnapshots.Count}", StatusSeverity.Success, $"Фоновый мониторинг: {names}");
        }
        else
        {
            Channels.Reset();
        }

        IsVisible = Bot.IsReported || Broadcaster.IsReported || Channels.IsReported;
    }

    private static string FormatSnapshotIssue(ChannelMonitoringSnapshot snapshot)
    {
        return string.IsNullOrWhiteSpace(snapshot.Detail)
            ? snapshot.ChannelDisplayName
            : $"{snapshot.ChannelDisplayName}: {snapshot.Detail}";
    }
}

public sealed partial class StreamMonitoringIndicatorViewModel : ObservableObject
{
    [ObservableProperty]
    private string _text;

    [ObservableProperty]
    private StatusSeverity _severity;

    [ObservableProperty]
    private string _tooltip = string.Empty;

    [ObservableProperty]
    private bool _isReported;

    public StreamMonitoringIndicatorViewModel(string role)
    {
        Role = role;
        _text = role;
    }

    public string Role { get; }

    public void Apply(StreamMonitoringStatus status, string? detail)
    {
        IsReported = true;

        var (label, severity) = status switch
        {
            StreamMonitoringStatus.Connecting => ("подключение", StatusSeverity.Info),
            StreamMonitoringStatus.Connected => ("работает", StatusSeverity.Success),
            StreamMonitoringStatus.Reconnecting => ("переподключение", StatusSeverity.Warning),
            StreamMonitoringStatus.Failed => ("ошибка", StatusSeverity.Error),
            _ => ("остановлен", StatusSeverity.None),
        };

        Text = $"{Role}: {label}";
        Severity = severity;
        Tooltip = string.IsNullOrWhiteSpace(detail)
            ? $"Мониторинг стрима, {Role.ToLowerInvariant()}: {label}"
            : $"Мониторинг стрима, {Role.ToLowerInvariant()}: {label} ({detail})";
    }

    public void ApplyCustom(string text, StatusSeverity severity, string tooltip)
    {
        IsReported = true;
        Text = text;
        Severity = severity;
        Tooltip = tooltip;
    }

    public void Reset()
    {
        IsReported = false;
        Text = Role;
        Severity = StatusSeverity.None;
        Tooltip = string.Empty;
    }
}
