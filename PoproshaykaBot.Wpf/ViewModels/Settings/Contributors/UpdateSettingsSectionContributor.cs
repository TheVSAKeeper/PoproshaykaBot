using MahApps.Metro.IconPacks;
using PoproshaykaBot.Core.Settings;
using PoproshaykaBot.Core.Settings.Stores;
using PoproshaykaBot.Core.Settings.Update;
using System.ComponentModel;

namespace PoproshaykaBot.Wpf.ViewModels.Settings.Contributors;

public sealed class UpdateSettingsSectionContributor : ISettingsSectionContributor
{
    private static readonly HashSet<string> NonDirtyProperties = new(StringComparer.Ordinal)
    {
        nameof(UpdateSettingsSectionViewModel.IsBusy),
        nameof(UpdateSettingsSectionViewModel.DownloadProgress),
        nameof(UpdateSettingsSectionViewModel.CurrentVersionText),
        nameof(UpdateSettingsSectionViewModel.StatusText),
        nameof(UpdateSettingsSectionViewModel.StatusSeverity),
        nameof(UpdateSettingsSectionViewModel.IsFrameworkDependentVisible),
        nameof(UpdateSettingsSectionViewModel.RepositoryHintText),
        nameof(UpdateSettingsSectionViewModel.RepositoryHintSeverity),
        "HasErrors",
    };

    private readonly UpdateSettingsSectionViewModel _update;
    private readonly UpdateStore _updateStore;
    private UpdateSettings _draft = new();

    public UpdateSettingsSectionContributor(UpdateSettingsSectionViewModel update, UpdateStore updateStore)
    {
        ArgumentNullException.ThrowIfNull(update);
        ArgumentNullException.ThrowIfNull(updateStore);

        _update = update;
        _updateStore = updateStore;
    }

    public int Order => 8;

    public string SectionKey => "update";

    public SettingsSection CreateSection()
    {
        return new(
            SectionKey,
            "Обновления",
            PackIconLucideKind.Download,
            "github релизы версия репозиторий проверка загрузка установка портативная сборка");
    }

    public IReadOnlyList<SettingsSectionBlockDescriptor> GetBlocks()
    {
        return
        [
            new(string.Empty, "Обновления", null, _update),
        ];
    }

    public void Load(SettingsSessionState session)
    {
        _draft = JsonStoreClone.DeepClone(_updateStore.Load());
        _update.LoadSettings(_draft);
    }

    public IDisposable SubscribeDirty(SettingsSessionState session, Action markDirty, Action notifyChangesState)
    {
        ArgumentNullException.ThrowIfNull(markDirty);

        var subscription = new CompositeSettingsSubscription();
        subscription.TrackPropertyChanged(_update, (_, e) =>
        {
            if (e.PropertyName is { } name && NonDirtyProperties.Contains(name))
            {
                return;
            }

            markDirty();
        });

        return subscription;
    }

    public void BeginSave(AppSettings targetSettings)
    {
        _update.SaveSettings(_draft);
    }

    public async Task<SettingsSectionSaveOutcome> SaveAsync(AppSettings targetSettings, bool appSettingsWritten)
    {
        var storedUpdate = _updateStore.Load();
        _draft.SkippedVersion = storedUpdate.SkippedVersion;
        _draft.LastCheckUtc = storedUpdate.LastCheckUtc;
        _updateStore.Save(_draft);
        await _update.OnSavedAsync(storedUpdate.ApplyMode);

        return SettingsSectionSaveOutcome.Clean;
    }
}
