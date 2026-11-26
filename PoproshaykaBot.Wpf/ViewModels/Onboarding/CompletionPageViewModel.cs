using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using KeepShell.ViewModels;
using MahApps.Metro.IconPacks;
using Microsoft.Extensions.Logging;
using PoproshaykaBot.Core.Infrastructure.Events;
using PoproshaykaBot.Core.Infrastructure.Events.Lifecycle;
using PoproshaykaBot.Core.Infrastructure.Hosting;
using PoproshaykaBot.Core.Platforms;
using PoproshaykaBot.Core.Platforms.Auth;
using PoproshaykaBot.Core.Server;
using PoproshaykaBot.Core.Settings;
using PoproshaykaBot.Core.Settings.Onboarding;
using PoproshaykaBot.Core.Settings.Stores;
using PoproshaykaBot.Core.Twitch.Auth;
using PoproshaykaBot.Core.Twitch.Chat;
using PoproshaykaBot.Core.Twitch.Onboarding;
using PoproshaykaBot.Core.YouTube.Auth;
using PoproshaykaBot.Wpf.Bootstrap;
using PoproshaykaBot.Wpf.Infrastructure;
using System.Collections.ObjectModel;

namespace PoproshaykaBot.Wpf.ViewModels.Onboarding;

public sealed partial class CompletionPageViewModel : OnboardingPageViewModelBase, IDisposable
{
    private readonly SettingsManager _settingsManager;
    private readonly AccountsStore _accountsStore;
    private readonly YouTubeSettingsStore? _youtubeSettingsStore;
    private readonly YouTubeAccountsStore? _youtubeAccountsStore;
    private readonly IEventBus _eventBus;
    private readonly KestrelHttpServer _kestrelHttpServer;
    private readonly IOnboardingChannelValidator _channelValidator;
    private readonly IBotConnectionController _botConnectionManager;
    private readonly ILogger<CompletionPageViewModel> _logger;
    private readonly IDialogService _dialogService;
    private readonly List<IDisposable> _subs = [];

    private OnboardingContext? _context;
    private CancellationTokenSource? _validationCts;
    private bool _hasCriticalIssue;
    private ChannelValidationResult _lastChannelCheckResult = ChannelValidationResult.Skipped;
    private PlatformAuthRole? _preferredChatDisplayAccount;
    private bool _suppressChatDisplaySync;

    [ObservableProperty]
    private string _summaryText = string.Empty;

