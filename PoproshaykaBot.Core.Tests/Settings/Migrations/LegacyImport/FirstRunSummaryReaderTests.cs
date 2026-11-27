using PoproshaykaBot.Core.Settings.Migrations.LegacyImport;

namespace PoproshaykaBot.Core.Tests.Settings.Migrations.LegacyImport;

[TestFixture]
public sealed class FirstRunSummaryReaderTests
{
    private string _targetDir = null!;

    [SetUp]
    public void SetUp()
    {
        _targetDir = LegacyDataFixture.CreateDirectory("poproshayka-first-run-reader");
    }

    [TearDown]
    public void TearDown()
    {
        LegacyDataFixture.Delete(_targetDir);
    }

    [Test]
    public void Read_ExtractsAllSixDomainBlocksAndTwitchFlags()
    {
        var settingsDir = Path.Combine(_targetDir, "settings");
        Directory.CreateDirectory(settingsDir);

        File.WriteAllText(Path.Combine(settingsDir, "broadcast-profiles.json"), """{"profiles":[{"name":"A"},{"name":"B"}]}""");
        File.WriteAllText(Path.Combine(settingsDir, "obs-chat.json"), """{"fontSize":16}""");
        File.WriteAllText(Path.Combine(settingsDir, "obs-integration.json"), """{"enabled":true,"host":"127.0.0.1","port":4455,"dashboardMicrophoneName":"Mic USB","dashboardSourceNames":["Mic USB","Browser"]}""");
        File.WriteAllText(Path.Combine(settingsDir, "settings.json"), """{"twitch":{"enabled":true,"httpServerPort":8095,"autoBroadcast":{"autoBroadcastEnabled":true,"broadcastIntervalMinutes":20}},"ranks":{"ranks":[{"name":"Новичок","minMessages":0}]}}""");
        File.WriteAllText(Path.Combine(settingsDir, "commands.json"), """{"commands":{"!hi":{"enabled":true}}}""");
        File.WriteAllText(Path.Combine(_targetDir, "users_statistics.json"), """{"1":{"name":"u1"},"2":{"name":"u2"},"3":{"name":"u3"}}""");
        File.WriteAllText(Path.Combine(_targetDir, "stream_sessions.json"), """[{"channel":"ch"},{"channel":"ch"}]""");

        var result = new LegacyImportResult
        {
            SourcePath = @"C:\old",
            TargetPath = _targetDir,
            CopiedFiles =
            [
                "settings/broadcast-profiles.json",
                "settings/obs-chat.json",
                "settings/obs-integration.json",
                "settings/settings.json",
                "settings/commands.json",
                "users_statistics.json",
                "stream_sessions.json",
            ],
            SkippedFiles = [],
            Failures = [],
        };

        var snapshot = FirstRunSummaryReader.Read(result);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(snapshot.BroadcastProfilesCount, Is.EqualTo(2));
            Assert.That(snapshot.HasObsChat, Is.True);
            Assert.That(snapshot.HttpServerPort, Is.EqualTo(8095));
            Assert.That(snapshot.HasObsIntegration, Is.True);
            Assert.That(snapshot.ObsEnabled, Is.True);
            Assert.That(snapshot.ObsHost, Is.EqualTo("127.0.0.1"));
            Assert.That(snapshot.ObsPort, Is.EqualTo(4455));
            Assert.That(snapshot.ObsDashboardMicrophoneName, Is.EqualTo("Mic USB"));
            Assert.That(snapshot.ObsAudioSourcesCount, Is.EqualTo(2));
            Assert.That(snapshot.HasAutoBroadcast, Is.True);
            Assert.That(snapshot.AutoBroadcastEnabled, Is.True);
            Assert.That(snapshot.AutoBroadcastIntervalMinutes, Is.EqualTo(20));
            Assert.That(snapshot.HasAppSettings, Is.True);
            Assert.That(snapshot.RanksCount, Is.EqualTo(1));
            Assert.That(snapshot.CommandsCount, Is.EqualTo(1));
            Assert.That(snapshot.ViewersCount, Is.EqualTo(3));
            Assert.That(snapshot.SessionsCount, Is.EqualTo(2));
            Assert.That(snapshot.HasAnyStatsFile, Is.True);
            Assert.That(snapshot.TwitchEnabled, Is.True);
            Assert.That(snapshot.TwitchBotAuthMissing, Is.True);
        }
    }

    [Test]
    public void Read_YouTubeUserStatisticsOnly_SetsHasAnyStatsFileAndViewersCount()
    {
        var youtubeDir = Path.Combine(_targetDir, "youtube");
        Directory.CreateDirectory(youtubeDir);
        File.WriteAllText(Path.Combine(youtubeDir, "users_statistics.json"), """[{"userId":"yt1","name":"Bob"},{"userId":"yt2","name":"Eve"}]""");

        var result = new LegacyImportResult
        {
            SourcePath = @"C:\old",
            TargetPath = _targetDir,
            CopiedFiles = ["youtube/users_statistics.json"],
            SkippedFiles = [],
            Failures = [],
        };

        var snapshot = FirstRunSummaryReader.Read(result);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(snapshot.ViewersCount, Is.EqualTo(2));
            Assert.That(snapshot.SessionsCount, Is.Zero);
            Assert.That(snapshot.TotalMessagesProcessed, Is.Zero);
            Assert.That(snapshot.HasAnyStatsFile, Is.True);
        }
    }
}
