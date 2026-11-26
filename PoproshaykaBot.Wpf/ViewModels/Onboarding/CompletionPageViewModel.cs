using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.Extensions.Logging;
using PoproshaykaBot.Core.Infrastructure.Events;
using PoproshaykaBot.Core.Infrastructure.Events.Lifecycle;
using PoproshaykaBot.Core.Infrastructure.Hosting;
using PoproshaykaBot.Core.Platforms;
using PoproshaykaBot.Core.Platforms.Auth;
using PoproshaykaBot.Core.Server;
using PoproshaykaBot.Core.Settings;
using PoproshaykaBot.Core.Settings.Stores;
using PoproshaykaBot.Core.Twitch.Auth;
using PoproshaykaBot.Core.Twitch.Chat;
using PoproshaykaBot.Core.Twitch.Onboarding;
using PoproshaykaBot.Core.YouTube.Auth;
using PoproshaykaBot.Wpf.Bootstrap;

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
    private readonly BotConnectionManager _botConnectionManager;
    private readonly ILogger<CompletionPageViewModel> _logger;
    private readonly IDialogService _dialogService;

    private OnboardingContext? _context;
    private CancellationTokenSource? _validationCts;
    private bool _hasCriticalIssue;
    private ChannelValidationResult _lastChannelCheckResult = ChannelValidationResult.Skipped;

    [ObservableProperty]
    private string _summaryText = string.Empty;

    [ObservableProperty]
    private string _validationText = string.Empty;

    [ObservableProperty]
    private bool _isValidationPending;

    [ObservableProperty]
    private bool _isValidationWarning;

    [ObservableProperty]
    private bool _isValidationFailure;

    [ObservableProperty]
    private bool _autoConnect = true;

    [ObservableProperty]
    private PlatformAuthRole _chatDisplayAccount = PlatformAuthRole.Bot;

    [ObservableProperty]
    private bool _showTwitchChatDisplayOption = true;

    public CompletionPageViewModel(
        SettingsManager settingsManager,
        AccountsStore accountsStore,
        IEventBus eventBus,
        KestrelHttpServer kestrelHttpServer,
        IOnboardingChannelValidator channelValidator,
        BotConnectionManager botConnectionManager,
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
    }

    public override string PageTitle => "Готово";

    public override void OnEnter(OnboardingContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        _context = context;
        ShowTwitchChatDisplayOption = context.Settings.Twitch.Enabled;

        var summaryLines = new List<string>();
        if (context.Settings.Twitch.Enabled)
        {
            var bot = context.CurrentTwitchAccount(_accountsStore, PlatformAuthRole.Bot);
            var broadcaster = context.CurrentTwitchAccount(_accountsStore, PlatformAuthRole.Broadcaster);
            var botLogin = string.IsNullOrWhiteSpace(bot.Login) ? "–" : bot.Login;
            var broadcasterLogin = string.IsNullOrWhiteSpace(broadcaster.Login) ? "–" : broadcaster.Login;
            var channel = string.IsNullOrWhiteSpace(context.Settings.Twitch.Channel) ? "–" : context.Settings.Twitch.Channel;
            summaryLines.Add($"Twitch: канал {channel} (бот: @{botLogin}, стример: @{broadcasterLogin})");
        }

        if (context.YouTubeSettings.Enabled)
        {
            var ytBot = string.IsNullOrWhiteSpace(context.YouTubeBotAccount.ChannelTitle) ? "–" : context.YouTubeBotAccount.ChannelTitle;
            var ytBroadcaster = string.IsNullOrWhiteSpace(context.YouTubeBroadcasterAccount.ChannelTitle) ? "–" : context.YouTubeBroadcasterAccount.ChannelTitle;
            var ytChannel = string.IsNullOrWhiteSpace(context.YouTubeSettings.Channel) ? "–" : context.YouTubeSettings.Channel;
            summaryLines.Add($"YouTube: {ytChannel} (бот: {ytBot}, стример: {ytBroadcaster})");
        }

        SummaryText = string.Join(Environment.NewLine, summaryLines);
        ChatDisplayAccount = context.Settings.Twitch.ChatDisplayAccount;

        CanAdvance = false;
        _hasCriticalIssue = false;
        _lastChannelCheckResult = ChannelValidationResult.Skipped;

        ValidationText = "Выполняется проверка…";
        IsValidationPending = true;
        IsValidationWarning = false;
        IsValidationFailure = false;

        _validationCts?.Cancel();
        _validationCts?.Dispose();
        _validationCts = new CancellationTokenSource();

        _ = RunValidationsAsync(_validationCts.Token);
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
        await _eventBus.PublishAsync(new PlatformAuthorizationRefreshed(StreamingPlatform.YouTube, PlatformAuthRole.Bot), CancellationToken.None);
        await _eventBus.PublishAsync(new PlatformAuthorizationRefreshed(StreamingPlatform.YouTube, PlatformAuthRole.Broadcaster), CancellationToken.None);

        if (portChanged)
        {
            await TryRestartHttpServerAsync(newPort);
        }

        if (AutoConnect && _botConnectionManager.CurrentPhase != BotLifecyclePhase.Connected)
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

        return true;
    }

    public void Dispose()
    {
        _validationCts?.Cancel();
        _validationCts?.Dispose();
        _validationCts = null;
    }

    partial void OnChatDisplayAccountChanged(PlatformAuthRole value)
    {
        if (_context is not null)
        {
            _context.Settings.Twitch.ChatDisplayAccount = value;
        }
    }

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
            UpdateValidation([new("Проверка настроек", ValidationStatus.Failure, "вернитесь на предыдущий шаг и повторите проверку")]);
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
            lines.Add(new("Площадки трансляции", ValidationStatus.Failure, "выберите хотя бы одну площадку"));
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
                : new ValidationLine("Канал YouTube", ValidationStatus.Failure, "укажите канал на шаге настройки YouTube");
            lines.Add(ytChannelLine);
            hasCritical |= ytChannelLine.Status == ValidationStatus.Failure;

            var youtubeBot = _context.YouTubeBotAccount;
            var youtubeBroadcaster = _context.YouTubeBroadcasterAccount;
            var ytBotScopes = ValidateRole("Доступы YouTube бота", youtubeBot.AccessToken, youtubeBot.RefreshToken,
                youtubeBot.StoredScopes, youtubeBot.Scopes, YouTubeScopes.BotRequired, optional: false);
            lines.Add(ytBotScopes);
            hasCritical |= ytBotScopes.Status == ValidationStatus.Failure;

            var ytBroadcasterScopes = ValidateRole("Доступы YouTube стримера", youtubeBroadcaster.AccessToken, youtubeBroadcaster.RefreshToken,
                youtubeBroadcaster.StoredScopes, youtubeBroadcaster.Scopes, YouTubeScopes.BroadcasterRequired, optional: true);
            lines.Add(ytBroadcasterScopes);
            hasCritical |= ytBroadcasterScopes.Status == ValidationStatus.Failure;
        }

        if (_context.Settings.Twitch.Enabled)
        {
            var channelLine = new ValidationLine("Канал на Twitch", ValidationStatus.Pending, "проверка…");
            lines.Add(channelLine);

            UpdateValidation(lines);

            var (channelStatus, channelDetail, channelResult) =
                await CheckChannelExistsAsync(_context, cancellationToken);

            if (cancellationToken.IsCancellationRequested)
            {
                return;
            }

            _lastChannelCheckResult = channelResult;
            lines[^1] = channelLine with { Status = channelStatus, Detail = channelDetail };
            hasCritical |= channelStatus == ValidationStatus.Failure;
        }

        UpdateValidation(lines);

        _hasCriticalIssue = hasCritical;
        CanAdvance = !hasCritical;
    }

    private async Task<(ValidationStatus Status, string Detail, ChannelValidationResult Result)> CheckChannelExistsAsync(
        OnboardingContext context,
        CancellationToken cancellationToken)
    {
        var channel = context.Settings.Twitch.Channel?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(channel))
        {
            return (ValidationStatus.Skipped, "пропущено: канал не задан", ChannelValidationResult.Skipped);
        }

        var clientId = context.Settings.Twitch.ClientId;
        var accessToken = context.CurrentTwitchAccount(_accountsStore, PlatformAuthRole.Bot).AccessToken;
        if (string.IsNullOrWhiteSpace(clientId) || string.IsNullOrWhiteSpace(accessToken))
        {
            return (ValidationStatus.Skipped, "пропущено: нет client id или токена бота", ChannelValidationResult.Skipped);
        }

        try
        {
            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeoutCts.CancelAfter(TimeSpan.FromSeconds(8));

            var result = await _channelValidator.ValidateAsync(channel, clientId, accessToken, timeoutCts.Token);

            return result switch
            {
                ChannelValidationResult.Found =>
                    (ValidationStatus.Success, $"найден @{channel}", result),
                ChannelValidationResult.NotFound =>
                    (ValidationStatus.Failure,
                        $"канал «{channel}» не найден. Возможно, имя написано с ошибкой – вернитесь на шаг настройки Twitch.",
                        result),
                _ =>
                    (ValidationStatus.Skipped, "не удалось проверить (сеть)", result),
            };
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return (ValidationStatus.Skipped, "превышено время ожидания", ChannelValidationResult.Skipped);
        }
        catch (Exception exception)
        {
            _logger.OnboardingChannelCheckFailed(exception, channel);
            return (ValidationStatus.Skipped, "ошибка при проверке", ChannelValidationResult.Skipped);
        }
    }

    private void UpdateValidation(List<ValidationLine> lines)
    {
        ValidationText = string.Join(Environment.NewLine, lines.Select(FormatLine));

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
            ? new(title, ValidationStatus.Failure, "вернитесь к авторизации бота и войдите в Twitch")
            : new(title, ValidationStatus.Skipped, "не подключён (режим «только бот»)");
    }

    private static ValidationLine ValidateRole(string title, string? accessToken, string? refreshToken,
        IReadOnlyCollection<string> granted, IReadOnlyCollection<string> requested, IReadOnlyList<string> required, bool optional)
    {
        if (!PlatformAuthRoles.HasCredentials(accessToken, refreshToken))
        {
            return optional
                ? new(title, ValidationStatus.Skipped, "не подключён (режим «только бот»)")
                : new(title, ValidationStatus.Failure, "требуется авторизация бота");
        }

        return ValidateScopes(title, granted.Count > 0 ? granted : requested, required);
    }

    private static ValidationLine ValidateScopes(string title, IReadOnlyCollection<string> actual, IReadOnlyList<string> required)
    {
        var actualSet = new HashSet<string>(actual, StringComparer.Ordinal);
        var missing = required.Where(scope => !actualSet.Contains(scope)).ToArray();

        if (missing.Length == 0)
        {
            return new(title, ValidationStatus.Success, "все необходимые scope получены");
        }

        return new(title,
            ValidationStatus.Failure,
            $"отсутствуют scope: {string.Join(", ", missing)}. Вернитесь на шаг авторизации и переавторизуйтесь.");
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
            $"канал «{channel}» и логин стримера «@{broadcasterLogin}» различаются – проверьте, что это намеренно.");
    }

    private static string FormatLine(ValidationLine line)
    {
        var prefix = line.Status switch
        {
            ValidationStatus.Success => "✓",
            ValidationStatus.Warning => "⚠",
            ValidationStatus.Failure => "✗",
            ValidationStatus.Pending => "⏳",
            _ => "–",
        };

        return $"{prefix} {line.Title}: {line.Detail}";
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

    private readonly record struct ValidationLine(string Title, ValidationStatus Status, string Detail);
}
