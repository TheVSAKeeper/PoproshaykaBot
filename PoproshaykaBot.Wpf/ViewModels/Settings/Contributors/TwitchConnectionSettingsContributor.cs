using MahApps.Metro.IconPacks;
using Microsoft.Extensions.Logging;
using PoproshaykaBot.Core.Platforms;
using PoproshaykaBot.Core.Server;
using PoproshaykaBot.Core.Settings;
using PoproshaykaBot.Core.Settings.Stores;
using PoproshaykaBot.Core.Twitch.Auth;
using PoproshaykaBot.Wpf.Bootstrap;

namespace PoproshaykaBot.Wpf.ViewModels.Settings.Contributors;

public sealed class TwitchConnectionSettingsContributor : ISettingsSectionContributor, IDisposable
{
    private readonly OAuthSettingsViewModel _oauth;
    private readonly AccountsStore _accountsStore;
    private readonly SettingsManager _settingsManager;
    private readonly ILogger<TwitchConnectionSettingsContributor> _logger;
    private readonly ChannelCatalogStore? _catalog;

    private PlatformCredentialSnapshot _previousCredentials;
    private int _prevPort;

    public TwitchConnectionSettingsContributor(
        OAuthSettingsViewModel oauth,
        AccountsStore accountsStore,
        SettingsManager settingsManager,
        ILogger<TwitchConnectionSettingsContributor> logger,
        ChannelCatalogStore? catalog = null)
    {
        ArgumentNullException.ThrowIfNull(oauth);
        ArgumentNullException.ThrowIfNull(accountsStore);
        ArgumentNullException.ThrowIfNull(settingsManager);
        ArgumentNullException.ThrowIfNull(logger);

        _oauth = oauth;
        _accountsStore = accountsStore;
        _settingsManager = settingsManager;
        _logger = logger;
        _catalog = catalog;
    }

    public int Order => 1;

    public string SectionKey => "oauth";

    public SettingsSection CreateSection()
    {
        return new(
            SectionKey,
            "Авторизация",
            PackIconLucideKind.KeyRound,
            "oauth токен client id secret redirect uri scopes бот стример вещатель права доступа вход приложение аккаунт",
            [
                new SettingsSubsection("app", "Приложение Twitch"),
                new SettingsSubsection("bot", "Аккаунт бота"),
                new SettingsSubsection("broadcaster", "Аккаунт стримера"),
            ]);
    }

    public IReadOnlyList<SettingsSectionBlockDescriptor> GetBlocks()
    {
        return
        [
            new(
                "oauth/app",
                "Приложение Twitch",
                "Ключи приложения из консоли разработчика Twitch – через них входят и бот, и стример.",
                _oauth),
            new(
                "oauth/bot",
                "Аккаунт бота",
                "Аккаунт, от имени которого бот пишет в чат и читает его.",
                _oauth.Bot),
            new(
                "oauth/broadcaster",
                "Аккаунт стримера",
                "Аккаунт канала: через него приложение меняет название и категорию стрима и запускает опросы.",
                _oauth.Broadcaster),
        ];
    }

    public void Load(SettingsSessionState session)
    {
        ArgumentNullException.ThrowIfNull(session);

        _oauth.LoadApplication(session.Settings);
        _oauth.RefreshChannel(_catalog?.GetActiveChannel(StreamingPlatform.Twitch)?.DisplayName ?? session.TwitchChannel);
    }

    public IDisposable SubscribeDirty(SettingsSessionState session, Action markDirty, Action notifyChangesState)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(markDirty);

        void OnSettingChanged(object? sender, EventArgs e) => markDirty();
        void OnChannelChanged(string channel) => _oauth.RefreshChannel(channel);

        _oauth.SettingChanged += OnSettingChanged;
        session.TwitchChannelChanged += OnChannelChanged;

        var subscription = new CompositeSettingsSubscription();
        subscription.Add(() => _oauth.SettingChanged -= OnSettingChanged);
        subscription.Add(() => session.TwitchChannelChanged -= OnChannelChanged);
        return subscription;
    }

    public void BeginSave(AppSettings targetSettings)
    {
        ArgumentNullException.ThrowIfNull(targetSettings);

        var currentSettings = _settingsManager.Current.Twitch;
        _previousCredentials = new(currentSettings.ClientId, currentSettings.ClientSecret);
        _prevPort = currentSettings.HttpServerPort;

        _oauth.SaveApplication(targetSettings);
        ReconcileHttpServerPort(targetSettings);
    }

    public Task<SettingsSectionSaveOutcome> SaveAsync(AppSettings targetSettings, bool appSettingsWritten)
    {
        ArgumentNullException.ThrowIfNull(targetSettings);

        var events = _previousCredentials.CollectAuthorizationRefreshes(
            StreamingPlatform.Twitch,
            new(targetSettings.Twitch.ClientId, targetSettings.Twitch.ClientSecret),
            _accountsStore.ListContexts);

        var portChanged = targetSettings.Twitch.HttpServerPort != _prevPort;
        return Task.FromResult(new SettingsSectionSaveOutcome(true, events, portChanged));
    }

    public void Dispose()
    {
        _oauth.Dispose();
    }

    private void ReconcileHttpServerPort(AppSettings settings)
    {
        if (!RedirectUriPortResolver.TryResolve(settings.Twitch.RedirectUri, out var derivedPort))
        {
            _logger.SettingsRedirectUriInvalid(settings.Twitch.RedirectUri);
            return;
        }

        if (settings.Twitch.HttpServerPort == derivedPort)
        {
            return;
        }

        _logger.SettingsHttpPortUpdated(settings.Twitch.HttpServerPort, derivedPort);
        settings.Twitch.HttpServerPort = derivedPort;
    }
}
