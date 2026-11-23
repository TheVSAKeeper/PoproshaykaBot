using MahApps.Metro.IconPacks;
using PoproshaykaBot.Core.Settings;
using PoproshaykaBot.Core.Settings.Stores;
using PoproshaykaBot.Core.Streaming.Switching;

namespace PoproshaykaBot.Wpf.ViewModels.Settings.Contributors;

public sealed class ChannelsSettingsSectionContributor : ISettingsSectionContributor
{
    private readonly ChannelCatalogSettingsSectionViewModel _catalogViewModel;
    private readonly ChannelGroupsSettingsSectionViewModel _groupsViewModel;
    private readonly ChannelCatalogStore _catalogStore;
    private readonly IChannelSwitchCoordinator? _switchCoordinator;
    private bool _catalogEdited;
    private bool _groupsEdited;

    public ChannelsSettingsSectionContributor(
        ChannelCatalogSettingsSectionViewModel catalogViewModel,
        ChannelGroupsSettingsSectionViewModel groupsViewModel,
        ChannelCatalogStore catalogStore,
        IChannelSwitchCoordinator? switchCoordinator = null)
    {
        ArgumentNullException.ThrowIfNull(catalogViewModel);
        ArgumentNullException.ThrowIfNull(groupsViewModel);
        ArgumentNullException.ThrowIfNull(catalogStore);

        _catalogViewModel = catalogViewModel;
        _groupsViewModel = groupsViewModel;
        _catalogStore = catalogStore;
        _switchCoordinator = switchCoordinator;
    }

    public int Order => 7;

    public string SectionKey => "channels";

    public SettingsSection CreateSection()
    {
        return new(
            SectionKey,
            "Каналы и группы",
            PackIconLucideKind.Layers,
            "каналы группы каталог переключение мониторинг авторизация рейтинг топ статистика",
            [
                new SettingsSubsection("catalog", "Каталог каналов и переключение"),
                new SettingsSubsection("groups", "Группы каналов"),
            ]);
    }

    public IReadOnlyList<SettingsSectionBlockDescriptor> GetBlocks()
    {
        return
        [
            new(
                "channels/catalog",
                "Каналы",
                null,
                _catalogViewModel),
            new(
                "channels/groups",
                "Группы каналов",
                "Объединения каналов для срезов рейтинга в чате (!топ, !ранг) и на странице «Пользователи».",
                _groupsViewModel),
        ];
    }

    public void Load(SettingsSessionState session)
    {
        ArgumentNullException.ThrowIfNull(session);

        _catalogViewModel.Load();
        _groupsViewModel.Load();
        _catalogEdited = false;
        _groupsEdited = false;
    }

    public IDisposable SubscribeDirty(SettingsSessionState session, Action markDirty, Action notifyChangesState)
    {
        ArgumentNullException.ThrowIfNull(markDirty);

        void OnCatalogEdited(object? sender, EventArgs e)
        {
            _catalogEdited = true;
            markDirty();
        }

        void OnGroupsEdited(object? sender, EventArgs e)
        {
            _groupsEdited = true;
            markDirty();
        }

        _catalogViewModel.Edited += OnCatalogEdited;
        _groupsViewModel.Edited += OnGroupsEdited;

        var subscription = new CompositeSettingsSubscription();
        subscription.Add(() => _catalogViewModel.Edited -= OnCatalogEdited);
        subscription.Add(() => _groupsViewModel.Edited -= OnGroupsEdited);
        return subscription;
    }

    public string? ValidateBeforeSave()
    {
        return _groupsEdited && _groupsViewModel.HasErrors
            ? "Укажите название для каждой группы каналов перед сохранением."
            : _catalogEdited ? _catalogViewModel.ValidateAssignments() : null;
    }

    public void BeginSave(AppSettings targetSettings)
    {
    }

    public async Task<SettingsSectionSaveOutcome> SaveAsync(AppSettings targetSettings, bool appSettingsWritten)
    {
        if (!_catalogEdited && !_groupsEdited)
        {
            return SettingsSectionSaveOutcome.Clean;
        }

        if (_catalogEdited && _switchCoordinator is not null)
        {
            foreach (var target in _catalogViewModel.GetPendingActiveChanges(_catalogStore.Load()))
            {
                var result = await _switchCoordinator.SwitchCatalogChannelAsync(target);
                if (!result.Succeeded)
                {
                    return new(false, [], FailureMessage: result.ErrorMessage ?? "Не удалось переключить активный канал.");
                }

                _catalogViewModel.AcceptActiveSwitch(target.Key);
            }
        }

        var written = _catalogStore.Mutate(settings =>
        {
            if (_catalogEdited)
            {
                _catalogViewModel.ApplyTo(settings, applyActive: _switchCoordinator is null);
            }

            if (_groupsEdited)
            {
                _groupsViewModel.ApplyTo(settings);
            }
        });
        if (written && !_catalogStore.VerifyPersisted())
        {
            return new(false, [], FailureMessage: "Не удалось подтвердить запись каналов и назначений на диске.");
        }

        if (written)
        {
            if (_catalogEdited)
            {
                _catalogViewModel.AcceptSavedCatalog(_catalogStore.Load());
            }

            _catalogEdited = false;
            _groupsEdited = false;
        }

        return SettingsSectionSaveOutcome.FromWrite(written);
    }
}
