using PoproshaykaBot.Core.Platforms;
using PoproshaykaBot.Core.Settings.Migrations.LegacyImport;
using PoproshaykaBot.Core.Statistics;

namespace PoproshaykaBot.Core.Tests.Statistics;

[TestFixture]
public sealed class YouTubeStatisticsOwnershipTests
{
    private const string LegacyTwitchJson = """
                                            [
                                              {
                                                "userId": "u1",
                                                "name": "Alice",
                                                "messageCount": 12,
                                                "firstSeen": "2024-01-01T00:00:00Z",
                                                "lastSeen": "2024-01-02T00:00:00Z"
                                              }
                                            ]
                                            """;

    private const string InitialBotJson = """{"messagesProcessed":7,"totalMessagesProcessed":7,"startTime":"2026-01-01T00:00:00Z"}""";

    private string _directory = null!;

    [SetUp]
    public void SetUp()
    {
        _directory = Path.Combine(Path.GetTempPath(), "poproshayka-yt-ownership-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_directory);
    }

    [TearDown]
    public void TearDown()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }

    [Test]
    public async Task MigrationWritesUnifiedCatalog_AndLeavesLegacyPlatformFilesUntouched()
    {
        var twitchPath = Path.Combine(_directory, "users_statistics.json");
        var youtubeDir = Path.Combine(_directory, "youtube");
        var youtubePath = Path.Combine(youtubeDir, "users_statistics.json");
        Directory.CreateDirectory(youtubeDir);
        await File.WriteAllTextAsync(twitchPath, LegacyTwitchJson);
        await File.WriteAllTextAsync(youtubePath, "[]");

        var graph = new StatisticsTestGraph(_directory);
        await graph.AutoSaver.StartAsync(new Progress<string>(), CancellationToken.None);

        try
        {
            graph.UserRepository.TrackMessage(new PlatformUserKey(StreamingPlatform.Twitch, "u1"), "Alice");
            graph.UserRepository.TrackMessage(new PlatformUserKey(StreamingPlatform.YouTube, "yt1"), "BobYT");

            await graph.AutoSaver.SaveNowAsync();

            var savedCatalog = await graph.FileStore.LoadUserStatisticsCatalogAsync();

            using (Assert.EnterMultipleScope())
            {
                Assert.That(await File.ReadAllTextAsync(twitchPath), Is.EqualTo(LegacyTwitchJson));
                Assert.That(await File.ReadAllTextAsync(youtubePath), Is.EqualTo("[]"));
                Assert.That(savedCatalog.Value, Is.Not.Null);
                Assert.That(graph.UserRepository.ForPlatform(StreamingPlatform.Twitch).GetById("u1")!.MessageCount, Is.EqualTo(13ul));
                Assert.That(graph.UserRepository.ForPlatform(StreamingPlatform.YouTube).GetById("yt1")!.Platform, Is.EqualTo(StreamingPlatform.YouTube));
            }
        }
        finally
        {
            await graph.AutoSaver.DisposeAsync();
        }
    }

    [Test]
    public async Task CorruptYouTubeFile_AbortsWholeMigration_CreatesInvalidBackupAndDoesNotCreateCatalog()
    {
        var twitchPath = Path.Combine(_directory, "users_statistics.json");
        var youtubeDir = Path.Combine(_directory, "youtube");
        var youtubePath = Path.Combine(youtubeDir, "users_statistics.json");
        Directory.CreateDirectory(youtubeDir);

        await File.WriteAllTextAsync(twitchPath, LegacyTwitchJson);
        await File.WriteAllTextAsync(youtubePath, "{broken");

        var graph = new StatisticsTestGraph(_directory);
        await graph.AutoSaver.StartAsync(new Progress<string>(), CancellationToken.None);

        try
        {
            graph.UserRepository.TrackMessage(new PlatformUserKey(StreamingPlatform.Twitch, "u1"), "Alice");
            graph.UserRepository.TrackMessage(new PlatformUserKey(StreamingPlatform.YouTube, "yt2"), "CarolYT");

            await graph.AutoSaver.SaveNowAsync();

            using (Assert.EnterMultipleScope())
            {
                Assert.That(graph.UserLoader.IsLoaded, Is.False);
                Assert.That(File.Exists(Path.Combine(_directory, LegacyDataCatalog.UserStatisticsCatalogFileName)), Is.False);
                Assert.That(await File.ReadAllTextAsync(youtubePath), Is.EqualTo("{broken"));
                Assert.That(await File.ReadAllTextAsync(twitchPath), Is.EqualTo(LegacyTwitchJson));
                Assert.That(Directory.GetFiles(youtubeDir, "users_statistics.invalid-*.json"), Is.Not.Empty);
            }
        }
        finally
        {
            await graph.AutoSaver.DisposeAsync();
        }
    }

