using CommunityToolkit.Mvvm.ComponentModel;
using PoproshaykaBot.Core.Platforms;
using PoproshaykaBot.Core.Platforms.Auth;
using PoproshaykaBot.Core.Streaming;
using PoproshaykaBot.Core.YouTube;
using PoproshaykaBot.Core.YouTube.Auth;
using PoproshaykaBot.Wpf.ViewModels.Settings;
using System.ComponentModel;
using System.Net;
using System.Net.Http;

namespace PoproshaykaBot.Wpf.ViewModels.Onboarding;

public sealed partial class YouTubeOnboardingPageViewModel : OnboardingPageViewModelBase, IDisposable
{
    private readonly IYouTubeChannelIdentityClient? _identityClient;
    private readonly IPlatformChannelAdapter? _youtubeAdapter;
    private OnboardingContext? _context;
    private string _channelResolutionError = string.Empty;
    private string _lastChannelInput = string.Empty;
    private bool _isResolvingChannel;

    [ObservableProperty]
    private string _validationHint = string.Empty;

    public YouTubeOnboardingPageViewModel(
        YouTubeSettingsSectionViewModel editor,
        IYouTubeChannelIdentityClient? identityClient = null,
        IEnumerable<IPlatformChannelAdapter>? channelAdapters = null)
    {
        ArgumentNullException.ThrowIfNull(editor);

        Editor = editor;
        _identityClient = identityClient;
        _youtubeAdapter = channelAdapters?.FirstOrDefault(adapter => adapter.Platform == StreamingPlatform.YouTube);
        Editor.ShowQuotaDiagnostics = false;
        Editor.ShowEnableToggle = false;
        Editor.PropertyChanged += OnEditorPropertyChanged;
        Editor.SettingChanged += OnEditorSettingChanged;
        Editor.Bot.PropertyChanged += OnEditorAccountPropertyChanged;
        Editor.Broadcaster.PropertyChanged += OnEditorAccountPropertyChanged;
    }

    public override string PageTitle => "Настройка YouTube";

    public override bool IsBusy => _isResolvingChannel;

    public YouTubeSettingsSectionViewModel Editor { get; }

    public bool HasValidationHint => ValidationHint.Length > 0;

    public override bool IsApplicable(OnboardingContext context) => context.YouTubeSettings.Enabled;

    public override void OnEnter(OnboardingContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        _context = context;
        _channelResolutionError = string.Empty;
        Editor.Load(
            context.YouTubeSettings,
            context.YouTubeClientSecret,
            context.YouTubeBotAccount,
            context.YouTubeBroadcasterAccount);
        _lastChannelInput = Editor.Channel.Trim();
        UpdateValidation();
    }

    public override async Task<bool> OnLeavingAsync(OnboardingContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        UpdateValidation();
        if (!CanAdvance)
        {
            return false;
        }

        if (Editor.Enabled && !await EnsureChannelResolvedAsync(context).ConfigureAwait(true))
        {
            return false;
        }

        Editor.SaveTo(
            context.YouTubeSettings,
            out var secret,
            context.YouTubeBotAccount,
            context.YouTubeBroadcasterAccount);
        context.YouTubeClientSecret = secret;
        return true;
    }

    public void Dispose()
    {
        Editor.PropertyChanged -= OnEditorPropertyChanged;
        Editor.SettingChanged -= OnEditorSettingChanged;
        Editor.Bot.PropertyChanged -= OnEditorAccountPropertyChanged;
        Editor.Broadcaster.PropertyChanged -= OnEditorAccountPropertyChanged;
    }

