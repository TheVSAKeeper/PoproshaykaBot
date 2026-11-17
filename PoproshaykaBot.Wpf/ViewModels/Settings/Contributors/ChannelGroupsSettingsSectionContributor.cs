using MahApps.Metro.IconPacks;
using PoproshaykaBot.Core.Settings;
using PoproshaykaBot.Core.Settings.Stores;

namespace PoproshaykaBot.Wpf.ViewModels.Settings.Contributors;

public sealed class ChannelGroupsSettingsSectionContributor : ISettingsSectionContributor
{
    private readonly ChannelGroupsSettingsSectionViewModel _viewModel;
    private readonly ChannelCatalogStore _catalogStore;
    private bool _edited;

    public ChannelGroupsSettingsSectionContributor(
        ChannelGroupsSettingsSectionViewModel viewModel,
        ChannelCatalogStore catalogStore)
    {
        ArgumentNullException.ThrowIfNull(viewModel);
        ArgumentNullException.ThrowIfNull(catalogStore);

        _viewModel = viewModel;
        _catalogStore = catalogStore;
    }

    public int Order => 7;

    public string SectionKey => "channels";

    public SettingsSection CreateSection()
    {
        return new(
            SectionKey,
            "Группы каналов",
            PackIconLucideKind.Layers,
            "группы каналов объединение рейтинг топ статистика каналы");
    }

    public IReadOnlyList<SettingsSectionBlockDescriptor> GetBlocks()
    {
        return
        [
            new(
                string.Empty,
                "Группы каналов",
                "Объединения каналов для срезов рейтинга в чате (!топ, !ранг) и на странице «Пользователи».",
                _viewModel),
        ];
    }

    public void Load(SettingsSessionState session)
    {
        ArgumentNullException.ThrowIfNull(session);

        _viewModel.Load();
        _edited = false;
    }

    public IDisposable SubscribeDirty(SettingsSessionState session, Action markDirty, Action notifyChangesState)
    {
        ArgumentNullException.ThrowIfNull(markDirty);

        void OnEdited(object? sender, EventArgs e)
        {
            _edited = true;
            markDirty();
        }

        _viewModel.Edited += OnEdited;

        var subscription = new CompositeSettingsSubscription();
        subscription.Add(() => _viewModel.Edited -= OnEdited);
        return subscription;
    }

    public string? ValidateBeforeSave()
    {
        return _edited && _viewModel.HasErrors
            ? "Укажите название для каждой группы каналов перед сохранением."
            : null;
    }

    public void BeginSave(AppSettings targetSettings)
    {
    }

    public Task<SettingsSectionSaveOutcome> SaveAsync(AppSettings targetSettings, bool appSettingsWritten)
    {
        if (!_edited)
        {
            return Task.FromResult(SettingsSectionSaveOutcome.Clean);
        }

        var next = _viewModel.BuildSettings();
        var written = _catalogStore.Save(next);
        _edited = !written;
        return Task.FromResult(SettingsSectionSaveOutcome.FromWrite(written));
    }
}
