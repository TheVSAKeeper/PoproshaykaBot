using System.IO;
using KeepShell.Testing;
using Microsoft.Extensions.Logging.Abstractions;
using PoproshaykaBot.Core.Dashboard;
using PoproshaykaBot.Core.Settings.Stores;
using PoproshaykaBot.Core.Settings.Ui;
using PoproshaykaBot.Wpf.Infrastructure.Dashboard;
using PoproshaykaBot.Wpf.Tests.Support;
using PoproshaykaBot.Wpf.ViewModels;
using PoproshaykaBot.Wpf.ViewModels.Settings;
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
        var seedStore = new FakeLayoutStore(existingWide);
        using (var seed = new DashboardViewModel(AllTiles(), new DashboardLayoutCoordinator(seedStore), TimeProvider.System))
        {
            Assert.That(seed.ActivePreset, Is.EqualTo(DashboardLayoutPreset.Wide));
        }

        var snapshotBefore = seedStore.LoadDashboard()!;
        var store = new FakeLayoutStore(JsonStoreClone.DeepClone(snapshotBefore));
        var coordinator = new DashboardLayoutCoordinator(store);

        using var dashboard = new DashboardViewModel(AllTiles(), coordinator, TimeProvider.System);
        var loaded = store.LoadDashboard()!;

        Assert.Multiple(() =>
        {
            Assert.That(store.SaveCount, Is.Zero, "Существующая раскладка на диске не должна перезаписываться при старте.");
            Assert.That(store.Saved, Is.Null);
            Assert.That(dashboard.ActivePreset, Is.EqualTo(DashboardLayoutPreset.Wide));
            Assert.That(loaded.ColumnCount, Is.EqualTo(snapshotBefore.ColumnCount));
            Assert.That(loaded.RowCount, Is.EqualTo(snapshotBefore.RowCount));
            AssertTilesEqual(loaded.Tiles, snapshotBefore.Tiles);
            AssertRootEqual(loaded.Root, snapshotBefore.Root);
        });
    }

    [Test]
    public void Перестановка_или_изменение_весов_плиток_переводит_пресет_в_свою_раскладку()
    {
        var rearranged = DashboardLayoutPresets.CreateWide();
        var a = rearranged.Tiles.Single(t => t.TypeId == "stream-info");
        var b = rearranged.Tiles.Single(t => t.TypeId == "broadcast-profiles");
        (a.Row, b.Row) = (b.Row, a.Row);
        (a.RowSpan, b.RowSpan) = (b.RowSpan, a.RowSpan);
        DashboardLayoutReconciler.SyncRoot(rearranged);

        Assert.That(DashboardLayoutPresets.DetectPreset(rearranged), Is.EqualTo(DashboardLayoutPreset.Custom));

        var resized = DashboardLayoutPresets.CreateWide();
        DashboardLayoutReconciler.SyncRoot(resized);
        Assert.That(DashboardPaneEditor.TryResize(resized.Root!, [], [0.40, 0.20, 0.20, 0.20], out var weightedRoot), Is.True);
        resized.Root = weightedRoot;

        var store = new FakeLayoutStore(resized);
        var coordinator = new DashboardLayoutCoordinator(store);
        using var dashboard = new DashboardViewModel(AllTiles(), coordinator, TimeProvider.System);

        Assert.That(dashboard.ActivePreset, Is.EqualTo(DashboardLayoutPreset.Custom));

        dashboard.ApplyPresetCommand.Execute(DashboardLayoutPreset.Wide);
        Assert.That(dashboard.ActivePreset, Is.EqualTo(DashboardLayoutPreset.Wide));
    }

    [Test]
    public void Переключение_пресета_через_команду_сохраняет_чужие_плитки_и_синхронизируется_с_настройками()
    {
        var initial = DashboardLayoutPresets.CreateCompact();
        initial.Tiles.Add(new() { Id = "logs", TypeId = "logs", IsVisible = true, Order = 99 });

        var store = new FakeLayoutStore(initial);
        var coordinator = new DashboardLayoutCoordinator(store);
        var uiSettings = new MemorySettings();

        using var dashboard = new DashboardViewModel(AllTiles(), coordinator, TimeProvider.System);
        var section = new DashboardLayoutSectionViewModel(
            AllTiles(),
            uiSettings,
            new StubDashboardLayoutStore());
        section.LoadSettings(store.LoadDashboard());
        Assert.Multiple(() =>
        {
            Assert.That(dashboard.ActivePreset, Is.EqualTo(DashboardLayoutPreset.Compact));
            Assert.That(section.ActivePreset, Is.EqualTo(DashboardLayoutPreset.Compact));
        });

        dashboard.ApplyPresetCommand.Execute(DashboardLayoutPreset.Wide);
        section.LoadSettings(store.LoadDashboard());

        var savedAfterWide = store.LoadDashboard()!;
        Assert.Multiple(() =>
        {
            Assert.That(dashboard.ActivePreset, Is.EqualTo(DashboardLayoutPreset.Wide));
            Assert.That(section.ActivePreset, Is.EqualTo(DashboardLayoutPreset.Wide));
            Assert.That(uiSettings.GetStringValue("ui.dashboard.preset"), Is.Null.Or.Empty);
            Assert.That(savedAfterWide.Tiles.Any(t => t.TypeId == "logs" && t.IsVisible), Is.True);
            Assert.That(savedAfterWide.Tiles.Single(t => t.TypeId == "stream-channels").IsVisible, Is.False);
        });

        section.ApplyPresetCommand.Execute(DashboardLayoutPreset.Compact);
        coordinator.Commit(section.BuildLayout(), coordinator.Read().Revision, keepDraftRoot: true);
        dashboard.Reload();

        var savedAfterCompact = store.LoadDashboard()!;
        Assert.Multiple(() =>
        {
            Assert.That(dashboard.ActivePreset, Is.EqualTo(DashboardLayoutPreset.Compact));
            Assert.That(section.ActivePreset, Is.EqualTo(DashboardLayoutPreset.Compact));
            Assert.That(savedAfterCompact.Tiles.Any(t => t.TypeId == "logs" && t.IsVisible), Is.True);
            Assert.That(savedAfterCompact.Tiles.Single(t => t.TypeId == "stream-channels").IsVisible, Is.True);
            Assert.That(savedAfterCompact.Tiles.Single(t => t.TypeId == "stream-info").IsVisible, Is.False);
        });
    }

    private static void AssertTilesEqual(IReadOnlyList<DashboardTileSettings> actual, IReadOnlyList<DashboardTileSettings> expected)
    {
        Assert.That(actual, Has.Count.EqualTo(expected.Count));
        foreach (var exp in expected)
        {
            var act = actual.Single(t => t.TypeId == exp.TypeId);
            Assert.That(act.Row, Is.EqualTo(exp.Row), exp.TypeId);
            Assert.That(act.Column, Is.EqualTo(exp.Column), exp.TypeId);
            Assert.That(act.ColumnSpan, Is.EqualTo(exp.ColumnSpan), exp.TypeId);
            Assert.That(act.RowSpan, Is.EqualTo(exp.RowSpan), exp.TypeId);
            Assert.That(act.IsVisible, Is.EqualTo(exp.IsVisible), exp.TypeId);
            Assert.That(act.IsCollapsed, Is.EqualTo(exp.IsCollapsed), exp.TypeId);
        }
    }

    private static void AssertRootEqual(DashboardPane? actual, DashboardPane? expected)
    {
        if (expected is null)
        {
            Assert.That(actual, Is.Null);
            return;
        }

        Assert.That(actual, Is.Not.Null);
        switch (actual, expected)
        {
            case (TilePane a, TilePane b):
                Assert.That(a.TypeId, Is.EqualTo(b.TypeId));
                break;
            case (SplitPane a, SplitPane b):
                Assert.That(a.Orientation, Is.EqualTo(b.Orientation));
                Assert.That(a.Children, Has.Count.EqualTo(b.Children.Count));
                for (var i = 0; i < b.Children.Count; i++)
                {
                    Assert.That(a.Children[i].Weight, Is.EqualTo(b.Children[i].Weight));
                    AssertRootEqual(a.Children[i].Pane, b.Children[i].Pane);
                }
                break;
            default:
                Assert.Fail($"Несовпадение узлов дерева: {actual?.GetType().Name} != {expected.GetType().Name}");
                break;
        }
    }

    private sealed class StubDashboardLayoutStore()
        : DashboardLayoutStore(null, Path.Combine(Path.GetTempPath(), $"dashboard-preset-stub-{Guid.NewGuid():N}.json"))
    {
        public override MainWindowSettings? LoadMainWindow() => null;
    }
}
