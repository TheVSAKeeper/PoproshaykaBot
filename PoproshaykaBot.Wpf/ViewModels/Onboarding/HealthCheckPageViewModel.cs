using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using KeepShell.Services.Platform;
using KeepShell.ViewModels;
using Microsoft.Extensions.Logging;
using PoproshaykaBot.Core.Chat;
using PoproshaykaBot.Core.Infrastructure.Events;
using PoproshaykaBot.Core.Infrastructure.Events.Chat;
using PoproshaykaBot.Core.Infrastructure.Events.Lifecycle;
using PoproshaykaBot.Core.Platforms;
using PoproshaykaBot.Core.Platforms.Auth;
using PoproshaykaBot.Core.Twitch.Chat;
using PoproshaykaBot.Core.Users;
using PoproshaykaBot.Wpf.Bootstrap;
using PoproshaykaBot.Wpf.Infrastructure;
using PoproshaykaBot.Wpf.Infrastructure.Platforms;

namespace PoproshaykaBot.Wpf.ViewModels.Onboarding;

public sealed partial class HealthCheckPageViewModel : OnboardingPageViewModelBase, IDisposable
{
    private const string DefaultChatTestStatusText = "Не выполнено";

    private readonly IBroadcastMessenger _chatMessenger;
    private readonly ILogger<HealthCheckPageViewModel> _logger;
    private readonly IShellLauncher _shellLauncher;
    private readonly IClipboardService? _clipboard;
    private readonly List<IDisposable> _subs = [];
    private readonly Dictionary<StreamingPlatform, string> _botStatusByPlatform = [];
    private readonly Dictionary<StreamingPlatform, ChatTestResultState> _chatTestStateByPlatform = [];

    [ObservableProperty]
    private string _overlayUrl = string.Empty;

    [ObservableProperty]
    private string _overlayCopyFeedback = string.Empty;

    [ObservableProperty]
    private StatusSeverity _overlayCopyFeedbackSeverity = StatusSeverity.None;

    [ObservableProperty]
    private string _botStatusText = "Бот в чате: ожидание сообщения…";

    [ObservableProperty]
    private bool _botStatusKnown;

    [ObservableProperty]
    private bool _hasTwitchEnabled = true;

    [ObservableProperty]
    private bool _hasYouTubeEnabled;

