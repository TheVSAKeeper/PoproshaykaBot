using CommunityToolkit.Mvvm.ComponentModel;
using PoproshaykaBot.Core.Platforms.Auth;
using PoproshaykaBot.Core.Twitch.Chat;
using PoproshaykaBot.Core.YouTube.Auth;
using PoproshaykaBot.Wpf.Bootstrap;

namespace PoproshaykaBot.Wpf.ViewModels.Onboarding;

public sealed partial class PlatformsPageViewModel : OnboardingPageViewModelBase
{
    private OnboardingContext? _context;
    private bool _suppressUpdates;

    [ObservableProperty]
    private bool _twitchEnabled = true;

    [ObservableProperty]
    private bool _youTubeEnabled;

    [ObservableProperty]
    private string _twitchStatusText = "Не настроен";

    [ObservableProperty]
    private StatusSeverity _twitchStatusSeverity;

    [ObservableProperty]
    private string _youTubeStatusText = "Не настроен";

    [ObservableProperty]
    private StatusSeverity _youTubeStatusSeverity;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasValidationError))]
    private string _validationErrorText = string.Empty;

    public override string PageTitle => "Площадки трансляции";

    public bool HasValidationError => !string.IsNullOrEmpty(ValidationErrorText);

    public override void OnEnter(OnboardingContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        _context = context;
        _suppressUpdates = true;
        try
        {
            TwitchEnabled = context.Settings.Twitch.Enabled;
            YouTubeEnabled = context.YouTubeSettings.Enabled;
        }
        finally
        {
            _suppressUpdates = false;
        }

        RefreshStatuses(context);
        ValidateSelection();
    }

    public override Task<bool> OnLeavingAsync(OnboardingContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        context.Settings.Twitch.Enabled = TwitchEnabled;
        context.YouTubeSettings.Enabled = YouTubeEnabled;
        ValidateSelection();
        return Task.FromResult(CanAdvance);
    }

    partial void OnTwitchEnabledChanged(bool value)
    {
        if (_suppressUpdates)
        {
            return;
        }

        if (_context is not null)
        {
            _context.Settings.Twitch.Enabled = value;
            _context.RaisePlatformsChanged();
        }

        ValidateSelection();
    }

    partial void OnYouTubeEnabledChanged(bool value)
    {
        if (_suppressUpdates)
        {
            return;
        }

        if (_context is not null)
        {
            _context.YouTubeSettings.Enabled = value;
            _context.RaisePlatformsChanged();
        }

        ValidateSelection();
    }

    private void ValidateSelection()
    {
        var valid = TwitchEnabled || YouTubeEnabled;
        ValidationErrorText = valid
            ? string.Empty
            : "Выберите хотя бы одну площадку для работы бота.";
        CanAdvance = valid;
    }

    private void RefreshStatuses(OnboardingContext context)
    {
        var hasTwitchCredentials = !string.IsNullOrWhiteSpace(context.Settings.Twitch.ClientId)
            && !string.IsNullOrWhiteSpace(context.Settings.Twitch.ClientSecret);
        var hasTwitchChannel = context.TwitchChannel is not null || !string.IsNullOrWhiteSpace(context.Settings.Twitch.Channel);
        var hasTwitchBot = TwitchScopes.HasUsableAuthorization(PlatformAuthRole.Bot, context.BotAccount);

        (TwitchStatusText, TwitchStatusSeverity) = (hasTwitchCredentials, hasTwitchChannel, hasTwitchBot) switch
        {
            (true, true, true) => ("Настроен", StatusSeverity.Success),
            (false, false, false) when string.IsNullOrWhiteSpace(context.BotAccount.AccessToken) => ("Не настроен", StatusSeverity.None),
            _ => ("Требует донастройки", StatusSeverity.Warning),
        };

        var hasYouTubeCredentials = !string.IsNullOrWhiteSpace(context.YouTubeSettings.ClientId);
        var hasYouTubeChannel = context.YouTubeChannel is not null || !string.IsNullOrWhiteSpace(context.YouTubeSettings.Channel);
        var hasYouTubeBot = YouTubeScopes.HasUsableAuthorization(PlatformAuthRole.Bot, context.YouTubeBotAccount);

        (YouTubeStatusText, YouTubeStatusSeverity) = (hasYouTubeCredentials, hasYouTubeChannel, hasYouTubeBot) switch
        {
            (true, true, true) => ("Настроен", StatusSeverity.Success),
            (false, false, false) when string.IsNullOrWhiteSpace(context.YouTubeBroadcasterAccount.AccessToken) => ("Не настроен", StatusSeverity.None),
            _ => ("Требует донастройки", StatusSeverity.Warning),
        };
    }
}