    [Test]
    public async Task FailedCatalogSave_RollsBackHasChanges_AndRetriesOnNextSave()
    {
        var blockedCatalogTmp = Path.Combine(_directory, $"{LegacyDataCatalog.UserStatisticsCatalogFileName}.tmp");
        Directory.CreateDirectory(blockedCatalogTmp);

        var graph = new StatisticsTestGraph(_directory);
        await graph.AutoSaver.StartAsync(new Progress<string>(), CancellationToken.None);

        try
        {
            graph.UserRepository.TrackMessage(new PlatformUserKey(StreamingPlatform.Twitch, "u1"), "Alice");
            graph.UserRepository.TrackMessage(new PlatformUserKey(StreamingPlatform.YouTube, "yt1"), "BobYT");

            Assert.ThrowsAsync<InvalidOperationException>(() => graph.AutoSaver.SaveNowAsync());

            using (Assert.EnterMultipleScope())
            {
                Assert.That(graph.UserRepository.HasChanges, Is.True);
                Assert.That(File.Exists(Path.Combine(_directory, LegacyDataCatalog.UserStatisticsCatalogFileName)), Is.False);
            }

            Directory.Delete(blockedCatalogTmp);
            await graph.AutoSaver.SaveNowAsync();

            var catalog = await graph.FileStore.LoadUserStatisticsCatalogAsync();
            using (Assert.EnterMultipleScope())
            {
                Assert.That(graph.UserRepository.HasChanges, Is.False);
                Assert.That(catalog.Value!.Accounts.Select(static a => a.Name), Is.EquivalentTo(new[] { "Alice", "BobYT" }));
            }
        }
        finally
        {
            await graph.AutoSaver.DisposeAsync();
        }
    }

    [TestCase("youtube/users_statistics.json", false, true)]
    [TestCase("users_statistics.json", false, true)]
    [TestCase("user_statistics_catalog.json", false, true)]
    [TestCase("bot_statistics.json", true, false)]
    public async Task ExternalWrite_RevokesCatalogParticipantOnAnyOwnedUserPath(
        string touchedRelativePath,
        bool expectCatalogUpdated,
        bool expectBotUpdated)
    {
        var twitchPath = Path.Combine(_directory, "users_statistics.json");
        var youtubeDir = Path.Combine(_directory, "youtube");
        var youtubePath = Path.Combine(youtubeDir, "users_statistics.json");
        var botPath = Path.Combine(_directory, "bot_statistics.json");
        Directory.CreateDirectory(youtubeDir);

        await File.WriteAllTextAsync(twitchPath, LegacyTwitchJson);
        await File.WriteAllTextAsync(youtubePath, "[]");
        await File.WriteAllTextAsync(botPath, InitialBotJson);

        var graph = new StatisticsTestGraph(_directory);
        await graph.AutoSaver.StartAsync(new Progress<string>(), CancellationToken.None);

        try
        {
            await graph.AutoSaver.RunExternalWriteAsync(
                () => 0,
                _ => StatisticsExternalWrite.Files(touchedRelativePath));

            await graph.AutoSaver.RunExternalWriteAsync(() => 0, _ => StatisticsExternalWrite.None);

            graph.UserRepository.TrackMessage(new PlatformUserKey(StreamingPlatform.Twitch, "u1"), "Alice");
            graph.UserRepository.TrackMessage(new PlatformUserKey(StreamingPlatform.YouTube, "yt1"), "BobYT");
            graph.BotRepository.IncrementMessagesProcessed();

            await graph.AutoSaver.SaveNowAsync();

            var savedBot = await graph.FileStore.LoadBotAsync();
            var savedCatalog = await graph.FileStore.LoadUserStatisticsCatalogAsync();

            using (Assert.EnterMultipleScope())
            {
                Assert.That(graph.UserLoader.IsLoaded, Is.EqualTo(expectCatalogUpdated));
                Assert.That(savedBot.Value!.TotalMessagesProcessed > 7ul, Is.EqualTo(expectBotUpdated));
                Assert.That(
                    savedCatalog.Value!.UnattributedCounters.Single(static c => c.UserId == "u1").MessageCount == 13ul,
                    Is.EqualTo(expectCatalogUpdated));
            }
        }
        finally
        {
            await graph.AutoSaver.DisposeAsync();
        }
    }
}
