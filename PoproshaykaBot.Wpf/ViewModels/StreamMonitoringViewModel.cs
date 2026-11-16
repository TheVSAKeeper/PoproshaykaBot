using CommunityToolkit.Mvvm.ComponentModel;
using PoproshaykaBot.Core.Infrastructure.Events;
using PoproshaykaBot.Core.Infrastructure.Events.Streaming;
using PoproshaykaBot.Core.Platforms;
using PoproshaykaBot.Core.Platforms.Auth;
using PoproshaykaBot.Core.Streaming;
using PoproshaykaBot.Wpf.Infrastructure;
using PoproshaykaBot.Wpf.Infrastructure.Platforms;

namespace PoproshaykaBot.Wpf.ViewModels;

public sealed partial class StreamMonitoringViewModel : ObservableObject, IDisposable
{
    private readonly IChatPlatformRegistry? _platformRegistry;
    private readonly Dictionary<StreamingPlatform, PlatformMonitoringGroupViewModel> _groupsByPlatform;
    private readonly IDisposable _subscription;

    [ObservableProperty]
    private bool _isVisible;

    [ObservableProperty]
    private bool _showPlatformIcons;

    public StreamMonitoringViewModel(
        IEventBus eventBus,
        IChatPlatformRegistry? platformRegistry = null)
    {
        ArgumentNullException.ThrowIfNull(eventBus);

        _platformRegistry = platformRegistry;

        var groups = PlatformVisuals.OrderedPlatforms
            .Select(static platform => new PlatformMonitoringGroupViewModel(platform))
            .ToArray();

        Groups = groups;
        _groupsByPlatform = groups.ToDictionary(static group => group.Platform);
        _subscription = eventBus.SubscribeOnUi<StreamMonitoringStatusChanged>(Apply);
    }

    public IReadOnlyList<PlatformMonitoringGroupViewModel> Groups { get; }

    public void Dispose()
    {
        _subscription.Dispose();
    }

    private void Apply(StreamMonitoringStatusChanged statusEvent)
    {
        if (!_groupsByPlatform.TryGetValue(statusEvent.Platform, out var group))
        {
            return;
        }

        var resetOnDisconnect = statusEvent.Status == StreamMonitoringStatus.Disconnected
            && _platformRegistry?.Find(statusEvent.Platform) is { IsEnabled: false };

        group.Apply(statusEvent.Role, statusEvent.Status, statusEvent.Detail, resetOnDisconnect);

        var visibleCount = 0;
        var hasNonTwitchVisible = false;

        foreach (var item in Groups)
        {
            if (!item.IsVisible)
            {
                continue;
            }

            visibleCount++;

            if (item.Platform != StreamingPlatform.Twitch)
            {
                hasNonTwitchVisible = true;
            }
        }

        IsVisible = visibleCount > 0;
        ShowPlatformIcons = visibleCount > 1 || hasNonTwitchVisible;
    }
}

public sealed partial class PlatformMonitoringGroupViewModel : ObservableObject
{
    [ObservableProperty]
    private bool _isVisible;

    public PlatformMonitoringGroupViewModel(StreamingPlatform platform)
    {
        Platform = platform;
        DisplayName = PlatformVisuals.GetDisplayName(platform);
    }

    public StreamingPlatform Platform { get; }

    public string DisplayName { get; }

    public StreamMonitoringIndicatorViewModel Bot { get; } = new("Бот");

    public StreamMonitoringIndicatorViewModel Broadcaster { get; } = new("Стример");

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

        if (resetWhenDisconnected && status == StreamMonitoringStatus.Disconnected)
        {
            indicator.Reset();
        }
        else
        {
            indicator.Apply(status, detail);
        }

        IsVisible = Bot.IsReported || Broadcaster.IsReported;
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

    public void Reset()
    {
        IsReported = false;
        Text = Role;
        Severity = StatusSeverity.None;
        Tooltip = string.Empty;
    }
}
