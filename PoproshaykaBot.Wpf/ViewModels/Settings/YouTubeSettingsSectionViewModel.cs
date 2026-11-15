using CommunityToolkit.Mvvm.ComponentModel;
using KeepShell.Services;
using KeepShell.Services.Modal;
using Microsoft.Extensions.Logging;
using PoproshaykaBot.Core.Platforms.Auth;
using PoproshaykaBot.Core.YouTube;
using PoproshaykaBot.Core.YouTube.Auth;
using System.Globalization;

namespace PoproshaykaBot.Wpf.ViewModels.Settings;

public sealed partial class YouTubeSettingsSectionViewModel : ObservableObject, IDisposable
{
    private bool _suppressUpdates;

    [ObservableProperty]
    private bool _enabled;

    [ObservableProperty]
    private string _projectId = string.Empty;

    [ObservableProperty]
    private string _clientId = string.Empty;

    [ObservableProperty]
    private string _clientSecret = string.Empty;

    [ObservableProperty]
    private string _channel = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasQuotaBudgetError))]
    [NotifyPropertyChangedFor(nameof(QuotaBudgetError))]
    private string _dailyQuotaBudgetText = YouTubeSettings.DefaultDailyQuotaBudget.ToString(CultureInfo.InvariantCulture);

    public YouTubeSettingsSectionViewModel(
        IYouTubeOAuthService oauthService,
        IShellLauncher shellLauncher,
        IDialogService dialogService,
        IUiDispatcher uiDispatcher,
        TimeProvider timeProvider,
        ILoggerFactory loggerFactory)
    {
        var accountLogger = loggerFactory.CreateLogger<PlatformOAuthAccountViewModel>();

        Bot = new(
            new YouTubeAccountAuthAdapter(PlatformAuthRole.Bot, oauthService, () => ClientId, () => ClientSecret),
            dialogService,
            shellLauncher,
            uiDispatcher,
            timeProvider,
            accountLogger);

        Broadcaster = new(
            new YouTubeAccountAuthAdapter(PlatformAuthRole.Broadcaster, oauthService, () => ClientId, () => ClientSecret),
            dialogService,
            shellLauncher,
            uiDispatcher,
            timeProvider,
            accountLogger);

        Bot.SettingChanged += OnAccountSettingChanged;
        Broadcaster.SettingChanged += OnAccountSettingChanged;
    }

    public event EventHandler? SettingChanged;

    public PlatformOAuthAccountViewModel Bot { get; }

    public PlatformOAuthAccountViewModel Broadcaster { get; }

    public bool HasQuotaBudgetError => !TryParsePositiveQuotaBudget(DailyQuotaBudgetText, out _);

    public string QuotaBudgetError => HasQuotaBudgetError
        ? "Укажите целое положительное число."
        : string.Empty;

    public void Load(
        YouTubeSettings settings,
        string clientSecret,
        YouTubeAccountSettings bot,
        YouTubeAccountSettings broadcaster)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(clientSecret);
        ArgumentNullException.ThrowIfNull(bot);
        ArgumentNullException.ThrowIfNull(broadcaster);

        _suppressUpdates = true;
        try
        {
            Enabled = settings.Enabled;
            ProjectId = settings.ProjectId;
            ClientId = settings.ClientId;
            ClientSecret = clientSecret;
            Channel = settings.Channel;
            DailyQuotaBudgetText = settings.DailyQuotaBudget > 0
                ? settings.DailyQuotaBudget.ToString(CultureInfo.InvariantCulture)
                : YouTubeSettings.DefaultDailyQuotaBudget.ToString(CultureInfo.InvariantCulture);

            Bot.Load(bot);
            Broadcaster.Load(broadcaster);
        }
        finally
        {
            _suppressUpdates = false;
        }
    }

    public void SaveTo(
        YouTubeSettings settings,
        out string clientSecret,
        YouTubeAccountSettings botTarget,
        YouTubeAccountSettings broadcasterTarget)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(botTarget);
        ArgumentNullException.ThrowIfNull(broadcasterTarget);

        settings.Enabled = Enabled;
        settings.ProjectId = ProjectId.Trim();
        settings.ClientId = ClientId.Trim();
        settings.Channel = Channel.Trim();
        settings.DailyQuotaBudget = TryParsePositiveQuotaBudget(DailyQuotaBudgetText, out var budget)
            ? budget
            : YouTubeSettings.DefaultDailyQuotaBudget;

        clientSecret = ClientSecret.Trim();
        Bot.SaveTo(botTarget);
        Broadcaster.SaveTo(broadcasterTarget);
    }

    public void Dispose()
    {
        Bot.SettingChanged -= OnAccountSettingChanged;
        Broadcaster.SettingChanged -= OnAccountSettingChanged;
        Bot.Dispose();
        Broadcaster.Dispose();
    }

    partial void OnEnabledChanged(bool value) => RaiseIfNotSuppressed();

    partial void OnProjectIdChanged(string value) => RaiseIfNotSuppressed();

    partial void OnClientIdChanged(string value) => RaiseIfNotSuppressed();

    partial void OnClientSecretChanged(string value) => RaiseIfNotSuppressed();

    partial void OnChannelChanged(string value) => RaiseIfNotSuppressed();

    partial void OnDailyQuotaBudgetTextChanged(string value) => RaiseIfNotSuppressed();

    private static bool TryParsePositiveQuotaBudget(string? raw, out int budget)
    {
        return int.TryParse(raw?.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out budget) && budget > 0;
    }

    private void RaiseIfNotSuppressed()
    {
        if (!_suppressUpdates)
        {
            SettingChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    private void OnAccountSettingChanged(object? sender, EventArgs e)
    {
        RaiseIfNotSuppressed();
    }
}
