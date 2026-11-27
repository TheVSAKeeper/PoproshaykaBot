using System.IO;
using KeepShell.Testing;
using Microsoft.Extensions.Logging.Abstractions;
using PoproshaykaBot.Core.Dashboard;
using PoproshaykaBot.Core.Settings.Ui;
using PoproshaykaBot.Wpf.Infrastructure.Dashboard;
using PoproshaykaBot.Wpf.Tests.Support;
using PoproshaykaBot.Wpf.ViewModels;
using PoproshaykaBot.Wpf.ViewModels.Tiles;

namespace PoproshaykaBot.Wpf.Tests.Dashboard;

[TestFixture]
public sealed class DashboardPresetsTests
{
    private static DashboardTileViewModel[] AllTiles() =>
    [
        new FakeTile("stream-channels", sizesToContent: true, maxWidth: 420, maxHeight: 340, minHeight: 194),
        new FakeTile("stream-info", sizesToContent: true, maxWidth: 420, maxHeight: 320, minHeight: 194),
        new FakeTile("broadcast-status", sizesToContent: true, maxWidth: 360, minHeight: 160),
        new FakeTile("broadcast-profiles", maxWidth: 500, maxHeight: 320, minHeight: 144),
        new FakeTile("polls-control", maxWidth: 500, maxHeight: 320, minHeight: 144),
        new FakeTile("obs-info", sizesToContent: true, maxHeight: 720, minHeight: 154),
        new FakeTile("stream-preview", sizesToContent: true, maxHeight: 280),
        new FakeTile("chat-overlay-preview", fills: true, minWidth: 320, minHeight: 234),
        new FakeTile("twitch-chat", fills: true, minWidth: 280, minHeight: 234),
        new FakeTile("multiplatform-chat", fills: true, grows: true, minWidth: 280, minHeight: 220),
        new FakeTile("multistream", grows: true, maxWidth: 500, minWidth: 280, minHeight: 324),
    ];

    [Test]
    public void Компактный_пресет_строится_гильотинно_и_не_уходит_в_стопку_при_970x1050()
    {
        var compact = DashboardLayoutDefaults.Create();
        DashboardLayoutReconciler.SyncRoot(compact);

        var store = new FakeLayoutStore(compact);
        var coordinator = new DashboardLayoutCoordinator(store);
        using var dashboard = new DashboardViewModel(AllTiles(), coordinator, TimeProvider.System);

        var contentWithExpandedRail = 970d - DashboardPreviewReference.NavWidthExpanded;
        var contentWithCollapsedRail = 970d - DashboardPreviewReference.NavWidthCollapsed;

        Assert.Multiple(() =>
        {
            Assert.That(compact.Root, Is.Not.Null);
            Assert.That(dashboard.Pane, Is.Not.Null);
            Assert.That(dashboard.ActivePreset, Is.EqualTo(DashboardLayoutPreset.Compact));
            Assert.That(dashboard.Pane!.MinWidth(DashboardPaneSurface.ScaledStarBandMinWidth), Is.LessThanOrEqualTo(540));
            Assert.That(DashboardPaneSurface.ShouldStack(contentWithExpandedRail, dashboard.Pane), Is.False);
            Assert.That(DashboardPaneSurface.ShouldStack(contentWithCollapsedRail, dashboard.Pane), Is.False);

            var visibleIds = compact.Tiles.Where(t => t.IsVisible).Select(t => t.TypeId).ToArray();
            Assert.That(visibleIds, Is.EquivalentTo(new[]
            {
                "stream-channels",
                "broadcast-profiles",
                "obs-info",
                "twitch-chat",
            }));

            var hiddenIds = compact.Tiles.Where(t => !t.IsVisible).Select(t => t.TypeId).ToArray();
            Assert.That(hiddenIds, Is.EquivalentTo(new[]
            {
                "broadcast-status",
                "chat-overlay-preview",
                "multiplatform-chat",
                "stream-info",
                "multistream",
                "polls-control",
                "stream-preview",
            }));
        });
    }

