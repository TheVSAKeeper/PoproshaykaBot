using MahApps.Metro.IconPacks;
using PoproshaykaBot.Core.Dashboard;
using PoproshaykaBot.Core.Settings;

namespace PoproshaykaBot.Wpf.ViewModels.Settings.Contributors;

public sealed class DashboardSettingsSectionContributor : ISettingsSectionContributor
{
    private readonly DashboardLayoutSectionViewModel _dashboardLayout;
    private readonly DashboardLayoutCoordinator _coordinator;
    private int _revision;
    private bool _edited;

    public DashboardSettingsSectionContributor(
        DashboardLayoutSectionViewModel dashboardLayout,
        DashboardLayoutCoordinator coordinator)
    {
        ArgumentNullException.ThrowIfNull(dashboardLayout);
        ArgumentNullException.ThrowIfNull(coordinator);

        _dashboardLayout = dashboardLayout;
        _coordinator = coordinator;
    }

    public int Order => 7;

    public string SectionKey => "dashboard";

    public bool IsFullWidth => true;

    public SettingsSection CreateSection()
    {
        return new(
            SectionKey,
            "Дашборд",
            PackIconLucideKind.LayoutDashboard,
            "раскладка плитки сетка колонки строки обзор палитра перетаскивание");
    }

    public IReadOnlyList<SettingsSectionBlockDescriptor> GetBlocks()
    {
        return
        [
            new(string.Empty, "Дашборд", null, _dashboardLayout),
        ];
    }

    public void Load(SettingsSessionState session)
    {
        var snapshot = _coordinator.Read();
        _revision = snapshot.Revision;
        _edited = false;
        _dashboardLayout.LoadSettings(snapshot.Layout);
    }

    public IDisposable SubscribeDirty(SettingsSessionState session, Action markDirty, Action notifyChangesState)
    {
        ArgumentNullException.ThrowIfNull(markDirty);

        void OnEdited(object? sender, EventArgs e)
        {
            _edited = true;
            markDirty();
        }

        _dashboardLayout.Edited += OnEdited;

        var subscription = new CompositeSettingsSubscription();
        subscription.Add(() => _dashboardLayout.Edited -= OnEdited);
        return subscription;
    }

    public void BeginSave(AppSettings targetSettings)
    {
    }

    public Task<SettingsSectionSaveOutcome> SaveAsync(AppSettings targetSettings, bool appSettingsWritten)
    {
        if (_edited)
        {
            var layout = _dashboardLayout.BuildLayout();
            _revision = _coordinator
                .Commit(layout, _revision, _dashboardLayout.TreeEdited)
                .Snapshot.Revision;
            _edited = false;
        }

        return Task.FromResult(SettingsSectionSaveOutcome.Clean);
    }
}