    [ObservableProperty]
    private bool _showPlatformSelector;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsTwitchSelected))]
    [NotifyPropertyChangedFor(nameof(IsYouTubeSelected))]
    private StreamingPlatform _selectedPlatform = StreamingPlatform.Twitch;

    [ObservableProperty]
    private string _chatTestMessage = string.Empty;

    [ObservableProperty]
    private string _chatTestStatusText = DefaultChatTestStatusText;

    [ObservableProperty]
    private bool _chatTestSuccess;

    [ObservableProperty]
    private bool _chatTestError;

    [ObservableProperty]
    private StatusSeverity _chatTestSeverity = StatusSeverity.None;

    public HealthCheckPageViewModel(
        IBroadcastMessenger chatMessenger,
        IEventBus eventBus,
        ILogger<HealthCheckPageViewModel> logger,
        IShellLauncher shellLauncher,
        IClipboardService? clipboard = null)
    {
        _chatMessenger = chatMessenger;
        _logger = logger;
        _shellLauncher = shellLauncher;
        _clipboard = clipboard;

        _subs.Add(eventBus.SubscribeOnUi<ChatMessageReceived>(OnChatMessageReceived));
        _subs.Add(eventBus.SubscribeOnUi<PlatformAuthorizationRefreshed>(OnAuthorizationRefreshed));
    }

    public override string PageTitle => "Диагностика (опционально)";

    public IReadOnlyList<StreamingPlatform> EnabledPlatforms { get; private set; } = [StreamingPlatform.Twitch];

    public bool IsTwitchSelected
    {
        get => SelectedPlatform == StreamingPlatform.Twitch;
        set
        {
            if (value)
            {
                SelectedPlatform = StreamingPlatform.Twitch;
            }
        }
    }

    public bool IsYouTubeSelected
    {
        get => SelectedPlatform == StreamingPlatform.YouTube;
        set
        {
            if (value)
            {
                SelectedPlatform = StreamingPlatform.YouTube;
            }
        }
    }

    public string OverlayHint =>
        "Совет: в свойствах источника снимите «Shutdown source when not visible» и "
        + "«Refresh browser when scene becomes active», иначе оверлей перезагружается при смене сцены.";

    public override void OnEnter(OnboardingContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        OverlayUrl = $"http://localhost:{context.Settings.Twitch.HttpServerPort}/chat";
        OverlayCopyFeedback = string.Empty;
        OverlayCopyFeedbackSeverity = StatusSeverity.None;
        HasTwitchEnabled = context.Settings.Twitch.Enabled;
        HasYouTubeEnabled = context.YouTubeSettings.Enabled;

        var enabled = new List<StreamingPlatform>(2);
        if (HasTwitchEnabled)
        {
            enabled.Add(StreamingPlatform.Twitch);
        }

        if (HasYouTubeEnabled)
        {
            enabled.Add(StreamingPlatform.YouTube);
        }

        EnabledPlatforms = enabled;
        ShowPlatformSelector = enabled.Count > 1;
        _botStatusByPlatform.Clear();
        _chatTestStateByPlatform.Clear();

        var initialPlatform = enabled.Count > 0 ? enabled[0] : StreamingPlatform.Twitch;
        if (SelectedPlatform != initialPlatform)
        {
            SelectedPlatform = initialPlatform;
        }
        else
        {
            ApplySelectedPlatformState();
        }

        CanAdvance = true;
    }

    partial void OnSelectedPlatformChanged(StreamingPlatform value)
    {
        ApplySelectedPlatformState();
    }

    [RelayCommand]
    private void SendChatTest()
    {
        var message = ChatTestMessage.Trim();

        if (string.IsNullOrEmpty(message))
        {
            SetChatTestState(SelectedPlatform, new("Введите текст сообщения.", false, false, StatusSeverity.Warning));
            return;
        }

        try
        {
            var result = _chatMessenger.Send(SelectedPlatform, message);

            if (result is ChatEnqueueResult.Queued)
            {
                SetChatTestState(SelectedPlatform, new("Сообщение поставлено в очередь отправки.", true, false, StatusSeverity.Success));
            }
            else
            {
                SetChatTestState(SelectedPlatform, new($"Сообщение не отправлено: {ChatEnqueueResults.DescribeRefusal(result)}.", false, true, StatusSeverity.Error));
            }
        }
        catch (Exception exception)
        {
            _logger.OnboardingChatTestEnqueueFailed(exception);
            SetChatTestState(SelectedPlatform, new("Не удалось поставить сообщение в очередь.", false, true, StatusSeverity.Error));
        }
    }

    [RelayCommand]
    private void CopyOverlayUrl()
    {
        if (string.IsNullOrWhiteSpace(OverlayUrl))
        {
            return;
        }

        if (_clipboard?.TrySetText(OverlayUrl) == true)
        {
            OverlayCopyFeedback = "Ссылка скопирована";
            OverlayCopyFeedbackSeverity = StatusSeverity.Success;
            return;
        }

        OverlayCopyFeedback = "Не удалось скопировать ссылку.";
        OverlayCopyFeedbackSeverity = StatusSeverity.Error;
    }

    [RelayCommand]
    private void OpenOverlayInBrowser()
    {
        _shellLauncher.Open(OverlayUrl);
    }

    public void Dispose()
    {
        foreach (var sub in _subs)
        {
            sub.Dispose();
        }

        _subs.Clear();
    }

    private void OnChatMessageReceived(ChatMessageReceived @event)
    {
        if (!@event.IsBot)
        {
            return;
        }

        var status = DescribeBotStatus(@event.Status);
        _botStatusByPlatform[@event.Platform] = status;
        if (@event.Platform == SelectedPlatform)
        {
            ApplySelectedPlatformBotStatus();
        }
    }

    private void OnAuthorizationRefreshed(PlatformAuthorizationRefreshed @event)
    {
        if (!_botStatusByPlatform.Remove(@event.Platform))
        {
            return;
        }

        if (@event.Platform == SelectedPlatform)
        {
            ApplySelectedPlatformBotStatus();
        }
    }

    private void SetChatTestState(StreamingPlatform platform, ChatTestResultState state)
    {
        _chatTestStateByPlatform[platform] = state;
        if (platform == SelectedPlatform)
        {
            ApplyChatTestState(state);
        }
    }

    private void ApplySelectedPlatformState()
    {
        ApplySelectedPlatformBotStatus();
        var state = _chatTestStateByPlatform.TryGetValue(SelectedPlatform, out var saved)
            ? saved
            : ChatTestResultState.Initial;
        ApplyChatTestState(state);
    }

    private void ApplySelectedPlatformBotStatus()
    {
        var platformName = PlatformVisuals.GetDisplayName(SelectedPlatform);
        if (_botStatusByPlatform.TryGetValue(SelectedPlatform, out var status))
        {
            BotStatusKnown = true;
            BotStatusText = $"Бот в чате ({platformName}): {status}";
            return;
        }

        BotStatusKnown = false;
        BotStatusText = ShowPlatformSelector
            ? $"Бот в чате ({platformName}): ожидание сообщения…"
            : "Бот в чате: ожидание сообщения…";
    }

    private void ApplyChatTestState(ChatTestResultState state)
    {
        ChatTestStatusText = state.StatusText;
        ChatTestSuccess = state.IsSuccess;
        ChatTestError = state.IsError;
        ChatTestSeverity = state.Severity;
    }

    private static string DescribeBotStatus(UserStatus status)
    {
        if (status.HasFlag(UserStatus.Broadcaster))
        {
            return "владелец канала";
        }

        if (status.HasFlag(UserStatus.Moderator))
        {
            return "модератор";
        }

        return "обычный пользователь";
    }

    private readonly record struct ChatTestResultState(string StatusText, bool IsSuccess, bool IsError, StatusSeverity Severity)
    {
        public static ChatTestResultState Initial => new(DefaultChatTestStatusText, false, false, StatusSeverity.None);
    }
}