    [ObservableProperty]
    private string _validationText = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PageTitle))]
    [NotifyCanExecuteChangedFor(nameof(RevalidateCommand))]
    private bool _isValidationPending;

    [ObservableProperty]
    private bool _isValidationWarning;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PageTitle))]
    private bool _isValidationFailure;

    [ObservableProperty]
    private bool _autoConnect = true;

    [ObservableProperty]
    private string _connectionAfterFinishText = "Подключить бота после завершения";

    [ObservableProperty]
    private PlatformAuthRole _chatDisplayAccount = PlatformAuthRole.Bot;

    [ObservableProperty]
    private bool _showTwitchChatDisplayOption = true;

    [ObservableProperty]
    private bool _canSelectTwitchChatDisplayAccount;

    [ObservableProperty]
    private bool _showSingleTwitchChatDisplayNotice;

    [ObservableProperty]
    private string _twitchBotChatDisplayLabel = string.Empty;

    [ObservableProperty]
    private string _twitchBroadcasterChatDisplayLabel = string.Empty;

    [ObservableProperty]
    private string _singleTwitchChatDisplayText = string.Empty;

    public CompletionPageViewModel(
        SettingsManager settingsManager,
        AccountsStore accountsStore,
        IEventBus eventBus,
        KestrelHttpServer kestrelHttpServer,
        IOnboardingChannelValidator channelValidator,
        IBotConnectionController botConnectionManager,
        ILogger<CompletionPageViewModel> logger,
        IDialogService dialogService,
        YouTubeSettingsStore? youtubeSettingsStore = null,
        YouTubeAccountsStore? youtubeAccountsStore = null)
    {
        _settingsManager = settingsManager;
        _accountsStore = accountsStore;
        _youtubeSettingsStore = youtubeSettingsStore;
        _youtubeAccountsStore = youtubeAccountsStore;
        _eventBus = eventBus;
        _kestrelHttpServer = kestrelHttpServer;
        _channelValidator = channelValidator;
        _botConnectionManager = botConnectionManager;
        _logger = logger;
        _dialogService = dialogService;

        _subs.Add(eventBus.SubscribeOnUi<BotLifecyclePhaseChanged>(_ => UpdateConnectionAfterFinishText()));
        _subs.Add(eventBus.SubscribeOnUi<PlatformConnectionChanged>(_ => UpdateConnectionAfterFinishText()));
    }

    public event Action<OnboardingStep>? NavigateToStepRequested;

    public ObservableCollection<CompletionValidationItem> ValidationItems { get; } = [];

    public override string PageTitle => IsValidationPending
        ? "Проверка настроек"
        : IsValidationFailure
            ? "Нужно исправить настройки"
            : "Готово к запуску";

    public override void OnEnter(OnboardingContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        if (!ReferenceEquals(_context, context))
        {
            _preferredChatDisplayAccount = context.Settings.Twitch.ChatDisplayAccount;
        }

        _context = context;
        RefreshContextSummary(context);
        UpdateConnectionAfterFinishText();
        StartValidation();
    }

    public override async Task<bool> OnLeavingAsync(OnboardingContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        if (_hasCriticalIssue)
        {
            return false;
        }

        if (context.Settings.Twitch.Enabled)
        {
            var bot = context.CurrentTwitchAccount(_accountsStore, PlatformAuthRole.Bot);
            if (ValidateTwitchRole(context, PlatformAuthRole.Bot, bot).Status == ValidationStatus.Failure)
            {
                return ReportNotWritten("Вход бота в Twitch больше недоступен. Вернитесь к авторизации бота и войдите снова.");
            }

            if (_lastChannelCheckResult == ChannelValidationResult.NotFound
                && !ConfirmIgnoreChannelNotFound(context))
            {
                return false;
            }
        }

        if (context.YouTubeSettings.Enabled
            && !PlatformAuthRoles.HasCredentials(context.YouTubeBotAccount.AccessToken, context.YouTubeBotAccount.RefreshToken))
        {
            return ReportNotWritten("Вход бота в YouTube больше недоступен. Вернитесь к настройке YouTube и войдите снова.");
        }

        if (context.Settings.Twitch.Enabled)
        {
            var (hasBot, hasBroadcaster) = GetAuthorizedTwitchRoles(context);
            context.Settings.Twitch.ChatDisplayAccount = ResolveEffectiveChatDisplayAccount(context, hasBot, hasBroadcaster);
        }

        var oldPort = _settingsManager.Current.Twitch.HttpServerPort;
        var newPort = context.Settings.Twitch.HttpServerPort;
        var portChanged = oldPort != newPort;

        try
        {
            if (context.SaveSetup(_settingsManager, _accountsStore, _youtubeSettingsStore, _youtubeAccountsStore) is { } error)
            {
                return ReportNotWritten(error);
            }
        }
        catch (Exception exception)
        {
            _logger.OnboardingSettingsSaveFailed(exception);
            _dialogService.Error("Настройки не сохранены", context.HasIncompleteRollback
                ? OnboardingContext.RollbackIncomplete
                : "Не удалось завершить сохранение. Повторите попытку или отмените настройку.");
            return false;
        }

        await _eventBus.PublishAsync(new PlatformAuthorizationRefreshed(StreamingPlatform.Twitch, PlatformAuthRole.Bot,
            context.TwitchContextId(PlatformAuthRole.Bot)), CancellationToken.None);
        await _eventBus.PublishAsync(new PlatformAuthorizationRefreshed(StreamingPlatform.Twitch, PlatformAuthRole.Broadcaster,
            context.TwitchContextId(PlatformAuthRole.Broadcaster)), CancellationToken.None);
        await _eventBus.PublishAsync(new PlatformAuthorizationRefreshed(StreamingPlatform.YouTube, PlatformAuthRole.Bot,
            context.YouTubeContextId(PlatformAuthRole.Bot)), CancellationToken.None);
        await _eventBus.PublishAsync(new PlatformAuthorizationRefreshed(StreamingPlatform.YouTube, PlatformAuthRole.Broadcaster,
            context.YouTubeContextId(PlatformAuthRole.Broadcaster)), CancellationToken.None);

        if (portChanged)
        {
            await TryRestartHttpServerAsync(newPort);
        }

        if (_botConnectionManager is not null)
        {
            if (AutoConnect)
            {
                if (NeedsBotConnection(context))
                {
                    try
                    {
                        _botConnectionManager.StartConnection();
                    }
                    catch (InvalidOperationException exception)
                    {
                        _logger.OnboardingAutoConnectRejected(exception);
                    }
                }
            }
            else if (HasActiveOrConnectingBot(context))
            {
                try
                {
                    await _botConnectionManager.StopAsync();
                }
                catch (Exception exception)
                {
                    _logger.OnboardingBotStopFailed(exception);
                    _dialogService.Warning("Бот не отключён", "Не удалось отключить бота. Повторите попытку.");
                    return false;
                }
            }
        }

        return true;
    }

    public void Dispose()
    {
        foreach (var sub in _subs)
        {
            sub.Dispose();
        }

        _subs.Clear();
        _validationCts?.Cancel();
        _validationCts?.Dispose();
        _validationCts = null;
    }

    [RelayCommand]
    private void FixIssue(OnboardingStep step)
    {
        if (step != OnboardingStep.None)
        {
            NavigateToStepRequested?.Invoke(step);
        }
    }

    [RelayCommand(CanExecute = nameof(CanRevalidate))]
    private void Revalidate()
    {
        if (_context is null)
        {
            return;
        }

        RefreshContextSummary(_context);
        UpdateConnectionAfterFinishText();
        StartValidation();
    }

    private bool CanRevalidate() => _context is not null && !IsValidationPending;

    partial void OnChatDisplayAccountChanged(PlatformAuthRole value)
    {
        if (_context is not null && !_suppressChatDisplaySync)
        {
            _preferredChatDisplayAccount = value;
            _context.Settings.Twitch.ChatDisplayAccount = value;
        }
    }

    private void StartValidation()
    {
        CanAdvance = false;
        _hasCriticalIssue = false;
        _lastChannelCheckResult = ChannelValidationResult.Skipped;

        UpdateValidation([new("Проверка настроек", ValidationStatus.Pending, "выполняется…")]);

        _validationCts?.Cancel();
        _validationCts?.Dispose();
        _validationCts = new CancellationTokenSource();

        RevalidateCommand.NotifyCanExecuteChanged();
        _ = RunValidationsAsync(_validationCts.Token);
    }

    private void RefreshContextSummary(OnboardingContext context)
    {
        var summaryLines = new List<string>();
        if (context.Settings.Twitch.Enabled)
        {
            var bot = context.CurrentTwitchAccount(_accountsStore, PlatformAuthRole.Bot);
            var broadcaster = context.CurrentTwitchAccount(_accountsStore, PlatformAuthRole.Broadcaster);
            var botText = string.IsNullOrWhiteSpace(bot.Login) ? "не подключён" : $"@{bot.Login.Trim()}";
            var broadcasterText = string.IsNullOrWhiteSpace(broadcaster.Login) ? "не подключён" : $"@{broadcaster.Login.Trim()}";
            var channel = string.IsNullOrWhiteSpace(context.Settings.Twitch.Channel) ? "не задан" : context.Settings.Twitch.Channel.Trim();
            summaryLines.Add($"Twitch: канал {channel} (бот: {botText}, стример: {broadcasterText})");
        }

        if (context.YouTubeSettings.Enabled)
        {
            var ytBot = string.IsNullOrWhiteSpace(context.YouTubeBotAccount.ChannelTitle) ? "не подключён" : context.YouTubeBotAccount.ChannelTitle.Trim();
            var ytBroadcaster = string.IsNullOrWhiteSpace(context.YouTubeBroadcasterAccount.ChannelTitle) ? "не подключён" : context.YouTubeBroadcasterAccount.ChannelTitle.Trim();
            var ytChannel = string.IsNullOrWhiteSpace(context.YouTubeSettings.Channel) ? "не задан" : context.YouTubeSettings.Channel.Trim();
            summaryLines.Add($"YouTube: {ytChannel} (бот: {ytBot}, стример: {ytBroadcaster})");
        }

        SummaryText = string.Join(Environment.NewLine, summaryLines);
        UpdateTwitchChatDisplayState(context);
    }

    private void UpdateTwitchChatDisplayState(OnboardingContext context)
    {
        if (!context.Settings.Twitch.Enabled)
        {
            ShowTwitchChatDisplayOption = false;
            CanSelectTwitchChatDisplayAccount = false;
            ShowSingleTwitchChatDisplayNotice = false;
            TwitchBotChatDisplayLabel = string.Empty;
            TwitchBroadcasterChatDisplayLabel = string.Empty;
            SingleTwitchChatDisplayText = string.Empty;
            return;
        }

        var (hasBot, hasBroadcaster, bot, broadcaster) = InspectTwitchRoles(context);

        if (hasBot && hasBroadcaster)
        {
            ShowTwitchChatDisplayOption = true;
            CanSelectTwitchChatDisplayAccount = true;
            ShowSingleTwitchChatDisplayNotice = false;
            TwitchBotChatDisplayLabel = $"Аккаунт бота (@{bot.Login.Trim()})";
            TwitchBroadcasterChatDisplayLabel = $"Аккаунт стримера (@{broadcaster.Login.Trim()})";
            SingleTwitchChatDisplayText = string.Empty;
            SetDisplayedChatDisplayAccount(_preferredChatDisplayAccount ?? context.Settings.Twitch.ChatDisplayAccount);
            return;
        }

        if (hasBot)
        {
            ShowTwitchChatDisplayOption = true;
            CanSelectTwitchChatDisplayAccount = false;
            ShowSingleTwitchChatDisplayNotice = true;
            TwitchBotChatDisplayLabel = string.Empty;
            TwitchBroadcasterChatDisplayLabel = string.Empty;
            SingleTwitchChatDisplayText = $"Чат работает от имени бота (@{bot.Login.Trim()})";
            SetDisplayedChatDisplayAccount(PlatformAuthRole.Bot);
            return;
        }

        if (hasBroadcaster)
        {
            ShowTwitchChatDisplayOption = true;
            CanSelectTwitchChatDisplayAccount = false;
            ShowSingleTwitchChatDisplayNotice = true;
            TwitchBotChatDisplayLabel = string.Empty;
            TwitchBroadcasterChatDisplayLabel = string.Empty;
            SingleTwitchChatDisplayText = $"Чат работает от имени стримера (@{broadcaster.Login.Trim()})";
            SetDisplayedChatDisplayAccount(PlatformAuthRole.Broadcaster);
            return;
        }

        ShowTwitchChatDisplayOption = false;
        CanSelectTwitchChatDisplayAccount = false;
        ShowSingleTwitchChatDisplayNotice = false;
        TwitchBotChatDisplayLabel = string.Empty;
        TwitchBroadcasterChatDisplayLabel = string.Empty;
        SingleTwitchChatDisplayText = string.Empty;
        SetDisplayedChatDisplayAccount(PlatformAuthRole.Bot);
    }

    private (bool HasBot, bool HasBroadcaster) GetAuthorizedTwitchRoles(OnboardingContext context)
    {
        var (hasBot, hasBroadcaster, _, _) = InspectTwitchRoles(context);
        return (hasBot, hasBroadcaster);
    }

    private (bool HasBot, bool HasBroadcaster, TwitchAccountSettings Bot, TwitchAccountSettings Broadcaster) InspectTwitchRoles(OnboardingContext context)
    {
        var bot = context.CurrentTwitchAccount(_accountsStore, PlatformAuthRole.Bot);
        var broadcaster = context.CurrentTwitchAccount(_accountsStore, PlatformAuthRole.Broadcaster);
        var hasBot = TwitchScopes.HasUsableAuthorization(PlatformAuthRole.Bot, bot)
            && context.CanUseTwitchRole(PlatformAuthRole.Bot, bot)
            && !string.IsNullOrWhiteSpace(bot.Login);
        var hasBroadcaster = TwitchScopes.HasUsableAuthorization(PlatformAuthRole.Broadcaster, broadcaster)
            && context.CanUseTwitchRole(PlatformAuthRole.Broadcaster, broadcaster)
            && !string.IsNullOrWhiteSpace(broadcaster.Login);
        return (hasBot, hasBroadcaster, bot, broadcaster);
    }

    private PlatformAuthRole ResolveEffectiveChatDisplayAccount(OnboardingContext context, bool hasBot, bool hasBroadcaster)
    {
        if (hasBot && hasBroadcaster)
        {
            return _preferredChatDisplayAccount ?? context.Settings.Twitch.ChatDisplayAccount;
        }

        return hasBroadcaster ? PlatformAuthRole.Broadcaster : PlatformAuthRole.Bot;
    }

    private void SetDisplayedChatDisplayAccount(PlatformAuthRole role)
    {
        _suppressChatDisplaySync = true;
        try
        {
            ChatDisplayAccount = role;
        }
        finally
        {
            _suppressChatDisplaySync = false;
        }
    }

    private void UpdateConnectionAfterFinishText()
    {
        ConnectionAfterFinishText = _context is not null && HasActiveOrConnectingBot(_context)
            ? "Оставить бота подключённым после завершения"
            : "Подключить бота после завершения";
    }

    private bool HasActiveOrConnectingBot(OnboardingContext context)
    {
        if (_botConnectionManager is null)
        {
            return false;
        }

        if (_botConnectionManager.IsBusy
            || _botConnectionManager.CurrentPhase is BotLifecyclePhase.Connected or BotLifecyclePhase.Connecting or BotLifecyclePhase.Disconnecting)
        {
            return true;
        }

        if (context.Settings.Twitch.Enabled && IsPlatformActive(_botConnectionManager, StreamingPlatform.Twitch))
        {
            return true;
        }

        return context.YouTubeSettings.Enabled && IsPlatformActive(_botConnectionManager, StreamingPlatform.YouTube);
    }

    private bool NeedsBotConnection(OnboardingContext context)
    {
        if (_botConnectionManager is null)
        {
            return false;
        }

        if (_botConnectionManager.CurrentPhase != BotLifecyclePhase.Connected)
        {
            return true;
        }

        if (context.Settings.Twitch.Enabled
            && _botConnectionManager.GetPhase(StreamingPlatform.Twitch) != BotLifecyclePhase.Connected
            && !_botConnectionManager.IsPlatformBusy(StreamingPlatform.Twitch))
        {
            return true;
        }

        return context.YouTubeSettings.Enabled
            && _botConnectionManager.GetPhase(StreamingPlatform.YouTube) != BotLifecyclePhase.Connected
            && !_botConnectionManager.IsPlatformBusy(StreamingPlatform.YouTube);
    }

    private static bool IsPlatformActive(IBotConnectionController controller, StreamingPlatform platform) =>
        controller.IsPlatformBusy(platform)
        || controller.GetPhase(platform) is BotLifecyclePhase.Connected or BotLifecyclePhase.Connecting or BotLifecyclePhase.Disconnecting;

    private async Task RunValidationsAsync(CancellationToken cancellationToken)
    {
        try
        {
            await RunValidationsCoreAsync(cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            _logger.OnboardingCompletionCheckFailed(exception);
            if (cancellationToken.IsCancellationRequested)
            {
                return;
            }

            _hasCriticalIssue = true;
            UpdateValidation([new("Проверка настроек", ValidationStatus.Failure, "повторите проверку или вернитесь к параметрам площадок", OnboardingStep.Platforms)]);
            CanAdvance = false;
        }
    }

    private async Task RunValidationsCoreAsync(CancellationToken cancellationToken)
    {
        if (_context is null)
        {
            return;
        }

        var lines = new List<ValidationLine>();
        var hasCritical = false;

        if (!_context.Settings.Twitch.Enabled && !_context.YouTubeSettings.Enabled)
        {
            lines.Add(new("Площадки трансляции", ValidationStatus.Failure, "выберите хотя бы одну площадку", OnboardingStep.Platforms));
            UpdateValidation(lines);
            _hasCriticalIssue = true;
            CanAdvance = false;
            return;
        }

        if (_context.Settings.Twitch.Enabled)
        {
            var twitchBot = _context.CurrentTwitchAccount(_accountsStore, PlatformAuthRole.Bot);
            var twitchBroadcaster = _context.CurrentTwitchAccount(_accountsStore, PlatformAuthRole.Broadcaster);
            var botScopes = ValidateTwitchRole(_context, PlatformAuthRole.Bot, twitchBot);
            lines.Add(botScopes);
            hasCritical |= botScopes.Status == ValidationStatus.Failure;

            var broadcasterScopes = ValidateTwitchRole(_context, PlatformAuthRole.Broadcaster, twitchBroadcaster);
            lines.Add(broadcasterScopes);

            if (broadcasterScopes.Status == ValidationStatus.Success)
            {
                lines.Add(ValidateBroadcasterLogin(_context, twitchBroadcaster));
            }
        }

        if (_context.YouTubeSettings.Enabled)
        {
            var ytChannelConfigured = !string.IsNullOrWhiteSpace(_context.YouTubeSettings.Channel);
            var ytChannelLine = ytChannelConfigured
                ? new ValidationLine("Канал YouTube", ValidationStatus.Success, _context.YouTubeSettings.Channel.Trim())
                : new ValidationLine("Канал YouTube", ValidationStatus.Failure, "укажите канал на шаге настройки YouTube", OnboardingStep.YouTube);
            lines.Add(ytChannelLine);
            hasCritical |= ytChannelLine.Status == ValidationStatus.Failure;

            var youtubeBot = _context.YouTubeBotAccount;
            var youtubeBroadcaster = _context.YouTubeBroadcasterAccount;
            var ytBotScopes = ValidateRole("Доступы YouTube бота", youtubeBot.AccessToken, youtubeBot.RefreshToken,
                youtubeBot.StoredScopes, youtubeBot.Scopes, YouTubeScopes.BotRequired, optional: false,
                targetStep: OnboardingStep.YouTube);
            lines.Add(ytBotScopes);
            hasCritical |= ytBotScopes.Status == ValidationStatus.Failure;

            var ytBroadcasterScopes = ValidateRole("Доступы YouTube стримера", youtubeBroadcaster.AccessToken, youtubeBroadcaster.RefreshToken,
                youtubeBroadcaster.StoredScopes, youtubeBroadcaster.Scopes, YouTubeScopes.BroadcasterRequired, optional: true,
                allowReadOnlyYouTube: true, targetStep: OnboardingStep.YouTube);
            lines.Add(ytBroadcasterScopes);
            hasCritical |= ytBroadcasterScopes.Status == ValidationStatus.Failure;
        }

        if (_context.Settings.Twitch.Enabled)
        {
            var channelLine = new ValidationLine("Канал на Twitch", ValidationStatus.Pending, "проверка…");
            lines.Add(channelLine);

            UpdateValidation(lines);

            var (channelStatus, channelDetail, channelResult, channelStep) =
                await CheckChannelExistsAsync(_context, cancellationToken);

            if (cancellationToken.IsCancellationRequested)
            {
                return;
            }

            _lastChannelCheckResult = channelResult;
            lines[^1] = channelLine with { Status = channelStatus, Detail = channelDetail, TargetStep = channelStep };
            hasCritical |= channelStatus == ValidationStatus.Failure;
        }

        UpdateValidation(lines);

        _hasCriticalIssue = hasCritical;
        CanAdvance = !hasCritical;
    }

    private async Task<(ValidationStatus Status, string Detail, ChannelValidationResult Result, OnboardingStep TargetStep)> CheckChannelExistsAsync(
        OnboardingContext context,
        CancellationToken cancellationToken)
    {
        var channel = context.Settings.Twitch.Channel?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(channel))
        {
            return (ValidationStatus.Skipped, "пропущено: канал не задан", ChannelValidationResult.Skipped, OnboardingStep.None);
        }

        var clientId = context.Settings.Twitch.ClientId;
        var accessToken = context.CurrentTwitchAccount(_accountsStore, PlatformAuthRole.Bot).AccessToken;
        if (string.IsNullOrWhiteSpace(clientId) || string.IsNullOrWhiteSpace(accessToken))
        {
            return (ValidationStatus.Skipped, "пропущено: нет client id или токена бота", ChannelValidationResult.Skipped, OnboardingStep.None);
        }

        try
        {
            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeoutCts.CancelAfter(TimeSpan.FromSeconds(8));

            var result = await _channelValidator.ValidateAsync(channel, clientId, accessToken, timeoutCts.Token);

            return result switch
            {
                ChannelValidationResult.Found =>
                    (ValidationStatus.Success, $"найден @{channel}", result, OnboardingStep.None),
                ChannelValidationResult.NotFound =>
                    (ValidationStatus.Failure,
                        $"канал «{channel}» не найден. Возможно, имя написано с ошибкой – вернитесь на шаг настройки Twitch.",
                        result,
                        OnboardingStep.Credentials),
                _ =>
                    (ValidationStatus.Skipped, "не удалось проверить (сеть)", result, OnboardingStep.None),
            };
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return (ValidationStatus.Skipped, "превышено время ожидания", ChannelValidationResult.Skipped, OnboardingStep.None);
        }
        catch (Exception exception)
        {
            _logger.OnboardingChannelCheckFailed(exception, channel);
            return (ValidationStatus.Skipped, "ошибка при проверке", ChannelValidationResult.Skipped, OnboardingStep.None);
        }
    }

    private void UpdateValidation(List<ValidationLine> lines)
    {
        ValidationItems.Clear();
        foreach (var line in lines)
        {
            ValidationItems.Add(ToValidationItem(line));
        }

        ValidationText = string.Join(Environment.NewLine, lines.Select(static l => $"{l.Title}: {l.Detail}"));

        var anyFailure = lines.Any(l => l.Status == ValidationStatus.Failure);
        var anyWarning = lines.Any(l => l.Status == ValidationStatus.Warning);
        var anyPending = lines.Any(l => l.Status == ValidationStatus.Pending);

        IsValidationFailure = anyFailure;
        IsValidationWarning = !anyFailure && anyWarning;
        IsValidationPending = anyPending;
    }

    private bool ConfirmIgnoreChannelNotFound(OnboardingContext context)
    {
        return _dialogService.ConfirmWarning(
            "Канал не найден",
            $"Канал «{context.Settings.Twitch.Channel}» не найден на Twitch. Возможно, имя написано с ошибкой.\n\nВсё равно сохранить настройки?");
    }

    private bool ReportNotWritten(string message)
    {
        _logger.OnboardingCompletionNotWritten(message);
        _dialogService.Warning("Настройка не завершена", message);
        return false;
    }

    private async Task TryRestartHttpServerAsync(int newPort)
    {
        try
        {
            if (_kestrelHttpServer.IsRunning)
            {
                await _kestrelHttpServer.StopAsync();
            }

            await _kestrelHttpServer.StartAsync();
        }
        catch (Exception exception)
        {
            _logger.OnboardingHttpServerRestartFailed(exception, newPort);
            _dialogService.Warning(
                "Внимание",
                $"HTTP-сервер не удалось перезапустить на порту {newPort}.\nНастройки сохранены – потребуется перезапуск приложения.");
        }
    }

    private static ValidationLine ValidateTwitchRole(OnboardingContext context, PlatformAuthRole role, TwitchAccountSettings account)
    {
        var isBot = role == PlatformAuthRole.Bot;
        var title = isBot ? "Доступы аккаунта бота" : "Доступы аккаунта стримера";
        if (TwitchScopes.HasUsableAuthorization(role, account) && context.CanUseTwitchRole(role, account))
        {
            return new(title, ValidationStatus.Success, "все необходимые разрешения получены");
        }

        return isBot
            ? new(title, ValidationStatus.Failure, "вернитесь к авторизации бота и войдите в Twitch", OnboardingStep.BotAuthorization)
            : new(title, ValidationStatus.Skipped, "не подключён (режим «только бот»)");
    }

    private static ValidationLine ValidateRole(string title, string? accessToken, string? refreshToken,
        IReadOnlyCollection<string> granted, IReadOnlyCollection<string> requested, IReadOnlyList<string> required, bool optional,
        bool allowReadOnlyYouTube = false, OnboardingStep targetStep = OnboardingStep.None)
    {
        if (!PlatformAuthRoles.HasCredentials(accessToken, refreshToken))
        {
            return optional
                ? new(title, ValidationStatus.Skipped, "не подключён (режим «только бот»)")
                : new(title, ValidationStatus.Failure, "требуется авторизация бота", targetStep);
        }

        var actual = granted.Count > 0 ? granted : requested;
        var result = ValidateScopes(title, actual, required, targetStep);
        if (result.Status == ValidationStatus.Failure
            && allowReadOnlyYouTube
            && YouTubeScopes.ContainsUsableScopes(PlatformAuthRole.Broadcaster, actual))
        {
            return new(title, ValidationStatus.Warning,
                "доступно чтение данных эфира (youtube.readonly); для обновления названия и описания нужна полная авторизация (youtube.force-ssl)",
                targetStep);
        }

        return result;
    }

    private static ValidationLine ValidateScopes(string title, IReadOnlyCollection<string> actual, IReadOnlyList<string> required,
        OnboardingStep targetStep)
    {
        var actualSet = new HashSet<string>(actual, StringComparer.Ordinal);
        var missing = required.Where(scope => !actualSet.Contains(scope)).ToArray();

        if (missing.Length == 0)
        {
            return new(title, ValidationStatus.Success, "все необходимые scope получены");
        }

        return new(title,
            ValidationStatus.Failure,
            $"отсутствуют scope: {string.Join(", ", missing)}. Вернитесь на шаг авторизации и переавторизуйтесь.",
            targetStep);
    }

    private static ValidationLine ValidateBroadcasterLogin(OnboardingContext context, TwitchAccountSettings broadcaster)
    {
        var channel = context.Settings.Twitch.Channel?.Trim() ?? string.Empty;
        var broadcasterLogin = broadcaster.Login?.Trim() ?? string.Empty;

        if (string.IsNullOrWhiteSpace(channel) || string.IsNullOrWhiteSpace(broadcasterLogin))
        {
            return new("Канал и стример", ValidationStatus.Skipped, "пропущено: не заданы поля");
        }

        if (string.Equals(channel, broadcasterLogin, StringComparison.OrdinalIgnoreCase))
        {
            return new("Канал и стример", ValidationStatus.Success, $"@{broadcasterLogin}");
        }

        return new("Канал и стример",
            ValidationStatus.Warning,
            $"канал «{channel}» и логин стримера «@{broadcasterLogin}» различаются – проверьте, что это намеренно.",
            OnboardingStep.BroadcasterAuthorization);
    }

    private static CompletionValidationItem ToValidationItem(ValidationLine line)
    {
        var (statusLabel, severity, icon) = line.Status switch
        {
            ValidationStatus.Success => ("Готово", StatusSeverity.Success, PackIconLucideKind.Check),
            ValidationStatus.Warning => ("Предупреждение", StatusSeverity.Warning, PackIconLucideKind.TriangleAlert),
            ValidationStatus.Failure => ("Ошибка", StatusSeverity.Error, PackIconLucideKind.X),
            ValidationStatus.Pending => ("Проверка", StatusSeverity.Info, PackIconLucideKind.Clock),
            _ => ("Пропущено", StatusSeverity.None, PackIconLucideKind.Info),
        };

        return new CompletionValidationItem(
            line.Title,
            line.Detail,
            statusLabel,
            severity,
            icon,
            line.TargetStep);
    }

    private enum ValidationStatus
    {
        None = 0,
        Pending = 1,
        Success = 2,
        Warning = 3,
        Failure = 4,
        Skipped = 5,
    }

    private readonly record struct ValidationLine(
        string Title,
        ValidationStatus Status,
        string Detail,
        OnboardingStep TargetStep = OnboardingStep.None);
}

public sealed record CompletionValidationItem(
    string Title,
    string Detail,
    string StatusLabel,
    StatusSeverity Severity,
    PackIconLucideKind IconKind,
    OnboardingStep TargetStep)
{
    public bool CanFix => TargetStep != OnboardingStep.None;

    public string FixAutomationName => $"Исправить: {Title}";
}
