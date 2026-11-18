using MahApps.Metro.IconPacks;
using PoproshaykaBot.Core.Debugging;
using PoproshaykaBot.Core.Infrastructure.Events.Settings;
using PoproshaykaBot.Core.Platforms.Auth;
using PoproshaykaBot.Core.Settings;
using System.ComponentModel;

namespace PoproshaykaBot.Wpf.ViewModels.Settings.Contributors;

public sealed class BasicSettingsSectionContributor : ISettingsSectionContributor
{
    private readonly BasicSettingsSectionViewModel _basic;
    private readonly RateLimitingSettingsViewModel _rateLimiting;
    private readonly MessagesSettingsSectionViewModel _messages;
    private readonly SettingsManager _settingsManager;
    private readonly ITargetChannelProvider _targetChannelProvider;

    private string _prevTargetChannel = string.Empty;
    private PlatformAuthRole _prevChatDisplayAccount;

    public BasicSettingsSectionContributor(
        BasicSettingsSectionViewModel basic,
        RateLimitingSettingsViewModel rateLimiting,
        MessagesSettingsSectionViewModel messages,
        SettingsManager settingsManager,
        ITargetChannelProvider targetChannelProvider)
    {
        ArgumentNullException.ThrowIfNull(basic);
        ArgumentNullException.ThrowIfNull(rateLimiting);
        ArgumentNullException.ThrowIfNull(messages);
        ArgumentNullException.ThrowIfNull(settingsManager);
        ArgumentNullException.ThrowIfNull(targetChannelProvider);

        _basic = basic;
        _rateLimiting = rateLimiting;
        _messages = messages;
        _settingsManager = settingsManager;
        _targetChannelProvider = targetChannelProvider;
    }

    public int Order => 0;

    public string SectionKey => "basic";

    public SettingsSection CreateSection()
    {
        return new(
            SectionKey,
            "Основные",
            PackIconLucideKind.Settings2,
            "лимиты ограничения отправки задержка приветствие сообщения шаблоны текст",
            [
                new SettingsSubsection("limits", "Ограничения отправки"),
                new SettingsSubsection("messages", "Шаблоны сообщений"),
            ]);
    }

    public IReadOnlyList<SettingsSectionBlockDescriptor> GetBlocks()
    {
        return
        [
            new(
                "basic/channel",
                "Отображение чата Twitch",
                null,
                _basic),
            new(
                "basic/limits",
                "Ограничения отправки",
                "Паузы и лимиты, за которыми Twitch перестаёт считать бота спамом.",
                _rateLimiting),
            new(
                "basic/messages",
                "Шаблоны сообщений",
                "Тексты, которыми бот здоровается, прощается и отвечает.",
                _messages),
        ];
    }

    public void Load(SettingsSessionState session)
    {
        ArgumentNullException.ThrowIfNull(session);

        _basic.LoadSettings(session.Settings.Twitch);
        _rateLimiting.LoadSettings(session.Settings.Twitch);
        _messages.LoadSettings(session.Settings.Twitch.Messages);
        session.UpdateTwitchChannel(_basic.GetChannel());
    }

    public IDisposable SubscribeDirty(SettingsSessionState session, Action markDirty, Action notifyChangesState)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(markDirty);

        var subscription = new CompositeSettingsSubscription();

        void OnPropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (string.Equals(e.PropertyName, "HasErrors", StringComparison.Ordinal))
            {
                return;
            }

            if (ReferenceEquals(sender, _basic)
                && string.Equals(e.PropertyName, nameof(BasicSettingsSectionViewModel.Channel), StringComparison.Ordinal))
            {
                session.UpdateTwitchChannel(_basic.GetChannel());
            }

            markDirty();
        }

        subscription.TrackPropertyChanged(_basic, OnPropertyChanged);
        subscription.TrackPropertyChanged(_rateLimiting, OnPropertyChanged);

        foreach (var entry in _messages.Entries)
        {
            subscription.TrackPropertyChanged(entry, OnPropertyChanged);
        }

        return subscription;
    }

    public void BeginSave(AppSettings targetSettings)
    {
        ArgumentNullException.ThrowIfNull(targetSettings);

        _prevTargetChannel = _targetChannelProvider.Current.Login;
        _prevChatDisplayAccount = _settingsManager.Current.Twitch.ChatDisplayAccount;

        _basic.SaveSettings(targetSettings.Twitch);
        targetSettings.Twitch.Channel = _settingsManager.Current.Twitch.Channel;
        _rateLimiting.SaveSettings(targetSettings.Twitch);
        _messages.SaveSettings(targetSettings.Twitch.Messages);
    }

    public Task<SettingsSectionSaveOutcome> SaveAsync(AppSettings targetSettings, bool appSettingsWritten)
    {
        return Task.FromResult(SettingsSectionSaveOutcome.Clean);
    }

    public IReadOnlyList<SettingsSectionEvent> CollectPostSaveEvents(AppSettings targetSettings)
    {
        ArgumentNullException.ThrowIfNull(targetSettings);

        var newTargetChannel = _targetChannelProvider.Current.Login;
        if (!string.Equals(_prevTargetChannel, newTargetChannel, StringComparison.OrdinalIgnoreCase)
            || _prevChatDisplayAccount != targetSettings.Twitch.ChatDisplayAccount)
        {
            return [SettingsSectionEvent.Create(new ChatDisplaySettingsChanged(newTargetChannel, targetSettings.Twitch.ChatDisplayAccount))];
        }

        return [];
    }
}
