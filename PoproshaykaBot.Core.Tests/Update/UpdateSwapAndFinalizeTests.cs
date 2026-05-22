using PoproshaykaBot.Core.Update;

namespace PoproshaykaBot.Core.Tests.Update;

[TestFixture]
public sealed class UpdateSwapAndFinalizeTests
{
    [Test]
    public void Plan_DerivesBackupAndKeepsStaged()
    {
        var plan = UpdateSwapPlanner.Plan(@"C:\app\Bot.exe", @"C:\app\update\Bot.new.exe");

        using (Assert.EnterMultipleScope())
        {
            Assert.That(plan.CurrentExecutable, Is.EqualTo(@"C:\app\Bot.exe"));
            Assert.That(plan.BackupExecutable, Is.EqualTo(@"C:\app\Bot.exe.old"));
            Assert.That(plan.StagedExecutable, Is.EqualTo(@"C:\app\update\Bot.new.exe"));
        }
    }

    [Test]
    public void Finalizer_RemovesBackupAndStagingDirectory()
    {
        var root = Path.Combine(Path.GetTempPath(), "poproshayka-update-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        try
        {
            var executablePath = Path.Combine(root, "Bot.exe");
            var backupPath = executablePath + ".old";
            var stagingDirectory = Path.Combine(root, "update");

            File.WriteAllText(executablePath, "current");
            File.WriteAllText(backupPath, "previous");
            Directory.CreateDirectory(stagingDirectory);
            File.WriteAllText(Path.Combine(stagingDirectory, "apply-update.json"), "{}");

            UpdateFinalizer.Run(stagingDirectory, executablePath, NullLogger.Instance);

            using (Assert.EnterMultipleScope())
            {
                Assert.That(File.Exists(backupPath), Is.False);
                Assert.That(Directory.Exists(stagingDirectory), Is.False);
                Assert.That(File.Exists(executablePath), Is.True, "Текущий исполняемый файл не должен удаляться");
            }

            Assert.DoesNotThrow(() => UpdateFinalizer.Run(stagingDirectory, executablePath, NullLogger.Instance));
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }
}
