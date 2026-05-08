using PoproshaykaBot.Core.Infrastructure.Persistence;

namespace PoproshaykaBot.Core.Tests.Infrastructure.Persistence;

[TestFixture]
public sealed class AtomicFileTests
{
    [SetUp]
    public void SetUp()
    {
        _workDir = Path.Combine(Path.GetTempPath(), "AtomicFileTests_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_workDir);
        _targetPath = Path.Combine(_workDir, "data.json");
    }

    [TearDown]
    public void TearDown()
    {
        if (Directory.Exists(_workDir))
        {
            Directory.Delete(_workDir, true);
        }
    }

    private string _workDir = null!;
    private string _targetPath = null!;

    [Test]
    public void Save_NewFile_CreatesFileWithContent()
    {
        AtomicFile.Save(_targetPath, "{\"hello\":\"world\"}");

        using (Assert.EnterMultipleScope())
        {
            Assert.That(File.Exists(_targetPath), Is.True);
            Assert.That(File.ReadAllText(_targetPath), Is.EqualTo("{\"hello\":\"world\"}"));
        }
    }

    [Test]
    public void Save_NewFile_DoesNotCreateBackup()
    {
        AtomicFile.Save(_targetPath, "first");

        Assert.That(File.Exists(_targetPath + ".bak"), Is.False,
            "First write must not create a .bak — there is no previous content to back up.");
    }

    [Test]
    public void Save_OverExistingFile_KeepsBackupOfPrevious()
    {
        File.WriteAllText(_targetPath, "previous");

        AtomicFile.Save(_targetPath, "current");

        using (Assert.EnterMultipleScope())
        {
            Assert.That(File.ReadAllText(_targetPath), Is.EqualTo("current"));
            Assert.That(File.Exists(_targetPath + ".bak"), Is.True);
            Assert.That(File.ReadAllText(_targetPath + ".bak"), Is.EqualTo("previous"));
        }
    }

    [Test]
    public void Save_OverExistingFileTwice_KeepsLastGoodVersionInBackup()
    {
        File.WriteAllText(_targetPath, "v1");

        AtomicFile.Save(_targetPath, "v2");
        AtomicFile.Save(_targetPath, "v3");

        using (Assert.EnterMultipleScope())
        {
            Assert.That(File.ReadAllText(_targetPath), Is.EqualTo("v3"));
            Assert.That(File.ReadAllText(_targetPath + ".bak"), Is.EqualTo("v2"),
                ".bak must preserve the *last good* version so a crash rolls back to v{N-1}, not to the original baseline.");
        }
    }

    [Test]
    public void Save_DoesNotLeaveTempFile()
    {
        AtomicFile.Save(_targetPath, "content");

        using (Assert.EnterMultipleScope())
        {
            Assert.That(File.Exists(_targetPath + ".tmp"), Is.False);
            Assert.That(File.Exists(_targetPath + ".old"), Is.False);
        }
    }

    [Test]
    public void Save_CreatesMissingDirectories()
    {
        var nestedPath = Path.Combine(_workDir, "sub", "deeper", "file.json");

        AtomicFile.Save(nestedPath, "content");

        Assert.That(File.Exists(nestedPath), Is.True);
    }

    [Test]
    public void Save_NullArguments_Throws()
    {
        Assert.Throws<ArgumentException>(() => AtomicFile.Save("", "content"));
        Assert.Throws<ArgumentNullException>(() => AtomicFile.Save(null!, "content"));
        Assert.Throws<ArgumentNullException>(() => AtomicFile.Save(_targetPath, null!));
    }

    [Test]
    public void Save_PassesLoggerThrough_DoesNotThrow()
    {
        Assert.DoesNotThrow(() => AtomicFile.Save(_targetPath, "content", NullLogger.Instance));
    }
}
