using PoproshaykaBot.Core.Infrastructure;

namespace PoproshaykaBot.Core.Tests.Infrastructure;

[TestFixture]
public sealed class AppPathsTests
{
    private string _executableDir = null!;
    private string? _previousOverride;

    [SetUp]
    public void SetUp()
    {
        _executableDir = Path.Combine(Path.GetTempPath(), $"poproshayka-apppaths-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_executableDir);
        _previousOverride = Environment.GetEnvironmentVariable(AppPaths.BaseDirectoryEnvironmentVariable);
    }

    [TearDown]
    public void TearDown()
    {
        Environment.SetEnvironmentVariable(AppPaths.BaseDirectoryEnvironmentVariable, _previousOverride);
        AppPaths.Refresh();

        if (Directory.Exists(_executableDir))
        {
            Directory.Delete(_executableDir, recursive: true);
        }
    }

    [Test]
    public void ResolvePortable_DefaultsToPortable_AndSwitchesToAppDataWhenMarkerExists()
    {
        Assert.That(AppPaths.ResolvePortable(_executableDir), Is.True);

        AppPaths.SetAppDataStorageMarker(true, _executableDir);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(File.Exists(Path.Combine(_executableDir, AppPaths.AppDataMarkerFileName)), Is.True);
            Assert.That(AppPaths.ResolvePortable(_executableDir), Is.False);
            Assert.That(AppPaths.IsPortable, Is.False);
            Assert.That(AppPaths.DefaultBaseDirectory, Is.EqualTo(AppPaths.AppDataDirectory));
        }

        AppPaths.SetAppDataStorageMarker(false, _executableDir);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(File.Exists(Path.Combine(_executableDir, AppPaths.AppDataMarkerFileName)), Is.False);
            Assert.That(AppPaths.ResolvePortable(_executableDir), Is.True);
            Assert.That(AppPaths.IsPortable, Is.True);
            Assert.That(AppPaths.DefaultBaseDirectory, Is.EqualTo(_executableDir));
        }
    }

    [Test]
    public void Refresh_PreservesEnvironmentOverrideAtHighestPriority()
    {
        var customDir = Path.Combine(_executableDir, "custom-override");
        Environment.SetEnvironmentVariable(AppPaths.BaseDirectoryEnvironmentVariable, customDir);

        AppPaths.SetAppDataStorageMarker(true, _executableDir);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(AppPaths.IsBaseDirectoryOverridden, Is.True);
            Assert.That(AppPaths.BaseDirectory, Is.EqualTo(Path.GetFullPath(customDir)));
            Assert.That(AppPaths.DefaultBaseDirectory, Is.EqualTo(AppPaths.AppDataDirectory));
        }
    }
}
