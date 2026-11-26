using PoproshaykaBot.Core.Settings.Migrations.LegacyImport;
using PoproshaykaBot.Wpf.ViewModels.Migration;

namespace PoproshaykaBot.Wpf.Tests.Migration;

[TestFixture]
public class LegacyImportTextTests
{
    [Test]
    public void A_file_left_behind_keeps_the_headline_from_claiming_a_transfer()
    {
        var result = Build(inPlace: true, unmigrated: ["accounts.json"]);

        Assert.Multiple(() =>
        {
            Assert.That(LegacyImportText.BuildHeadline(result), Is.EqualTo("Часть данных осталась на прежнем месте."),
                "Ничего не переехало, поэтому «перенесено» было бы неправдой.");
            Assert.That(LegacyImportText.DescribeUnmigrated(result), Does.Contain("данные входа в Twitch"),
                "Файл называется по назначению, а не путём.");
            Assert.That(LegacyImportText.DescribeUnmigrated(result),
                Does.Contain("Исходные и рабочие файлы сохранены").And.Not.Contain("свеж"));
        });
    }

    [Test]
    public void Nothing_left_behind_leaves_the_result_screen_without_that_line()
    {
        Assert.That(LegacyImportText.DescribeUnmigrated(Build(copied: ["settings/settings.json"])), Is.Null);
    }

    [TestCase(new string[0], new string[0], false, "Переносить было нечего: все файлы уже на месте.")]
    [TestCase(new[] { "settings/settings.json" }, new string[0], false, "Данные перенесены.")]
    [TestCase(new string[0], new string[0], true, "Данные остались на месте и подготовлены к работе.")]
    [TestCase(new[] { "settings/settings.json" }, new[] { "accounts.json" }, false, "Данные перенесены не полностью.")]
    [TestCase(new string[0], new[] { "accounts.json" }, false, "Перенести данные не удалось.")]
    public void The_headline_names_what_actually_happened(string[] copied, string[] failed, bool inPlace, string expected)
    {
        var result = Build(copied, inPlace: inPlace, failures: [.. failed.Select(name => new LegacyImportFailure(name, "io"))]);

        Assert.That(LegacyImportText.BuildHeadline(result), Is.EqualTo(expected));
    }

    [TestCase(new[] { "users_statistics.json" }, "Перенесённые данные вступят в силу после перезапуска приложения. До перезапуска статистика не сохраняется – чтобы не затереть перенесённое.")]
    [TestCase(new[] { "youtube/users_statistics.json" }, "Перенесённые данные вступят в силу после перезапуска приложения. До перезапуска статистика не сохраняется – чтобы не затереть перенесённое.")]
    [TestCase(new[] { "settings/settings.json", "bot_statistics.json" }, "Перенесённые данные вступят в силу после перезапуска приложения. До перезапуска настройки и статистика не сохраняются – чтобы не затереть перенесённое.")]
    [TestCase(new[] { "stream_sessions.json" }, "Перенесённые данные вступят в силу после перезапуска приложения. До перезапуска история стримов не сохраняется – чтобы не затереть перенесённое.")]
    [TestCase(new[] { "users_statistics.json", "stream_sessions.json" }, "Перенесённые данные вступят в силу после перезапуска приложения. До перезапуска статистика и история стримов не сохраняются – чтобы не затереть перенесённое.")]
    [TestCase(new[] { "polls-history.json" }, "Перенесённые данные вступят в силу после перезапуска приложения. До перезапуска история голосований не сохраняется – чтобы не затереть перенесённое.")]
    [TestCase(new[] { "users_statistics.json", "stream_sessions.json", "polls-history.json" }, "Перенесённые данные вступят в силу после перезапуска приложения. До перезапуска статистика, история стримов и история голосований не сохраняются – чтобы не затереть перенесённое.")]
    [TestCase(new[] { "settings/polls.json" }, "Перенесённые данные вступят в силу после перезапуска приложения. До перезапуска настройки не сохраняются – чтобы не затереть перенесённое.")]
    [TestCase(new[] { "youtube/quota.json" }, "Перенесённые данные вступят в силу после перезапуска приложения. До перезапуска расход квоты YouTube не сохраняется – чтобы не затереть перенесённое.")]
    [TestCase(new[] { "chat-history/0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef.json" }, "Перенесённые данные вступят в силу после перезапуска приложения. До перезапуска кэш истории чата не сохраняется – чтобы не затереть перенесённое.")]
    [TestCase(new[] { "settings/ui-preferences.toml" }, "Перенесённые данные вступят в силу после перезапуска приложения.")]
    public void The_restart_line_promises_a_restart_and_warns_about_every_file_whose_writing_stopped(string[] copied, string expected)
    {
        Assert.That(LegacyImportText.BuildRestartNotice(Build(copied), isSettingsEntry: true), Is.EqualTo(expected));
    }

    [TestCase(false, false)]
    [TestCase(true, true)]
    public void The_restart_line_belongs_to_the_settings_entry_with_something_copied(bool isSettingsEntry, bool copiedNothing)
    {
        var result = Build(copiedNothing ? [] : ["settings/settings.json"]);

        Assert.That(LegacyImportText.BuildRestartNotice(result, isSettingsEntry), Is.Null,
            "На старте перезапуск и так впереди, а без переноса сообщать не о чем.");
    }