    private void OnEditorPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(YouTubeSettingsSectionViewModel.Channel))
        {
            ClearChannelResolutionStateIfInputChanged();
        }

        UpdateValidation();
    }

    private void OnEditorAccountPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        _channelResolutionError = string.Empty;
        UpdateValidation();
    }

    private void OnEditorSettingChanged(object? sender, EventArgs e)
    {
        ClearChannelResolutionStateIfInputChanged();
        UpdateValidation();
        if (_context is not null)
        {
            Editor.SaveTo(
                _context.YouTubeSettings,
                out var secret,
                _context.YouTubeBotAccount,
                _context.YouTubeBroadcasterAccount);
            _context.YouTubeClientSecret = secret;
        }
    }

    private void ClearChannelResolutionStateIfInputChanged()
    {
        var current = Editor.Channel.Trim();
        if (string.Equals(current, _lastChannelInput, StringComparison.Ordinal))
        {
            return;
        }

        _lastChannelInput = current;
        _channelResolutionError = string.Empty;
        if (_context is not null
            && _context.YouTubeResolvedChannel is { } resolved
            && !string.Equals(resolved.Input, current, StringComparison.OrdinalIgnoreCase))
        {
            _context.YouTubeResolvedChannel = null;
        }
    }

    private async Task<bool> EnsureChannelResolvedAsync(OnboardingContext context)
    {
        var rawInput = Editor.Channel.Trim();
        if (OnboardingContext.TryExtractYouTubeChannelId(rawInput, out _))
        {
            context.YouTubeResolvedChannel = null;
            _channelResolutionError = string.Empty;
            return true;
        }

        var normalizedHandle = YouTubeChannelInput.NormalizeHandle(rawInput);
        if (string.IsNullOrWhiteSpace(normalizedHandle))
        {
            SetResolutionError("Не удалось распознать ссылку на канал YouTube. Укажите @handle, прямой UC-ID или ссылку вида youtube.com/channel/UC…");
            return false;
        }

        if (context.YouTubeResolvedChannel is { } cached
            && !string.IsNullOrWhiteSpace(cached.ChannelId)
            && string.Equals(cached.Input, rawInput, StringComparison.OrdinalIgnoreCase))
        {
            _channelResolutionError = string.Empty;
            return true;
        }

        if (TryMatchExistingChannel(context, rawInput, normalizedHandle, out var existingResolved))
        {
            context.YouTubeResolvedChannel = existingResolved;
            _channelResolutionError = string.Empty;
            return true;
        }

        SetResolving(true);
        ValidationHint = "Определяем ID канала YouTube…";
        OnPropertyChanged(nameof(HasValidationHint));

        try
        {
            var resolved = await ResolveRemoteChannelAsync(context, rawInput, normalizedHandle).ConfigureAwait(true);
            if (resolved is null)
            {
                return false;
            }

            context.YouTubeResolvedChannel = resolved;
            _channelResolutionError = string.Empty;
            UpdateValidation();
            return true;
        }
        finally
        {
            SetResolving(false);
        }
    }

    private static bool TryMatchExistingChannel(
        OnboardingContext context,
        string rawInput,
        string normalizedHandle,
        out OnboardingContext.ResolvedYouTubeChannelInfo? resolved)
    {
        if (context.YouTubeChannel is { } existing && !string.IsNullOrWhiteSpace(existing.ChannelId))
        {
            var existingNormalized = YouTubeChannelInput.NormalizeHandle(existing.Handle);
            if (string.Equals(rawInput, existing.ChannelId, StringComparison.Ordinal)
                || string.Equals(rawInput, existing.Handle, StringComparison.OrdinalIgnoreCase)
                || (existingNormalized is not null && string.Equals(normalizedHandle, existingNormalized, StringComparison.OrdinalIgnoreCase)))
            {
                resolved = new(
                    rawInput,
                    existing.ChannelId,
                    string.IsNullOrWhiteSpace(existing.DisplayName) ? normalizedHandle : existing.DisplayName,
                    string.IsNullOrWhiteSpace(existing.ChannelUrl) ? $"https://youtube.com/{normalizedHandle}" : existing.ChannelUrl);
                return true;
            }
        }

        resolved = null;
        return false;
    }

    private async Task<OnboardingContext.ResolvedYouTubeChannelInfo?> ResolveRemoteChannelAsync(
        OnboardingContext context,
        string rawInput,
        string normalizedHandle)
    {
        var botDraft = new YouTubeAccountSettings();
        var broadcasterDraft = new YouTubeAccountSettings();
        Editor.Bot.SaveTo(botDraft);
        Editor.Broadcaster.SaveTo(broadcasterDraft);

        var hasUsableTokenCandidate = false;
        var unauthorizedEncountered = false;

        if (_identityClient is not null)
        {
            foreach (var candidate in new[] { botDraft, broadcasterDraft })
            {
                if (!HasLiveAccessToken(candidate))
                {
                    continue;
                }

                hasUsableTokenCandidate = true;
                try
                {
                    var identity = await _identityClient
                        .ResolveChannelByHandleAsync(normalizedHandle, candidate.AccessToken.Trim(), CancellationToken.None)
                        .ConfigureAwait(true);
                    if (identity is not null && !string.IsNullOrWhiteSpace(identity.ChannelId))
                    {
                        var displayName = string.IsNullOrWhiteSpace(identity.ChannelTitle) ? normalizedHandle : identity.ChannelTitle;
                        return new(rawInput, identity.ChannelId, displayName, $"https://youtube.com/{normalizedHandle}");
                    }

                    SetResolutionError($"Канал «{rawInput}» не найден на YouTube. Проверьте @handle или укажите прямой UC-ID / ссылку вида youtube.com/channel/UC…");
                    return null;
                }
                catch (YouTubeOAuthException exception) when (exception.StatusCode == HttpStatusCode.Unauthorized)
                {
                    unauthorizedEncountered = true;
                }
                catch (Exception exception) when (exception is not OperationCanceledException)
                {
                    SetResolutionError(FormatResolutionFailure(rawInput, exception));
                    return null;
                }
            }
        }

        if (_youtubeAdapter is not null)
        {
            try
            {
                var identity = await _youtubeAdapter
                    .ResolveChannelIdentityAsync(rawInput, context.YouTubeContextId(PlatformAuthRole.Bot), CancellationToken.None)
                    .ConfigureAwait(true);
                if (identity is not null && !string.IsNullOrWhiteSpace(identity.Channel.ChannelId))
                {
                    var displayName = string.IsNullOrWhiteSpace(identity.DisplayName) ? normalizedHandle : identity.DisplayName;
                    var channelUrl = string.IsNullOrWhiteSpace(identity.ChannelUrl) ? $"https://youtube.com/{normalizedHandle}" : identity.ChannelUrl;
                    return new(rawInput, identity.Channel.ChannelId, displayName, channelUrl);
                }

                SetResolutionError(hasUsableTokenCandidate || unauthorizedEncountered
                    ? $"Не удалось подтвердить канал «{rawInput}» на YouTube. Проверьте имя или укажите прямой UC-ID / ссылку вида youtube.com/channel/UC…"
                    : $"Не удалось определить ID канала «{rawInput}»: нет действующего токена для поиска. Переавторизуйтесь или укажите прямой UC-ID / ссылку вида youtube.com/channel/UC…");
                return null;
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                SetResolutionError(FormatResolutionFailure(rawInput, exception));
                return null;
            }
        }

        SetResolutionError(unauthorizedEncountered
            ? $"Токен доступа отклонён YouTube при поиске «{rawInput}». Переавторизуйтесь или укажите прямой UC-ID / ссылку вида youtube.com/channel/UC…"
            : $"Не удалось определить ID канала «{rawInput}»: нет действующего access-токена для поиска. Переавторизуйтесь или укажите прямой UC-ID / ссылку вида youtube.com/channel/UC…");
        return null;
    }

    private static bool HasLiveAccessToken(YouTubeAccountSettings account)
    {
        return !string.IsNullOrWhiteSpace(account.AccessToken)
            && (account.AccessTokenExpiresAt is not { } expiresAt || expiresAt > DateTimeOffset.UtcNow.AddMinutes(1));
    }

    private static string FormatResolutionFailure(string rawInput, Exception exception)
    {
        if (exception is HttpRequestException or TaskCanceledException)
        {
            return $"Не удалось проверить канал «{rawInput}» (нет связи с YouTube). Повторите попытку или укажите прямой UC-ID / ссылку вида youtube.com/channel/UC…";
        }

        var safe = StreamingErrorMessages.SafeMessage(exception);
        return $"Не удалось определить ID канала «{rawInput}»: {safe} Укажите прямой UC-ID или ссылку вида youtube.com/channel/UC…";
    }

    private void SetResolving(bool value)
    {
        if (_isResolvingChannel == value)
        {
            return;
        }

        _isResolvingChannel = value;
        OnIsBusyChanged();
    }

    private void SetResolutionError(string message)
    {
        _channelResolutionError = message;
        ValidationHint = message;
        OnPropertyChanged(nameof(HasValidationHint));
    }

    private void UpdateValidation()
    {
        if (!Editor.Enabled)
        {
            ValidationHint = string.Empty;
            CanAdvance = true;
            OnPropertyChanged(nameof(HasValidationHint));
            return;
        }

        var botDraft = new YouTubeAccountSettings();
        Editor.Bot.SaveTo(botDraft);

        if (string.IsNullOrWhiteSpace(Editor.ClientId))
        {
            ValidationHint = "Укажите Client ID проекта Google Cloud для YouTube.";
            CanAdvance = false;
        }
        else if (string.IsNullOrWhiteSpace(Editor.Channel))
        {
            ValidationHint = "Укажите @handle, ID (UC…) или ссылку на канал YouTube.";
            CanAdvance = false;
        }
        else if (Editor.HasQuotaBudgetError)
        {
            ValidationHint = Editor.QuotaBudgetError;
            CanAdvance = false;
        }
        else if (!PlatformAuthRoles.HasCredentials(botDraft.AccessToken, botDraft.RefreshToken))
        {
            ValidationHint = "Авторизуйте аккаунт бота YouTube для работы в чате.";
            CanAdvance = false;
        }
        else if (!YouTubeScopes.HasUsableToken(botDraft))
        {
            ValidationHint = "Срок действия токена бота YouTube истёк. Авторизуйтесь заново.";
            CanAdvance = false;
        }
        else if (!YouTubeScopes.HasUsableAuthorization(PlatformAuthRole.Bot, botDraft))
        {
            ValidationHint = "Аккаунту бота YouTube нужно право youtube.force-ssl для работы в чате. Переавторизуйтесь.";
            CanAdvance = false;
        }
        else if (!string.IsNullOrEmpty(_channelResolutionError))
        {
            ValidationHint = _channelResolutionError;
            CanAdvance = true;
        }
        else
        {
            ValidationHint = string.Empty;
            CanAdvance = true;
        }

        OnPropertyChanged(nameof(HasValidationHint));
    }
}
