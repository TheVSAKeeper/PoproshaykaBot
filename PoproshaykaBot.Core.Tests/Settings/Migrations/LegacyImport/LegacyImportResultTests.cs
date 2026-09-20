using PoproshaykaBot.Core.Settings.Migrations.LegacyImport;
using PoproshaykaBot.Core.Statistics;

namespace PoproshaykaBot.Core.Tests.Settings.Migrations.LegacyImport;

[TestFixture]
public class LegacyImportResultTests
{
    [TestCase(new[] { "users_statistics.json" }, true)]
    [TestCase(new[] { "settings/settings.json", "users_statistics.json" }, true)]
    [TestCase(new[] { "bot_statistics.json" }, true)]
    [TestCase(new[] { "USERS_STATISTICS.JSON" }, true)]
    [TestCase(new[] { "settings/settings.json" }, false)]
    [TestCase(new[] { "stream_sessions.json" }, false)]
    [TestCase(new string[0], false)]
    public void Перенос_считается_тронувшим_статистику_по_любому_из_двух_её_файлов(string[] copied, bool expected)
    {
        Assert.That(Build(copied).CopiedStatistics, Is.EqualTo(expected),
            "По этому признаку гашение решает, возвращать ли право сохранять статистику, поэтому имена файлов берутся из каталога переноса");
    }

    [TestCase(new[] { "stream_sessions.json" }, true)]
    [TestCase(new[] { "STREAM_SESSIONS.JSON" }, true)]
    [TestCase(new[] { "settings/settings.json", "stream_sessions.json" }, true)]
    [TestCase(new[] { "users_statistics.json" }, false)]
    [TestCase(new string[0], false)]
    public void Перенос_считается_тронувшим_историю_стримов_по_её_файлу(string[] copied, bool expected)
    {
        var result = Build(copied);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.CopiedStreamHistory, Is.EqualTo(expected),
                "По этому признаку гашение решает, возвращать ли стору истории право писать, поэтому имя файла берётся из каталога переноса");
            Assert.That(result.ExternalWrite, Is.EqualTo(new StatisticsExternalWrite(result.CopiedStatistics, expected)),
                "Область внешней записи получает обе половины одним значением – собирать их на стороне хоста нечем");
        }
    }

    private static LegacyImportResult Build(string[] copied)
    {
        return new()
        {
            SourcePath = @"C:\old",
            TargetPath = @"C:\new",
            CopiedFiles = copied,
            SkippedFiles = [],
            Failures = [],
            RequiresReauthorization = false,
        };
    }
}
