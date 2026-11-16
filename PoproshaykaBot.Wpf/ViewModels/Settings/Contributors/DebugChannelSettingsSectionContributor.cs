using MahApps.Metro.IconPacks;
using PoproshaykaBot.Core.Debugging;
using PoproshaykaBot.Core.Settings;
using PoproshaykaBot.Core.Settings.Debugging;
using PoproshaykaBot.Core.Settings.Stores;

namespace PoproshaykaBot.Wpf.ViewModels.Settings.Contributors;

public sealed class DebugChannelSettingsSectionContributor : ISettingsSectionContributor
{
    private readonly DebugChannelSectionViewModel _debugChannel;
    private readonly DebugChannelStore _debugChannelStore;
    private DebugChannelSettings _draft = new();

    public DebugChannelSettingsSectionContributor(
        DebugChannelSectionViewModel debugChannel,
        DebugChannelStore debugChannelStore)
    {
        ArgumentNullException.ThrowIfNull(debugChannel);
        ArgumentNullException.ThrowIfNull(debugChannelStore);

        _debugChannel = debugChannel;
        _debugChannelStore = debugChannelStore;
    }

    public int Order => 9;

    public string SectionKey => "debug";

    public SettingsSection CreateSection()
    {
        return new(
            SectionKey,
            "Отладка",
            PackIconLucideKind.Bug,
            "чужой канал наблюдение чтение чата тестирование только чтение отправка сообщений");
    }

    public IReadOnlyList<SettingsSectionBlockDescriptor> GetBlocks()
    {
        return
        [
            new(string.Empty, "Отладка", null, _debugChannel),
        ];
    }

    public void Load(SettingsSessionState session)
    {
        _draft = JsonStoreClone.DeepClone(_debugChannelStore.Load());
        _debugChannel.LoadSettings(_draft);
    }

    public IDisposable SubscribeDirty(SettingsSessionState session, Action markDirty, Action notifyChangesState)
    {
        ArgumentNullException.ThrowIfNull(markDirty);

        var subscription = new CompositeSettingsSubscription();
        subscription.TrackPropertyChanged(_debugChannel, (_, e) =>
        {
            if (!string.Equals(e.PropertyName, "HasErrors", StringComparison.Ordinal))
            {
                markDirty();
            }
        });

        return subscription;
    }

    public void BeginSave(AppSettings targetSettings)
    {
        _debugChannel.SaveSettings(_draft);
    }

    public Task<SettingsSectionSaveOutcome> SaveAsync(AppSettings targetSettings, bool appSettingsWritten)
    {
        _debugChannelStore.Save(_draft);
        return Task.FromResult(SettingsSectionSaveOutcome.Clean);
    }

    public void OnSectionShown()
    {
        _debugChannel.Recent.OnShown();
    }
}
