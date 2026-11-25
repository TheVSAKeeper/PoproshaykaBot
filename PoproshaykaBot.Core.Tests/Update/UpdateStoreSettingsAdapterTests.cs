using PoproshaykaBot.Core.Settings.Stores;
using PoproshaykaBot.Core.Update;
using CoreUpdateApplyMode = PoproshaykaBot.Core.Settings.Update.UpdateApplyMode;
using KeepUpdateApplyMode = KeepShell.Updates.UpdateApplyMode;

namespace PoproshaykaBot.Core.Tests.Update;

[TestFixture]
public sealed class UpdateStoreSettingsAdapterTests
{
    private string _root = null!;

    [SetUp]
    public void SetUp()
    {
        _root = Path.Combine(Path.GetTempPath(), "poproshayka-update-adapter-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
    }

    [TearDown]
    public void TearDown()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, true);
        }
    }

    private UpdateStore CreateStore() => new(null, Path.Combine(_root, "update.json"));

    [Test]
    public void ReadsAndMapsAllSettingsFromStore()
    {
        var store = CreateStore();
        var checkedAt = new DateTimeOffset(2026, 11, 25, 12, 30, 0, TimeSpan.Zero);
        store.Save(new()
        {
            AutoCheckEnabled = false,
            CheckIntervalHours = 12,
            ApplyMode = CoreUpdateApplyMode.SilentOnExit,
            AllowFrameworkDependentUpdate = true,
            SkippedVersion = "3.1.0.9",
            RepositoryOverride = "fork/PoproshaykaBot",
            LastCheckUtc = checkedAt,
        });

        var adapter = new UpdateStoreSettingsAdapter(store);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(adapter.AutomaticCheckEnabled, Is.False);
            Assert.That(adapter.CheckInterval, Is.EqualTo(TimeSpan.FromHours(12)));
            Assert.That(adapter.ApplyMode, Is.EqualTo(KeepUpdateApplyMode.Automatic));
            Assert.That(adapter.AllowFrameworkDependentUpdate, Is.True);
            Assert.That(adapter.SkippedVersion, Is.EqualTo("3.1.0.9"));
            Assert.That(adapter.RepositoryOverride, Is.EqualTo("fork/PoproshaykaBot"));
            Assert.That(adapter.LastCheckUtc, Is.EqualTo(checkedAt));
        }
    }

    [Test]
    public void Save_PersistsMutatedValues_WithoutLosingExternalUpdates()
    {
        var store = CreateStore();
        var adapter = new UpdateStoreSettingsAdapter(store);
        var checkedAt = new DateTimeOffset(2026, 11, 25, 14, 0, 0, TimeSpan.Zero);

        adapter.AutomaticCheckEnabled = false;
        adapter.CheckInterval = TimeSpan.FromMinutes(20);
        adapter.ApplyMode = KeepUpdateApplyMode.Automatic;
        adapter.AllowFrameworkDependentUpdate = true;
        adapter.SkippedVersion = " 3.1.0.10 ";
        adapter.RepositoryOverride = "   ";
        adapter.LastCheckUtc = checkedAt;

        var external = store.Load();
        external.RepositoryOverride = "other/repo";
        store.Save(external);

        adapter.Save();

        var reloaded = new UpdateStore(null, Path.Combine(_root, "update.json")).Load();
        using (Assert.EnterMultipleScope())
        {
            Assert.That(reloaded.AutoCheckEnabled, Is.False);
            Assert.That(reloaded.CheckIntervalHours, Is.EqualTo(1));
            Assert.That(reloaded.ApplyMode, Is.EqualTo(CoreUpdateApplyMode.SilentOnExit));
            Assert.That(reloaded.AllowFrameworkDependentUpdate, Is.True);
            Assert.That(reloaded.SkippedVersion, Is.EqualTo("3.1.0.10"));
            Assert.That(reloaded.RepositoryOverride, Is.Null);
            Assert.That(reloaded.LastCheckUtc, Is.EqualTo(checkedAt));
        }
    }

    [Test]
    public void ReflectsExternalStoreChanges_WhenNoPendingEditOverridesProperty()
    {
        var store = CreateStore();
        var adapter = new UpdateStoreSettingsAdapter(store);

        Assert.That(adapter.RepositoryOverride, Is.Null);

        var updated = store.Load();
        updated.RepositoryOverride = "fork/PoproshaykaBot";
        updated.ApplyMode = CoreUpdateApplyMode.SilentOnExit;
        store.Save(updated);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(adapter.RepositoryOverride, Is.EqualTo("fork/PoproshaykaBot"));
            Assert.That(adapter.ApplyMode, Is.EqualTo(KeepUpdateApplyMode.Automatic));
        }
    }
}
