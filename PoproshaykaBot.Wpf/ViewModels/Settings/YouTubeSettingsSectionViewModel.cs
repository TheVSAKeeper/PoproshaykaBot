using CommunityToolkit.Mvvm.ComponentModel;
using KeepShell.Services;
using KeepShell.Services.Modal;
using Microsoft.Extensions.Logging;
using PoproshaykaBot.Core.Diagnostics;
using PoproshaykaBot.Core.Platforms.Auth;
using PoproshaykaBot.Core.YouTube;
using PoproshaykaBot.Core.YouTube.Auth;
using PoproshaykaBot.Core.YouTube.Quota;
using PoproshaykaBot.Wpf.Infrastructure;
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

    [ObservableProperty]
    private bool _showQuotaDiagnostics = true;

    [ObservableProperty]
    private bool _showEnableToggle = true;

    [ObservableProperty]
    private bool _isAdvancedExpanded = true;

    [ObservableProperty]
    private bool _isGuideExpanded;

    public YouTubeSettingsSectionViewModel(
        IYouTubeOAuthService oauthService,
        IShellLauncher shellLauncher,
        IDialogService dialogService,
        IUiDispatcher uiDispatcher,
        TimeProvider timeProvider,
        ILoggerFactory loggerFactory,
        IYouTubeQuotaGovernor? quotaGovernor = null,
        IYouTubeChatDiagnostics? chatDiagnostics = null,
        IYouTubeMonitorDiagnostics? monitorDiagnostics = null,
        IEmbeddedYouTubeAuthDialog? embeddedYouTubeAuth = null)
    {
        var accountLogger = loggerFactory.CreateLogger<PlatformOAuthAccountViewModel>();

        Bot = new(
            new YouTubeAccountAuthAdapter(PlatformAuthRole.Bot, oauthService, () => ClientId, () => ClientSecret, embeddedYouTubeAuth),
            dialogService,
            shellLauncher,
            uiDispatcher,
            timeProvider,
            accountLogger);

        Broadcaster = new(
            new YouTubeAccountAuthAdapter(PlatformAuthRole.Broadcaster, oauthService, () => ClientId, () => ClientSecret, embeddedYouTubeAuth),
            dialogService,
            shellLauncher,
            uiDispatcher,
            timeProvider,
            accountLogger);

        QuotaStatus = new(quotaGovernor, uiDispatcher, timeProvider, chatDiagnostics, monitorDiagnostics);

        Bot.SettingChanged += OnAccountSettingChanged;
        Broadcaster.SettingChanged += OnAccountSettingChanged;
    }

    public event EventHandler? SettingChanged;

    public PlatformOAuthAccountViewModel Bot { get; }

    public PlatformOAuthAccountViewModel Broadcaster { get; }

    public bool ShowLegacyChannelField { get; set; } = true;

    public YouTubeQuotaStatusViewModel QuotaStatus { get; }

    public bool HasQuotaBudgetError => !TryParsePositiveQuotaBudget(DailyQuotaBudgetText, out _);

    public string QuotaBudgetError => HasQuotaBudgetError
        ? "Укажите целое положительное число."
        : string.Empty;

    public void Load(
        YouTubeSettings settings,
        string clientSecret,
        YouTubeAccountSettings? bot = null,
        YouTubeAccountSettings? broadcaster = null)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(clientSecret);

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

            if (bot is not null)
            {
                Bot.Load(bot);
            }

            if (broadcaster is not null)
            {
                Broadcaster.Load(broadcaster);
            }
            QuotaStatus.Refresh();
        }
        finally
        {
            _suppressUpdates = false;
        }
    }

    public void SetQuotaAutoRefresh(bool active)
    {
        QuotaStatus.SetAutoRefresh(active && ShowQuotaDiagnostics);
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

        SaveApplication(settings, out clientSecret);
        Bot.SaveTo(botTarget);
        Broadcaster.SaveTo(broadcasterTarget);
    }

    public void SaveApplication(YouTubeSettings settings, out string clientSecret)
    {
        settings.Enabled = Enabled;
        settings.ProjectId = ProjectId.Trim();
        settings.ClientId = ClientId.Trim();
        settings.Channel = Channel.Trim();
        settings.DailyQuotaBudget = TryParsePositiveQuotaBudget(DailyQuotaBudgetText, out var budget)
            ? budget
            : YouTubeSettings.DefaultDailyQuotaBudget;

        clientSecret = ClientSecret.Trim();
    }

    public void Dispose()
    {
        Bot.SettingChanged -= OnAccountSettingChanged;
        Broadcaster.SettingChanged -= OnAccountSettingChanged;
        Bot.Dispose();
        Broadcaster.Dispose();
        QuotaStatus.Dispose();
    }

    partial void OnEnabledChanged(bool value) => RaiseIfNotSuppressed();

    partial void OnProjectIdChanged(string value) => RaiseIfNotSuppressed();

    partial void OnClientIdChanged(string value) => RaiseIfNotSuppressed();

    partial void OnClientSecretChanged(string value) => RaiseIfNotSuppressed();

    partial void OnChannelChanged(string value) => RaiseIfNotSuppressed();

    partial void OnDailyQuotaBudgetTextChanged(string value) => RaiseIfNotSuppressed();

    partial void OnShowQuotaDiagnosticsChanged(bool value) => UpdateDefaultAdvancedExpanded();

    partial void OnShowEnableToggleChanged(bool value) => UpdateDefaultAdvancedExpanded();

    private void UpdateDefaultAdvancedExpanded()
    {
        IsAdvancedExpanded = ShowQuotaDiagnostics || ShowEnableToggle;
    }

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
