using MahApps.Metro.IconPacks;
using PoproshaykaBot.Core.Platforms;
using PoproshaykaBot.Core.Settings;
using PoproshaykaBot.Core.Settings.Stores;
using System.ComponentModel;

namespace PoproshaykaBot.Wpf.ViewModels.Settings.Contributors;

public sealed class StreamSettingsSectionContributor : ISettingsSectionContributor
{
    private readonly BotMessagingLinksViewModel _autoBroadcast;
    private readonly BotLifecycleAutomationSectionViewModel _botLifecycle;
    private readonly PollsSettingsSectionViewModel _polls;
    private readonly PlatformLifecycleSettingsStore? _platformLifecycleStore;
    private readonly SettingsManager? _settingsManager;

    private PlatformLifecycleSettingsFile _lifecycleDraft = new();
    private bool _lifecycleEdited;

    public StreamSettingsSectionContributor(
        BotMessagingNavigation messaging,
        BotLifecycleAutomationSectionViewModel botLifecycle,
        PollsSettingsSectionViewModel polls,
        PlatformLifecycleSettingsStore? platformLifecycleStore = null,
        SettingsManager? settingsManager = null)
    {
        ArgumentNullException.ThrowIfNull(messaging);
        ArgumentNullException.ThrowIfNull(botLifecycle);
        ArgumentNullException.ThrowIfNull(polls);

        _autoBroadcast = new(
            ("Периодическая рассылка", () => messaging.OpenRule("broadcast")),
            ("Начало эфира", () => messaging.OpenRule("stream-start")),
            ("Окончание эфира", () => messaging.OpenRule("stream-stop")),
            ("Итог эфира", () => messaging.OpenRule("stream-stats")));
        _botLifecycle = botLifecycle;
        _polls = polls;
        _platformLifecycleStore = platformLifecycleStore;
        _settingsManager = settingsManager;
    }

    public int Order => 5;

    public string SectionKey => "stream";

    public bool HasExternalChanges => _polls.HasChanges;

    public SettingsSection CreateSection()
    {
        return new(
            SectionKey,
            "Опросы",
            PackIconLucideKind.Radio,
            "голосование заготовки правила запуска");
    }

    public IReadOnlyList<SettingsSectionBlockDescriptor> GetBlocks()
    {
        List<SettingsSectionBlockDescriptor> blocks =
        [
            new(
                "stream/auto",
                "Сообщения об эфире и рассылка",
                "Тексты и условия публикаций доступны на странице «Бот».",
                _autoBroadcast),
            new(
                "stream/lifecycle",
                "Автозапуск бота",
                "Когда бот подключается к чату и когда отключается без вашего участия.",
                _botLifecycle),
            new(
                "stream/polls",
                "Опросы",
                "Заготовки голосований и правила их запуска в чате.",
                _polls),
        ];

        foreach (var platform in _botLifecycle.Platforms)
        {
            blocks.Add(new(
                $"stream/lifecycle/{platform.Platform.ToString().ToLowerInvariant()}",
                "Автозапуск бота",
                "Когда бот подключается к чату и когда отключается без вашего участия.",
                platform)
            {
                Platform = platform.Platform,
            });
        }

        return blocks;
    }

    public void Load(SettingsSessionState session)
    {
        ArgumentNullException.ThrowIfNull(session);

        _lifecycleDraft = _platformLifecycleStore is not null
            ? JsonStoreClone.DeepClone(_platformLifecycleStore.Load())
            : new();
        _botLifecycle.LoadSettings(_lifecycleDraft);
        _lifecycleEdited = false;
        _polls.RevertCommand.Execute(null);
    }

    public IDisposable SubscribeDirty(SettingsSessionState session, Action markDirty, Action notifyChangesState)
    {
        ArgumentNullException.ThrowIfNull(markDirty);
        ArgumentNullException.ThrowIfNull(notifyChangesState);

        var subscription = new CompositeSettingsSubscription();

        void OnLifecyclePropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (!string.Equals(e.PropertyName, "HasErrors", StringComparison.Ordinal))
            {
                _lifecycleEdited = true;
                markDirty();
            }
        }

        void OnPollsChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (string.Equals(e.PropertyName, nameof(PollsSettingsSectionViewModel.HasChanges), StringComparison.Ordinal))
            {
                notifyChangesState();
            }
        }

        subscription.TrackPropertyChanged(_botLifecycle, OnLifecyclePropertyChanged);
        subscription.TrackCollectionItems(_botLifecycle.Platforms, OnLifecyclePropertyChanged);
        subscription.TrackPropertyChanged(_polls, OnPollsChanged);

        return subscription;
    }

    public void BeginSave(AppSettings targetSettings)
    {
        ArgumentNullException.ThrowIfNull(targetSettings);

        if (_lifecycleEdited)
        {
            _botLifecycle.SaveSettings(_lifecycleDraft);
            if (_platformLifecycleStore is null
                && _lifecycleDraft.Platforms.TryGetValue(StreamingPlatform.Twitch, out var twitchLifecycle))
            {
                targetSettings.Twitch.BotLifecycleAutomation.AutoConnectOnStreamOnline = twitchLifecycle.AutoConnectOnStreamOnline;
                targetSettings.Twitch.BotLifecycleAutomation.AutoDisconnectOnStreamOffline = twitchLifecycle.AutoDisconnectOnStreamOffline;
            }
        }
    }

    public Task<SettingsSectionSaveOutcome> SaveAsync(AppSettings targetSettings, bool appSettingsWritten)
    {
        var written = !_polls.HasChanges || _polls.SaveChanges();
        if (_lifecycleEdited && _platformLifecycleStore is not null)
        {
            var lifecycleWritten = _platformLifecycleStore.Save(_lifecycleDraft);
            written &= lifecycleWritten;

            if (lifecycleWritten)
            {
                _lifecycleEdited = false;

                if (_settingsManager is not null
                    && _lifecycleDraft.Platforms.TryGetValue(StreamingPlatform.Twitch, out var twitchLifecycle))
                {
                    _settingsManager.Mutate(settings =>
                    {
                        settings.Twitch.BotLifecycleAutomation.AutoConnectOnStreamOnline = twitchLifecycle.AutoConnectOnStreamOnline;
                        settings.Twitch.BotLifecycleAutomation.AutoDisconnectOnStreamOffline = twitchLifecycle.AutoDisconnectOnStreamOffline;
                    });
                }
            }
        }

        return Task.FromResult(SettingsSectionSaveOutcome.FromWrite(written));
    }
}
