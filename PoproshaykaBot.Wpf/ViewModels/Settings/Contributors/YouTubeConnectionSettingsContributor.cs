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
        _youTube.ShowLegacyChannelField = false;
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
                _youTube) { Platform = PoproshaykaBot.Core.Platforms.StreamingPlatform.YouTube },
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
        _youTube.Load(_settingsDraft, _clientSecretDraft);
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
        var currentSettings = _settingsStore.Load();
        _previousCredentials = new(currentSettings.ClientId, _accountsStore.LoadClientSecret());

        _youTube.SaveApplication(_settingsDraft, out _clientSecretDraft);
        _settingsDraft.Channel = currentSettings.Channel;
    }

    public Task<SettingsSectionSaveOutcome> SaveAsync(AppSettings targetSettings, bool appSettingsWritten)
    {
        var written = _settingsStore.Save(_settingsDraft);
        if (!string.Equals(_previousCredentials.ClientSecret, _clientSecretDraft, StringComparison.Ordinal))
        {
            written &= _accountsStore.SaveClientSecret(_clientSecretDraft);
        }

        var events = _previousCredentials.CollectAuthorizationRefreshes(
            StreamingPlatform.YouTube,
            new(_settingsDraft.ClientId, _clientSecretDraft),
            _accountsStore.ListContexts);

        return Task.FromResult(new SettingsSectionSaveOutcome(written, events));
    }

    internal (bool Ready, string? Summary, string? MissingReason) EvaluateStep1Readiness()
    {
        List<string> missing = [];
        if (!_youTube.Enabled)
        {
            missing.Add("включить YouTube");
        }

        if (string.IsNullOrWhiteSpace(_youTube.ClientId))
        {
            missing.Add("указать Client ID");
        }

        if (string.IsNullOrWhiteSpace(_youTube.ClientSecret))
        {
            missing.Add("указать Client Secret");
        }

        if (_youTube.HasQuotaBudgetError)
        {
            missing.Add("исправить бюджет квоты");
        }

        if (missing.Count > 0)
        {
            return (false, null, $"Заполните и сохраните Шаг 1 («Доступ площадки») выше: нужно {string.Join(", ", missing)}.");
        }

        var saved = _settingsStore.Load();
        var savedSecret = _accountsStore.LoadClientSecret();
        if (!saved.Enabled
            || saved.Enabled != _youTube.Enabled
            || _youTube.ClientId.Trim() != saved.ClientId
            || _youTube.ClientSecret.Trim() != savedSecret)
        {
            return (false, null, "Сохраните изменения в Шаге 1 («Доступ площадки») выше.");
        }

        return (true, "YouTube включён, ключи Google-проекта сохранены", null);
    }

    public void Dispose()
    {
        _youTube.Dispose();
    }
}