    [Test]
    public void Широкий_пресет_сохраняет_все_широкие_плитки_и_скрывает_единую_плитку()
    {
        var wide = DashboardLayoutPresets.CreateWide();
        DashboardLayoutReconciler.SyncRoot(wide);

        Assert.Multiple(() =>
        {
            Assert.That(wide.ColumnCount, Is.EqualTo(4));
            Assert.That(wide.RowCount, Is.EqualTo(5));
            Assert.That(wide.Root, Is.Not.Null);
            Assert.That(DashboardLayoutPresets.DetectPreset(wide), Is.EqualTo(DashboardLayoutPreset.Wide));
            Assert.That(wide.Tiles.Single(t => t.TypeId == "stream-channels").IsVisible, Is.False);
            Assert.That(wide.Tiles.Single(t => t.TypeId == "stream-info").IsVisible, Is.True);
            Assert.That(wide.Tiles.Single(t => t.TypeId == "multistream").IsVisible, Is.True);
        });
    }

    [Test]
    public void Существующая_раскладка_на_диске_не_перезаписывается_при_загрузке_дашборда()
    {
        var existingWide = DashboardLayoutPresets.CreateWide();
        var store = new FakeLayoutStore(existingWide);
        var coordinator = new DashboardLayoutCoordinator(store);

        using var dashboard = new DashboardViewModel(AllTiles(), coordinator, TimeProvider.System);

        Assert.Multiple(() =>
        {
            Assert.That(dashboard.ActivePreset, Is.EqualTo(DashboardLayoutPreset.Wide));
            Assert.That(store.LoadDashboard()!.ColumnCount, Is.EqualTo(4));
        });
    }

    [Test]
    public void Переключение_пресета_через_команду_сохраняет_чужие_плитки_и_обновляет_настройки()
    {
        var dir = Directory.CreateTempSubdirectory("poproshayka-presets-");
        try
        {
            var initial = DashboardLayoutPresets.CreateCompact();
            initial.Tiles.Add(new() { Id = "logs", TypeId = "logs", IsVisible = true, Order = 99 });

            var store = new FakeLayoutStore(initial);
            var coordinator = new DashboardLayoutCoordinator(store);
            var uiSettings = new MemorySettings();
            var preferences = new ShellPreferences(uiSettings);

            using var dashboard = new DashboardViewModel(AllTiles(), coordinator, TimeProvider.System, preferences: preferences);
            Assert.That(dashboard.ActivePreset, Is.EqualTo(DashboardLayoutPreset.Compact));

            dashboard.ApplyPresetCommand.Execute(DashboardLayoutPreset.Wide);

            var savedAfterWide = store.LoadDashboard()!;
            Assert.Multiple(() =>
            {
                Assert.That(dashboard.ActivePreset, Is.EqualTo(DashboardLayoutPreset.Wide));
                Assert.That(preferences.DashboardPreset, Is.EqualTo(DashboardLayoutPreset.Wide));
                Assert.That(savedAfterWide.Tiles.Any(t => t.TypeId == "logs" && t.IsVisible), Is.True);
                Assert.That(savedAfterWide.Tiles.Single(t => t.TypeId == "stream-channels").IsVisible, Is.False);
            });

            dashboard.ApplyPresetCommand.Execute(DashboardLayoutPreset.Compact);

            var savedAfterCompact = store.LoadDashboard()!;
            Assert.Multiple(() =>
            {
                Assert.That(dashboard.ActivePreset, Is.EqualTo(DashboardLayoutPreset.Compact));
                Assert.That(preferences.DashboardPreset, Is.EqualTo(DashboardLayoutPreset.Compact));
                Assert.That(savedAfterCompact.Tiles.Any(t => t.TypeId == "logs" && t.IsVisible), Is.True);
                Assert.That(savedAfterCompact.Tiles.Single(t => t.TypeId == "stream-channels").IsVisible, Is.True);
                Assert.That(savedAfterCompact.Tiles.Single(t => t.TypeId == "stream-info").IsVisible, Is.False);
            });
        }
        finally
        {
            Directory.Delete(dir.FullName, recursive: true);
        }
    }
}
