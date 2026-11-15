using MahApps.Metro.IconPacks;
using PoproshaykaBot.Core.Platforms;
using PoproshaykaBot.Core.Settings;
using PoproshaykaBot.Core.Settings.Stores;
using PoproshaykaBot.Core.YouTube;
using PoproshaykaBot.Core.YouTube.Auth;

namespace PoproshaykaBot.Wpf.ViewModels.Settings.Contributors;

public sealed class YouTubeConnectionSettingsContributor : ISettingsSectionContributor, IDisposable
{
    private readonly YouTubeSettingsSectionViewModel _youTube;
    private readonly YouTubeSettingsStore _settingsStore;
    private readonly YouTubeAccountsStore _accountsStore;

    private YouTubeSettings _settingsDraft = new();
    private string _clientSecretDraft = string.Empty;
    private YouTubeAccountSettings _botDraft = new();
    private YouTubeAccountSettings _broadcasterDraft = new();
    private PlatformCredentialSnapshot _previousCredentials;

    public YouTubeConnectionSettingsContributor(
        YouTubeSettingsSectionViewModel youTube,
        YouTubeSettingsStore settingsStore,
        YouTubeAccountsStore accountsStore)
    {
        ArgumentNullException.ThrowIfNull(youTube);
        ArgumentNullException.ThrowIfNull(settingsStore);
        ArgumentNullException.ThrowIfNull(accountsStore);

        _youTube = youTube;
        _settingsStore = settingsStore;
        _accountsStore = accountsStore;
    }

    public int Order => 2;

    public string SectionKey => "youtube";

    public SettingsSection CreateSection()
    {
        return new(
            SectionKey,
            "YouTube",
            PackIconLucideKind.Tv,
            "youtube ютуб google проект project id client secret квота канал авторизация бот стример testing production",
            [
                new SettingsSubsection("project", "Google-проект"),
                new SettingsSubsection("bot", "Аккаунт бота"),
                new SettingsSubsection("broadcaster", "Аккаунт стримера"),
            ]);
    }

    public IReadOnlyList<SettingsSectionBlockDescriptor> GetBlocks()
    {
        return
        [
            new(
                "youtube/project",
                "Google-проект",
                "Ваш собственный проект Google Cloud и реквизиты Desktop-клиента.",
                _youTube),
            new(
                "youtube/bot",
                "Аккаунт бота",
                "Google-аккаунт с каналом YouTube, от имени которого бот читает и пишет в чат трансляции.",
                _youTube.Bot),
            new(
                "youtube/broadcaster",
                "Аккаунт стримера",
                "Google-аккаунт владельца канала: используется для чтения информации об активной трансляции.",
                _youTube.Broadcaster),
        ];
    }

    public void Load(SettingsSessionState session)
    {
        ArgumentNullException.ThrowIfNull(session);

        _settingsDraft = JsonStoreClone.DeepClone(_settingsStore.Load());
        _clientSecretDraft = _accountsStore.LoadClientSecret();
        _botDraft = JsonStoreClone.DeepClone(_accountsStore.LoadBot());
        _broadcasterDraft = JsonStoreClone.DeepClone(_accountsStore.LoadBroadcaster());

        _youTube.Load(_settingsDraft, _clientSecretDraft, _botDraft, _broadcasterDraft);
    }

    public IDisposable SubscribeDirty(SettingsSessionState session, Action markDirty, Action notifyChangesState)
    {
        ArgumentNullException.ThrowIfNull(markDirty);

        void OnSettingChanged(object? sender, EventArgs e) => markDirty();

        _youTube.SettingChanged += OnSettingChanged;

        var subscription = new CompositeSettingsSubscription();
        subscription.Add(() => _youTube.SettingChanged -= OnSettingChanged);
        return subscription;
    }

    public string? ValidateBeforeSave()
    {
        return _youTube.HasQuotaBudgetError
            ? "В разделе «YouTube» указан некорректный бюджет квоты. Исправьте его и сохраните ещё раз."
            : null;
    }

    public void BeginSave(AppSettings targetSettings)
    {
        var currentBot = _accountsStore.LoadBot();
        var currentBroadcaster = _accountsStore.LoadBroadcaster();
        _previousCredentials = new(
            _settingsStore.Load().ClientId,
            _accountsStore.LoadClientSecret(),
            currentBot.AccessToken,
            currentBot.RefreshToken,
            currentBroadcaster.AccessToken,
            currentBroadcaster.RefreshToken);

        _youTube.SaveTo(_settingsDraft, out _clientSecretDraft, _botDraft, _broadcasterDraft);
    }

    public Task<SettingsSectionSaveOutcome> SaveAsync(AppSettings targetSettings, bool appSettingsWritten)
    {
        var written = _settingsStore.Save(_settingsDraft);
        written &= _accountsStore.SaveAll(_clientSecretDraft, _botDraft, _broadcasterDraft);

        var events = _previousCredentials.CollectAuthorizationRefreshes(
            StreamingPlatform.YouTube,
            new(
                _settingsDraft.ClientId,
                _clientSecretDraft,
                _botDraft.AccessToken,
                _botDraft.RefreshToken,
                _broadcasterDraft.AccessToken,
                _broadcasterDraft.RefreshToken));

        return Task.FromResult(new SettingsSectionSaveOutcome(written, events));
    }

    public void Dispose()
    {
        _youTube.Dispose();
    }
}
