using PoproshaykaBot.Core.Settings.Migrations.LegacyImport;
using PoproshaykaBot.Wpf.ViewModels.Migration;
using System.IO;

namespace PoproshaykaBot.Wpf.Tests.Migration;

[TestFixture]
public class LegacyImportTextTests
{
    [Test]
    public void A_file_left_behind_keeps_unmigrated_notice_in_human_readable_form()
    {
        var result = Build(inPlace: true, unmigrated: ["accounts.json"]);

        Assert.Multiple(() =>
        {
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

    [Test]
    public void DiagnoseInvalidFolder_ExplainsWhatIsMissingWithoutTechnicalStackTraces()
    {
        var emptyDir = Path.Combine(Path.GetTempPath(), "pb-invalid-empty-" + Guid.NewGuid().ToString("N"));
        var unrelatedDir = Path.Combine(Path.GetTempPath(), "pb-invalid-other-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(emptyDir);
        Directory.CreateDirectory(unrelatedDir);
        try
        {
            File.WriteAllText(Path.Combine(unrelatedDir, "readme.txt"), "hello");

            var missingMessage = LegacyImportText.DiagnoseInvalidFolder(Path.Combine(emptyDir, "missing"));
            var emptyMessage = LegacyImportText.DiagnoseInvalidFolder(emptyDir);
            var unrelatedMessage = LegacyImportText.DiagnoseInvalidFolder(unrelatedDir);

            using (Assert.EnterMultipleScope())
            {
                Assert.That(missingMessage, Does.Contain("не найдена"));
                Assert.That(emptyMessage, Does.Contain("пуста").And.Contain("settings.json"));
                Assert.That(unrelatedMessage, Does.Contain("settings.json").And.Contain("users_statistics.json"));
                Assert.That(unrelatedMessage, Does.Not.Contain("Exception").And.Not.Contain("StackTrace"));
            }
        }
        finally
        {
            Directory.Delete(emptyDir, recursive: true);
            Directory.Delete(unrelatedDir, recursive: true);
        }
    }

    [Test]
    public void BuildFirstRunSummary_ComputesSemanticBlocksFromImportedFiles_ForFullAndPartialImports()
    {
        var targetDir = Path.Combine(Path.GetTempPath(), "pb-summary-" + Guid.NewGuid().ToString("N"));
        var settingsDir = Path.Combine(targetDir, "settings");
        Directory.CreateDirectory(settingsDir);
        try
        {
            File.WriteAllText(Path.Combine(settingsDir, "broadcast-profiles.json"), """{"profiles":[{"name":"A"},{"name":"B"}]}""");
            File.WriteAllText(Path.Combine(settingsDir, "obs-chat.json"), """{"fontSize":16}""");
            File.WriteAllText(Path.Combine(settingsDir, "obs-integration.json"), """{"enabled":true,"host":"127.0.0.1","port":4455,"dashboardMicrophoneName":"Mic USB","dashboardSourceNames":["Mic USB","Browser"]}""");
            File.WriteAllText(Path.Combine(settingsDir, "settings.json"), """{"twitch":{"httpServerPort":8095,"chatDisplayAccount":"Bot","autoBroadcast":{"autoBroadcastEnabled":true,"broadcastIntervalMinutes":20}},"ranks":{"ranks":[{"name":"Новичок","minMessages":0}]}}""");
            File.WriteAllText(Path.Combine(settingsDir, "commands.json"), """{"commands":{"!hi":{"enabled":true}}}""");
            File.WriteAllText(Path.Combine(targetDir, "users_statistics.json"), """{"1":{"name":"u1"},"2":{"name":"u2"},"3":{"name":"u3"}}""");
            File.WriteAllText(Path.Combine(targetDir, "stream_sessions.json"), """[{"channel":"ch"},{"channel":"ch"}]""");

            var fullResult = new LegacyImportResult
            {
                SourcePath = @"C:\old",
                TargetPath = targetDir,
                CopiedFiles = ["settings/broadcast-profiles.json", "settings/obs-chat.json", "settings/obs-integration.json", "settings/settings.json", "settings/commands.json", "users_statistics.json", "stream_sessions.json"],
                SkippedFiles = [],
                Failures = [],
            };

            var fullSummary = LegacyImportText.BuildFirstRunSummary(fullResult, twitchEnabled: true, twitchBotRequired: false);

            using (Assert.EnterMultipleScope())
            {
                Assert.That(fullSummary.IsPartial, Is.False);
                Assert.That(fullSummary.TransferredItems.Select(static i => i.Title), Is.EqualTo(new[]
                {
                    "Профили эфира",
                    "Оверлей и локальный порт",
                    "OBS и аудиоисточники",
                    "Авто-рассылка",
                    "Фразы и ранги",
                    "Статистика зрителей и сессий",
                }));
                Assert.That(fullSummary.TransferredItems[0].Detail, Does.Contain("2 профиля"));
                Assert.That(fullSummary.TransferredItems[1].Detail, Does.Contain("8095"));
                Assert.That(fullSummary.TransferredItems[2].Detail, Does.Contain("Mic USB").And.Contain("2 аудиоисточника"));
                Assert.That(fullSummary.TransferredItems[3].Detail, Does.Contain("20 мин"));
                Assert.That(fullSummary.TransferredItems[5].Detail, Does.Contain("3 зрителя").And.Contain("2 эфира"));
                Assert.That(fullSummary.AttentionItems, Is.Empty);
            }

            var partialResult = new LegacyImportResult
            {
                SourcePath = @"C:\old",
                TargetPath = targetDir,
                CopiedFiles = ["settings/settings.json"],
                SkippedFiles = [],
                Failures = [new("settings/obs-integration.json", "locked")],
                BotAuthorizationRequired = true,
            };

            var partialSummary = LegacyImportText.BuildFirstRunSummary(partialResult, twitchEnabled: true, twitchBotRequired: true);

            using (Assert.EnterMultipleScope())
            {
                Assert.That(partialSummary.IsPartial, Is.True);
                Assert.That(partialSummary.Headline, Does.Contain("частично"));
                Assert.That(partialSummary.TransferredItems.Select(static i => i.Title), Is.EqualTo(new[]
                {
                    "Оверлей и локальный порт",
                    "Авто-рассылка",
                    "Фразы и ранги",
                }));
                Assert.That(partialSummary.TransferredItems[0].Detail, Is.EqualTo("Локальный порт веб-сервера 8095"));
                Assert.That(partialSummary.TransferredItems[2].Detail, Does.Not.Contain("команд"));
                Assert.That(partialSummary.AttentionItems.Any(static i => i.Title == "Профили эфира" && !i.IsWarning), Is.True);
                Assert.That(partialSummary.AttentionItems.Any(static i => i.Title == "OBS и аудиоисточники" && i.IsWarning), Is.True);
                Assert.That(partialSummary.AttentionItems.Any(static i => i.Title == "Статистика зрителей и сессий" && !i.IsWarning), Is.True);
                Assert.That(partialSummary.AttentionItems.Any(static i => i.Title == "Вход в Twitch" && i.IsWarning), Is.True);
            }
        }
        finally
        {
            Directory.Delete(targetDir, recursive: true);
        }
    }

    [Test]
    public void BuildFirstRunSummary_SplitsOverlayAndPortBlock_WhenOneFileFailsAndOtherSucceeds()
    {
        var targetDir = Path.Combine(Path.GetTempPath(), "pb-summary-split-" + Guid.NewGuid().ToString("N"));
        var settingsDir = Path.Combine(targetDir, "settings");
        Directory.CreateDirectory(settingsDir);
        try
        {
            File.WriteAllText(Path.Combine(settingsDir, "settings.json"), """{"twitch":{"httpServerPort":8095}}""");
            File.WriteAllText(Path.Combine(settingsDir, "obs-chat.json"), """{"fontSize":16}""");

            var obsChatFailedResult = new LegacyImportResult
            {
                SourcePath = @"C:\old",
                TargetPath = targetDir,
                CopiedFiles = ["settings/settings.json"],
                SkippedFiles = [],
                Failures = [new("settings/obs-chat.json", "locked")],
            };

            var obsChatFailedSummary = LegacyImportText.BuildFirstRunSummary(obsChatFailedResult, twitchEnabled: true, twitchBotRequired: false);

            var settingsFailedResult = new LegacyImportResult
            {
                SourcePath = @"C:\old",
                TargetPath = targetDir,
                CopiedFiles = ["settings/obs-chat.json"],
                SkippedFiles = [],
                Failures = [new("settings/settings.json", "locked")],
            };

            var settingsFailedSummary = LegacyImportText.BuildFirstRunSummary(settingsFailedResult, twitchEnabled: true, twitchBotRequired: false);

            using (Assert.EnterMultipleScope())
            {
                Assert.That(obsChatFailedSummary.TransferredItems.Single(static i => i.Title == "Оверлей и локальный порт").Detail,
                    Is.EqualTo("Локальный порт веб-сервера 8095"));
                Assert.That(obsChatFailedSummary.AttentionItems.Any(static i => i.Title == "Оформление чата OBS" && i.IsWarning), Is.True);

                Assert.That(settingsFailedSummary.TransferredItems.Single(static i => i.Title == "Оверлей и локальный порт").Detail,
                    Is.EqualTo("Оформление чата для OBS"));
                Assert.That(settingsFailedSummary.AttentionItems.Any(static i => i.Title == "Локальный порт веб-сервера" && i.IsWarning), Is.True);
            }
        }
        finally
        {
            Directory.Delete(targetDir, recursive: true);
        }
    }
}