    [Test]
    public void DescribeFailure_DistinguishesYouTubeUserStatisticsFromTwitch()
    {
        using (Assert.EnterMultipleScope())
        {
            Assert.That(LegacyImportText.DescribeFailure(new("users_statistics.json", "io")),
                Does.StartWith("Статистика зрителей –"));
            Assert.That(LegacyImportText.DescribeFailure(new("youtube\\users_statistics.json", "io")),
                Does.StartWith("Статистика зрителей YouTube –"));
            Assert.That(LegacyImportText.DescribeFailure(new("youtube.json", "io")),
                Does.StartWith("Настройки YouTube –"));
            Assert.That(LegacyImportText.DescribeFailure(new("youtube-accounts.json", "io")),
                Does.StartWith("Данные входа в YouTube –"));
        }
    }

    [Test]
    public void Every_LegacyDataCatalog_Entry_Has_Explicit_Title_In_LegacyImportText()
    {
        Assert.That(LegacyDataCatalog.AllEntries, Is.Not.Empty);

        using (Assert.EnterMultipleScope())
        {
            foreach (var entry in LegacyDataCatalog.AllEntries)
            {
                var line = LegacyImportText.DescribeFailure(new(entry.RelativeTargetPath, "io"));
                Assert.That(line, Does.Not.StartWith("Данные предыдущей версии"),
                    $"Запись {entry.RelativeTargetPath} отсутствует в LegacyImportText.FileTitles");
            }
        }
    }

    [Test]
    public void DescribeSourceTitle_ShowsFolderNameAndChannelForManuallySelectedFolders()
    {
        var first = new LegacyDataSummary
        {
            SourcePath = @"C:\Backups\MayBackup",
            Kind = LegacyDataSourceKind.SelectedFolder,
            HasMonolithicSettings = false,
            HasSettingsDirectory = true,
            HasOAuthTokens = false,
            Channel = "streamer_one",
            Files = [],
        };
        var second = new LegacyDataSummary
        {
            SourcePath = @"C:\Backups\JuneBackup\",
            Kind = LegacyDataSourceKind.SelectedFolder,
            HasMonolithicSettings = false,
            HasSettingsDirectory = true,
            HasOAuthTokens = false,
            Files = [],
        };
        var working = new LegacyDataSummary
        {
            SourcePath = @"C:\App",
            Kind = LegacyDataSourceKind.CurrentBaseDirectory,
            HasMonolithicSettings = true,
            HasSettingsDirectory = false,
            HasOAuthTokens = false,
            Files = [],
        };

        using (Assert.EnterMultipleScope())
        {
            Assert.That(LegacyImportText.DescribeSourceTitle(first), Is.EqualTo("MayBackup (@streamer_one)"));
            Assert.That(LegacyImportText.DescribeSourceTitle(second), Is.EqualTo("JuneBackup"));
            Assert.That(LegacyImportText.DescribeSourceTitle(first), Does.Not.Contain("Выбранная папка"));
            Assert.That(LegacyImportText.DescribeSourceTitle(working), Is.EqualTo("Данные в рабочей папке"));
        }
    }

    [TestCase(false, true, false, null)]
    [TestCase(false, false, true, "Для работы бота нужно войти в YouTube.")]
    [TestCase(true, true, false, "Для работы бота нужно войти в Twitch.")]
    [TestCase(true, true, true, "Для работы бота нужно войти в Twitch и YouTube.")]
    public void BuildAuthorizationNotice_RespectsEnabledPlatformsAndMissingBotRoles(
        bool twitchEnabled,
        bool twitchBotRequired,
        bool youtubeBotRequired,
        string? expected)
    {
        var result = new LegacyImportResult
        {
            SourcePath = @"C:\old",
            TargetPath = @"C:\new",
            CopiedFiles = ["settings/settings.json"],
            SkippedFiles = [],
            Failures = [],
            BotAuthorizationRequired = true,
            BroadcasterAuthorizationRecommended = true,
        };

        var actual = LegacyImportText.BuildAuthorizationNotice(
            result,
            isSettingsEntry: false,
            twitchEnabled: twitchEnabled,
            twitchBotRequired: twitchBotRequired,
            youtubeBotRequired: youtubeBotRequired);

        if (!twitchEnabled)
        {
            Assert.That(actual, Is.EqualTo(expected));
            if (actual is not null)
            {
                Assert.That(actual, Does.Not.Contain("Twitch"));
            }
        }
        else
        {
            Assert.That(actual, Does.StartWith(expected!));
        }
    }

    private static LegacyImportResult Build(
        IReadOnlyList<string>? copied = null,
        bool inPlace = false,
        IReadOnlyList<LegacyImportFailure>? failures = null,
        IReadOnlyList<string>? unmigrated = null)
    {
        return new()
        {
            SourcePath = @"C:\old",
            TargetPath = @"C:\new",
            CopiedFiles = copied ?? [],
            SkippedFiles = [],
            Failures = failures ?? [],
            IsInPlaceMigration = inPlace,
            MigratedSettingsFiles = inPlace ? ["settings.json"] : [],
            UnmigratedLegacyFiles = unmigrated ?? [],
        };
    }
}
