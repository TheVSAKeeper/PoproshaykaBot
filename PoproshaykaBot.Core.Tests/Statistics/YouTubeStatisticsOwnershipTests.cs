using Microsoft.Extensions.Logging.Abstractions;
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
    public async Task LegacyTwitchJsonOmitsPlatformOnResave_WhileYouTubeWritesPlatformYouTube()
    {
        var twitchPath = Path.Combine(_directory, "users_statistics.json");
        var youtubePath = Path.Combine(_directory, "youtube", "users_statistics.json");
        await File.WriteAllTextAsync(twitchPath, LegacyTwitchJson);

        var graph = new StatisticsTestGraph(_directory);
        await graph.AutoSaver.StartAsync(new Progress<string>(), CancellationToken.None);

        try
        {
            graph.UserRepository.TrackMessage(new PlatformUserKey(StreamingPlatform.Twitch, "u1"), "Alice");
            graph.UserRepository.TrackMessage(new PlatformUserKey(StreamingPlatform.YouTube, "yt1"), "BobYT");

            await graph.AutoSaver.SaveNowAsync();

            var savedTwitchJson = await File.ReadAllTextAsync(twitchPath);
            var savedYouTubeJson = await File.ReadAllTextAsync(youtubePath);

            using (Assert.EnterMultipleScope())
            {
                Assert.That(savedTwitchJson, Does.Not.Contain("\"platform\""));
                Assert.That(savedYouTubeJson, Does.Contain("\"platform\": \"YouTube\""));
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
    public async Task CorruptYouTubeFile_AbandonsYouTubeOnly_LeavesTwitchOperationalAndCreatesBackupInYouTubeFolder()
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
                Assert.That(graph.UserLoader.IsLoadedFor(StreamingPlatform.Twitch), Is.True);
                Assert.That(graph.UserLoader.IsLoadedFor(StreamingPlatform.YouTube), Is.False);
                Assert.That(await File.ReadAllTextAsync(youtubePath), Is.EqualTo("{broken"));
                Assert.That(Directory.GetFiles(youtubeDir, "users_statistics.invalid-*.json"), Is.Not.Empty);
                Assert.That((await graph.FileStore.LoadUsersAsync(StreamingPlatform.Twitch)).Value!.Single().MessageCount, Is.EqualTo(13ul));
            }
        }
        finally
        {
            await graph.AutoSaver.DisposeAsync();
        }
    }

    [Test]
    public async Task PartialSaveFailure_RollsBackOnlyUncommittedUnits_LeavingCommittedPlatformClean()
    {
        var youtubeDir = Path.Combine(_directory, "youtube");
        var blockedYouTubeTmp = Path.Combine(youtubeDir, "users_statistics.json.tmp");
        Directory.CreateDirectory(blockedYouTubeTmp);

        var graph = new StatisticsTestGraph(_directory);
        await graph.AutoSaver.StartAsync(new Progress<string>(), CancellationToken.None);

        try
        {
            graph.UserRepository.TrackMessage(new PlatformUserKey(StreamingPlatform.Twitch, "u1"), "Alice");
            graph.UserRepository.TrackMessage(new PlatformUserKey(StreamingPlatform.YouTube, "yt1"), "BobYT");

            Assert.ThrowsAsync<InvalidOperationException>(() => graph.AutoSaver.SaveNowAsync());

            using (Assert.EnterMultipleScope())
            {
                Assert.That(graph.UserRepository.GetPartition(StreamingPlatform.Twitch).HasChanges, Is.False);
                Assert.That(graph.UserRepository.GetPartition(StreamingPlatform.YouTube).HasChanges, Is.True);
                Assert.That(File.Exists(Path.Combine(_directory, "users_statistics.json")), Is.True);
                Assert.That(File.Exists(Path.Combine(youtubeDir, "users_statistics.json")), Is.False);
            }

            Directory.Delete(blockedYouTubeTmp);
            await graph.AutoSaver.SaveNowAsync();

            using (Assert.EnterMultipleScope())
            {
                Assert.That(graph.UserRepository.GetPartition(StreamingPlatform.YouTube).HasChanges, Is.False);
                Assert.That((await graph.FileStore.LoadUsersAsync(StreamingPlatform.YouTube)).Value!.Single().Name, Is.EqualTo("BobYT"));
            }
        }
        finally
        {
            await graph.AutoSaver.DisposeAsync();
        }
    }

    [TestCase("youtube/users_statistics.json", true, false, true)]
    [TestCase("users_statistics.json", false, true, true)]
    [TestCase("bot_statistics.json", true, true, false)]
    public async Task ExternalWrite_RevokesOnlyTouchedFileParticipant(
        string touchedRelativePath,
        bool expectTwitchLoaded,
        bool expectYouTubeLoaded,
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
            await graph.AutoSaver.RunExternalWriteAsync(() => 0, _ => StatisticsExternalWrite.None);

            graph.UserRepository.TrackMessage(new PlatformUserKey(StreamingPlatform.Twitch, "u1"), "Alice");
            graph.UserRepository.TrackMessage(new PlatformUserKey(StreamingPlatform.YouTube, "yt1"), "BobYT");
            graph.BotRepository.IncrementMessagesProcessed();

            await graph.AutoSaver.SaveNowAsync();

            var savedBot = await graph.FileStore.LoadBotAsync();

            using (Assert.EnterMultipleScope())
            {
                Assert.That(graph.UserLoader.IsLoadedFor(StreamingPlatform.Twitch), Is.EqualTo(expectTwitchLoaded));
                Assert.That(graph.UserLoader.IsLoadedFor(StreamingPlatform.YouTube), Is.EqualTo(expectYouTubeLoaded));
                Assert.That(savedBot.Value!.TotalMessagesProcessed > 7ul, Is.EqualTo(expectBotUpdated));
                Assert.That(
                    (await graph.FileStore.LoadUsersAsync(StreamingPlatform.Twitch)).Value!.Single().MessageCount == 13ul,
                    Is.EqualTo(expectTwitchLoaded));
                Assert.That(
                    (await graph.FileStore.LoadUsersAsync(StreamingPlatform.YouTube)).Value!.Count == 1,
                    Is.EqualTo(expectYouTubeLoaded));
            }
        }
        finally
        {
            await graph.AutoSaver.DisposeAsync();
        }
    }
}
