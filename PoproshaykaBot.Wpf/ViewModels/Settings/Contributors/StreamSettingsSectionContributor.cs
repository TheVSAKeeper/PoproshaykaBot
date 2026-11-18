using MahApps.Metro.IconPacks;
using PoproshaykaBot.Core.Settings;
using System.ComponentModel;

namespace PoproshaykaBot.Wpf.ViewModels.Settings.Contributors;

public sealed class StreamSettingsSectionContributor : ISettingsSectionContributor
{
    private readonly AutoBroadcastSettingsViewModel _autoBroadcast;
    private readonly BotLifecycleAutomationSectionViewModel _botLifecycle;
    private readonly PollsSettingsSectionViewModel _polls;

    public StreamSettingsSectionContributor(
        AutoBroadcastSettingsViewModel autoBroadcast,
        BotLifecycleAutomationSectionViewModel botLifecycle,
        PollsSettingsSectionViewModel polls)
    {
        ArgumentNullException.ThrowIfNull(autoBroadcast);
        ArgumentNullException.ThrowIfNull(botLifecycle);
        ArgumentNullException.ThrowIfNull(polls);

        _autoBroadcast = autoBroadcast;
        _botLifecycle = botLifecycle;
        _polls = polls;
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
        return
        [
            new(
                "stream/auto",
                "Автоматический режим",
                "Что бот делает сам, когда стрим начинается и заканчивается.",
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
    }

    public void Load(SettingsSessionState session)
    {
        ArgumentNullException.ThrowIfNull(session);

        _autoBroadcast.LoadSettings(session.Settings.Twitch.AutoBroadcast);
        _botLifecycle.LoadSettings(session.Settings.Twitch.BotLifecycleAutomation);
        _polls.RevertCommand.Execute(null);
    }

    public IDisposable SubscribeDirty(SettingsSessionState session, Action markDirty, Action notifyChangesState)
    {
        ArgumentNullException.ThrowIfNull(markDirty);
        ArgumentNullException.ThrowIfNull(notifyChangesState);

        var subscription = new CompositeSettingsSubscription();

        void OnPropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (!string.Equals(e.PropertyName, "HasErrors", StringComparison.Ordinal))
            {
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

        subscription.TrackPropertyChanged(_autoBroadcast, OnPropertyChanged);
        subscription.TrackPropertyChanged(_botLifecycle, OnPropertyChanged);
        subscription.TrackPropertyChanged(_polls, OnPollsChanged);

        return subscription;
    }

    public void BeginSave(AppSettings targetSettings)
    {
        ArgumentNullException.ThrowIfNull(targetSettings);

        _autoBroadcast.SaveSettings(targetSettings.Twitch.AutoBroadcast);
        _botLifecycle.SaveSettings(targetSettings.Twitch.BotLifecycleAutomation);
    }

    public Task<SettingsSectionSaveOutcome> SaveAsync(AppSettings targetSettings, bool appSettingsWritten)
    {
        var written = !_polls.HasChanges || _polls.SaveChanges();
        return Task.FromResult(SettingsSectionSaveOutcome.FromWrite(written));
    }
}
