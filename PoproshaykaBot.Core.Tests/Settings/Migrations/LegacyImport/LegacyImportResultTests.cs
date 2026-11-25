using PoproshaykaBot.Core.Settings.Migrations.LegacyImport;
using PoproshaykaBot.Core.Statistics;

namespace PoproshaykaBot.Core.Tests.Settings.Migrations.LegacyImport;

[TestFixture]
public class LegacyImportResultTests
{
    [TestCase(new[] { "users_statistics.json" }, true)]
    [TestCase(new[] { "youtube/users_statistics.json" }, true)]
    [TestCase(new[] { "youtube\\users_statistics.json" }, true)]
    [TestCase(new[] { "settings/settings.json", "users_statistics.json" }, true)]
    [TestCase(new[] { "bot_statistics.json" }, true)]
    [TestCase(new[] { "USERS_STATISTICS.JSON" }, true)]
    [TestCase(new[] { "YOUTUBE/USERS_STATISTICS.JSON" }, true)]
    [TestCase(new[] { "settings/settings.json" }, false)]
    [TestCase(new[] { "stream_sessions.json" }, false)]
    [TestCase(new string[0], false)]
    public void Перенос_считается_тронувшим_статистику_по_любому_из_двух_её_файлов(string[] copied, bool expected)
    {
        Assert.That(Build(copied).CopiedStatistics, Is.EqualTo(expected),
            "По этому признаку гашение решает, возвращать ли право сохранять статистику, поэтому имена файлов берутся из каталога переноса");
    }

    [Test]
    public void Область_внешней_записи_различает_пути_платформ_и_нормализует_слеши()
    {
        var result = Build(["youtube\\users_statistics.json"]);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.ExternalWrite.Touches("youtube/users_statistics.json"), Is.True);
            Assert.That(result.ExternalWrite.Touches("users_statistics.json"), Is.False);
            Assert.That(result.ExternalWrite, Is.EqualTo(StatisticsExternalWrite.Files("youtube/users_statistics.json")));
        }
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
            Assert.That(result.ExternalWrite, Is.EqualTo(new StatisticsExternalWrite(copied)),
                "Область внешней записи получает перенесённые относительные пути одним значением");
        }
    }

    [TestCase(new[] { "polls-history.json" }, true)]
    [TestCase(new[] { "POLLS-HISTORY.JSON" }, true)]
    [TestCase(new[] { "settings/settings.json", "polls-history.json" }, true)]
    [TestCase(new[] { "settings/polls.json" }, false)]
    [TestCase(new[] { "stream_sessions.json" }, false)]
    [TestCase(new string[0], false)]
    public void Перенос_считается_тронувшим_историю_голосований_по_её_файлу(string[] copied, bool expected)
    {
        var result = Build(copied);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.CopiedPollHistory, Is.EqualTo(expected),
                "Профили голосований (settings/polls.json) и их история – разные файлы, право записи отнимает только вторая");
            Assert.That(result.ExternalWrite.Touches("polls-history.json"), Is.EqualTo(expected));
        }
    }

    [TestCase(new[] { "settings/settings.json" }, new[] { "settings.json" })]
    [TestCase(new[] { "settings\\accounts.json", "users_statistics.json" }, new[] { "accounts.json" })]
    [TestCase(new[] { "settings/polls.json", "settings/update.json" }, new[] { "polls.json", "update.json" })]
    [TestCase(new[] { "settings/SETTINGS.JSON" }, new[] { "settings.json" })]
    [TestCase(new[] { "stream_sessions.json" }, new string[0])]
    [TestCase(new string[0], new string[0])]
    public void Перенос_считается_тронувшим_настройки_по_именам_файлов_из_каталога(string[] copied, string[] expected)
    {
        var result = Build(copied);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.CopiedSettingsFiles, Is.EquivalentTo(expected),
                "Имя файла берётся из каталога переноса, а путь в CopiedFiles лежит вместе с папкой settings – сверять их целиком нельзя");

            Assert.That(result.CopiedSettings, Is.EqualTo(expected.Length > 0),
                "По этому признаку строка итога переноса решает, называть ли настройки среди того, что до перезапуска не сохраняется");
        }
    }

    [Test]
    public void Настройки_вида_из_переноса_права_записи_ни_у_кого_не_отнимают()
    {
        Assert.That(Build(["settings/ui-preferences.toml"]).CopiedSettings, Is.False,
            "ui-preferences.toml не JsonStore: владельца с гейтом у него нет, и обещать замороженную запись было бы неправдой");
    }

    [Test]
    public void Переписанным_считается_и_скопированное_и_то_что_переписала_миграция_раскладки()
    {
        var result = new LegacyImportResult
        {
            SourcePath = @"C:\old",
            TargetPath = @"C:\new",
            CopiedFiles = ["settings/settings.json", "settings/accounts.json"],
            SkippedFiles = [],
            Failures = [],
            MigratedSettingsFiles = ["ACCOUNTS.JSON", "polls.json"],
        };

        Assert.That(result.RewrittenSettingsFiles, Is.EquivalentTo(new[] { "settings.json", "accounts.json", "polls.json" }),
            "Гейт ключуется именем файла, поэтому принесённое копированием и переписанное миграцией раскладки сходятся в один список без повторов");
    }

    [TestCase(new[] { "chat-history/0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef.json" }, true)]
    [TestCase(new[] { "chat-history\\0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef.json" }, true)]
    [TestCase(new[] { "settings/chat.json" }, false)]
    [TestCase(new string[0], false)]
    public void Перенос_считается_тронувшим_кэш_истории_чата_по_папке(string[] copied, bool expected)
    {
        Assert.That(Build(copied).CopiedChatHistory, Is.EqualTo(expected));
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
        };
    }
}
