using PoproshaykaBot.Core.Settings.Migrations.LegacyImport;

namespace PoproshaykaBot.Core.Tests.Settings.Migrations.LegacyImport;

[TestFixture]
public class LegacyImportResultTests
{
    [TestCase(new[] { "users_statistics.json" }, true)]
    [TestCase(new[] { "settings/settings.json", "users_statistics.json" }, true)]
    [TestCase(new[] { "bot_statistics.json" }, true)]
    [TestCase(new[] { "USERS_STATISTICS.JSON" }, true)]
    [TestCase(new[] { "settings/settings.json" }, false)]
    [TestCase(new string[0], false)]
    public void Перенос_считается_тронувшим_статистику_по_любому_из_двух_её_файлов(string[] copied, bool expected)
    {
        var result = new LegacyImportResult
        {
            SourcePath = @"C:\old",
            TargetPath = @"C:\new",
            CopiedFiles = copied,
            SkippedFiles = [],
            Failures = [],
            RequiresReauthorization = false,
        };

        Assert.That(result.CopiedStatistics, Is.EqualTo(expected),
            "По этому признаку гашение решает, возвращать ли право сохранять статистику, поэтому имена файлов берутся из каталога переноса");
    }
}
