using MahApps.Metro.IconPacks;
using PoproshaykaBot.Core.Settings;
using PoproshaykaBot.Core.Settings.Chat;
using PoproshaykaBot.Core.Settings.Stores;

namespace PoproshaykaBot.Wpf.ViewModels.Settings.Contributors;

public sealed class ChatSettingsSectionContributor : ISettingsSectionContributor, IDisposable
{
    private readonly ChatSettingsSectionViewModel _chat;
    private readonly ChatSettingsStore _chatSettingsStore;

    private ChatSettings _draft = new();

    public ChatSettingsSectionContributor(
        ChatSettingsSectionViewModel chat,
        ChatSettingsStore chatSettingsStore)
    {
        ArgumentNullException.ThrowIfNull(chat);
        ArgumentNullException.ThrowIfNull(chatSettingsStore);

        _chat = chat;
        _chatSettingsStore = chatSettingsStore;
    }

    public int Order => 3;

    public string SectionKey => "chat";

    public SettingsSection CreateSection()
    {
        return new(
            SectionKey,
            "Чат",
            PackIconLucideKind.MessageSquare,
            "чат история кэш сообщения окно бота obs прошлые лимит");
    }

    public IReadOnlyList<SettingsSectionBlockDescriptor> GetBlocks()
    {
        return
        [
            new(
                string.Empty,
                "Чат",
                "Кэш сообщений между запусками и показ прошлых сообщений для всех площадок.",
                _chat),
        ];
    }

    public void Load(SettingsSessionState session)
    {
        ArgumentNullException.ThrowIfNull(session);

        _draft = JsonStoreClone.DeepClone(_chatSettingsStore.Load());
        _chat.LoadSettings(_draft);
    }

    public IDisposable SubscribeDirty(SettingsSessionState session, Action markDirty, Action notifyChangesState)
    {
        ArgumentNullException.ThrowIfNull(markDirty);

        var subscription = new CompositeSettingsSubscription();
        subscription.TrackPropertyChanged(_chat, (_, e) =>
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
        ArgumentNullException.ThrowIfNull(targetSettings);

        _chat.SaveSettings(_draft);
    }

    public Task<SettingsSectionSaveOutcome> SaveAsync(AppSettings targetSettings, bool appSettingsWritten)
    {
        var written = _chatSettingsStore.Save(_draft);
        return Task.FromResult(SettingsSectionSaveOutcome.FromWrite(written));
    }

    public void Dispose()
    {
        _chat.Dispose();
    }
}
