using MahApps.Metro.IconPacks;
using PoproshaykaBot.Core.Settings;

namespace PoproshaykaBot.Wpf.ViewModels.Settings.Contributors;

public sealed class UpdateSettingsSectionContributor : ISettingsSectionContributor
{
    private static readonly HashSet<string> NonDirtyProperties = new(StringComparer.Ordinal)
    {
        nameof(UpdateSettingsSectionViewModel.ApplyModes),
        nameof(UpdateSettingsSectionViewModel.DefaultRepository),
        nameof(UpdateSettingsSectionViewModel.SkippedVersion),
        nameof(UpdateSettingsSectionViewModel.HasSkippedVersion),
        nameof(UpdateSettingsSectionViewModel.IsUpdatable),
        nameof(UpdateSettingsSectionViewModel.IsFrameworkDependentBuild),
        nameof(UpdateSettingsSectionViewModel.IsRepositoryOverrideValid),
        nameof(UpdateSettingsSectionViewModel.LastCheckText),
        nameof(UpdateSettingsSectionViewModel.Stage),
        nameof(UpdateSettingsSectionViewModel.Candidate),
        nameof(UpdateSettingsSectionViewModel.Pending),
        nameof(UpdateSettingsSectionViewModel.Progress),
        nameof(UpdateSettingsSectionViewModel.Error),
        nameof(UpdateSettingsSectionViewModel.IsDownloading),
        nameof(UpdateSettingsSectionViewModel.IsChecking),
        nameof(UpdateSettingsSectionViewModel.IsBusy),
        nameof(UpdateSettingsSectionViewModel.CanCheckNow),
        nameof(UpdateSettingsSectionViewModel.DownloadProgress),
        nameof(UpdateSettingsSectionViewModel.CurrentVersionText),
        nameof(UpdateSettingsSectionViewModel.StatusText),
        nameof(UpdateSettingsSectionViewModel.StatusSeverity),
        nameof(UpdateSettingsSectionViewModel.RepositoryHintText),
        nameof(UpdateSettingsSectionViewModel.RepositoryHintSeverity),
        "HasErrors",
    };

    private readonly UpdateSettingsSectionViewModel _update;

    public UpdateSettingsSectionContributor(UpdateSettingsSectionViewModel update)
    {
        ArgumentNullException.ThrowIfNull(update);

        _update = update;
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
        _update.Reload();
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
    }

    public Task<SettingsSectionSaveOutcome> SaveAsync(AppSettings targetSettings, bool appSettingsWritten)
    {
        _update.Save();
        return Task.FromResult(SettingsSectionSaveOutcome.Clean);
    }
}
