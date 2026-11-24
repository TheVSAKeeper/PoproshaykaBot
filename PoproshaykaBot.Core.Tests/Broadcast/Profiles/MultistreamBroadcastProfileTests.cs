using PoproshaykaBot.Core.Broadcast.Profiles;
using PoproshaykaBot.Core.Infrastructure.Events;
using PoproshaykaBot.Core.Infrastructure.Events.Broadcasting;
using PoproshaykaBot.Core.Platforms;
using PoproshaykaBot.Core.Platforms.Auth;
using PoproshaykaBot.Core.Settings;
using PoproshaykaBot.Core.Settings.Stores;
using PoproshaykaBot.Core.Tests.Polls;
using PoproshaykaBot.Core.YouTube.Auth;
using PoproshaykaBot.Core.YouTube.Streaming;

namespace PoproshaykaBot.Core.Tests.Broadcast.Profiles;

[TestFixture]
public sealed class MultistreamBroadcastProfileTests
{
    private string _tempDir = null!;
    private BroadcastProfilesStore _profilesStore = null!;
    private BroadcastProfilePlatformsStore _platformsStore = null!;
    private BroadcastProfileYouTubeStore _youtubeStore = null!;
    private ChannelCatalogStore _catalogStore = null!;
    private YouTubeAccountsStore _youtubeAccountsStore = null!;
    private IChannelInformationApplier _twitchApplier = null!;
    private IYouTubeBroadcastProfileApplier _youtubeApplier = null!;
    private IEventBus _eventBus = null!;
    private TestTimeProvider _clock = null!;

    [SetUp]
    public void SetUp()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "multistream-profiles-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);

        _profilesStore = new(filePath: Path.Combine(_tempDir, "broadcast-profiles.json"));
        _platformsStore = new(filePath: Path.Combine(_tempDir, "broadcast-profile-platforms.json"));
        _youtubeStore = new(filePath: Path.Combine(_tempDir, "broadcast-profile-youtube.json"));
        _catalogStore = new(filePath: Path.Combine(_tempDir, "channels.json"));
        _youtubeAccountsStore = new(filePath: Path.Combine(_tempDir, "youtube-accounts.json"));

        _twitchApplier = Substitute.For<IChannelInformationApplier>();
        _twitchApplier.ApplyAsync(Arg.Any<BroadcastProfile>(), Arg.Any<CancellationToken>()).Returns(true);

        _youtubeApplier = Substitute.For<IYouTubeBroadcastProfileApplier>();
        _youtubeApplier.ApplyAsync(
                Arg.Any<BroadcastProfile>(),
                Arg.Any<BroadcastProfileYouTubeMetadata?>(),
                Arg.Any<CancellationToken>())
            .Returns(new BroadcastProfilePlatformApplyResult(StreamingPlatform.YouTube, true));

        _eventBus = Substitute.For<IEventBus>();
        _clock = new() { UtcNow = new(2026, 11, 20, 18, 0, 0, TimeSpan.Zero) };
    }

    [TearDown]
    public void TearDown()
    {
        if (Directory.Exists(_tempDir))
        {
            Directory.Delete(_tempDir, recursive: true);
        }
    }

    [Test]
    public async Task BroadcastProfileYouTubeStore_RoundTrip_EmptyRemoval_And_SettingsWriteGate()
    {
        var path = Path.Combine(_tempDir, "yt-meta.json");
        var gate = new SettingsWriteGate();
        var store = new BroadcastProfileYouTubeStore(null, path, gate);
        var profileId = Guid.NewGuid();

        var saved = store.SetMetadata(profileId, new BroadcastProfileYouTubeMetadata
        {
            TitleOverride = "  Стрим YT #{n}  ",
            Description = "  Описание серии {n}  ",
            PrivacyStatus = "UNLISTED",
        });

        var reloaded = new BroadcastProfileYouTubeStore(null, path).GetMetadata(profileId);

        Assert.Multiple(() =>
        {
            Assert.That(saved, Is.True);
            Assert.That(reloaded, Is.Not.Null);
            Assert.That(reloaded!.TitleOverride, Is.EqualTo("Стрим YT #{n}"));
            Assert.That(reloaded.Description, Is.EqualTo("Описание серии {n}"));
            Assert.That(reloaded.PrivacyStatus, Is.EqualTo(YouTubeBroadcastPrivacyStatus.Unlisted));
        });

        store.SetMetadata(profileId, new BroadcastProfileYouTubeMetadata
        {
            TitleOverride = "   ",
            Description = "",
            PrivacyStatus = YouTubeBroadcastPrivacyStatus.Unchanged,
        });

        var afterClear = new BroadcastProfileYouTubeStore(null, path);
        Assert.Multiple(() =>
        {
            Assert.That(afterClear.GetMetadata(profileId), Is.Null);
            Assert.That(afterClear.Load().Profiles, Does.Not.ContainKey(profileId));
        });

        await gate.RunExternalWriteAsync(
            () =>
            {
                File.WriteAllText(path, """{"profiles":{}}""");
                return Task.FromResult(0);
            },
            static _ => ["yt-meta.json"]);

        var blockedWrite = store.SetMetadata(profileId, new BroadcastProfileYouTubeMetadata { TitleOverride = "В памяти" });

        Assert.Multiple(() =>
        {
            Assert.That(blockedWrite, Is.False);
            Assert.That(store.GetMetadata(profileId)?.TitleOverride, Is.EqualTo("В памяти"));
            Assert.That(new BroadcastProfileYouTubeStore(null, path).GetMetadata(profileId), Is.Null);
        });
    }

    [Test]
    public async Task YouTubeBroadcastProfileApplier_Renders_Placeholders_And_Updates_Broadcast()
    {
        SeedActiveYouTubeChannel("UC_MAIN", "yt-ctx");
        SeedYouTubeBroadcaster("yt-ctx", "access-1", [YouTubeScopes.ForceSsl, YouTubeScopes.ReadOnly]);

        var scheduledStart = new DateTimeOffset(2026, 11, 20, 17, 0, 0, TimeSpan.Zero);
        var broadcast = new YouTubeBroadcastSummary(
            "vid-100",
            "UC_MAIN",
            "Main",
            "chat-100",
            "Старое название",
            scheduledStart.AddMinutes(5),
            Description: "Старое описание",
            PrivacyStatus: "public",
            ScheduledStartTimeUtc: scheduledStart);

        var source = Substitute.For<IYouTubeActiveBroadcastSource>();
        source.GetActiveBroadcast(new(StreamingPlatform.YouTube, "UC_MAIN")).Returns(broadcast);

        YouTubeBroadcastUpdateRequest? capturedRequest = null;
        string? capturedContext = null;
        var client = Substitute.For<IYouTubeLiveBroadcastClient>();
        client.UpdateBroadcastMetadataAsync(
                Arg.Do<YouTubeBroadcastUpdateRequest>(r => capturedRequest = r),
                Arg.Do<string?>(c => capturedContext = c),
                Arg.Any<CancellationToken>())
            .Returns(YouTubeBroadcastUpdateResult.Success());

        var applier = new YouTubeBroadcastProfileApplier(
            _catalogStore,
            _youtubeAccountsStore,
            source,
            client,
            NullLogger<YouTubeBroadcastProfileApplier>.Instance);

        var profile = new BroadcastProfile
        {
            Name = "Сюжет",
            Title = "Общий стрим #12",
            CurrentNumber = 12,
        };
        var metadata = new BroadcastProfileYouTubeMetadata
        {
            TitleOverride = "YouTube выпуск #{n}",
            Description = "Прохождение, серия {n}",
            PrivacyStatus = YouTubeBroadcastPrivacyStatus.Unlisted,
        };

        var result = await applier.ApplyAsync(profile, metadata, CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(result.Platform, Is.EqualTo(StreamingPlatform.YouTube));
            Assert.That(result.Succeeded, Is.True);
            Assert.That(result.ErrorMessage, Is.Null);
            Assert.That(capturedContext, Is.EqualTo("yt-ctx"));
            Assert.That(capturedRequest, Is.Not.Null);
            Assert.That(capturedRequest!.BroadcastId, Is.EqualTo("vid-100"));
            Assert.That(capturedRequest.Title, Is.EqualTo("YouTube выпуск #12"));
            Assert.That(capturedRequest.Description, Is.EqualTo("Прохождение, серия 12"));
            Assert.That(capturedRequest.ScheduledStartTimeUtc, Is.EqualTo(scheduledStart));
            Assert.That(capturedRequest.PrivacyStatus, Is.EqualTo(YouTubeBroadcastPrivacyStatus.Unlisted));
        });
    }

    [Test]
    public async Task YouTubeBroadcastProfileApplier_Fetches_ActiveBroadcast_When_ScheduledStartTime_Missing()
    {
        SeedYouTubeBroadcaster(null, "access-default", [YouTubeBroadcastWriteAccess.FullYouTubeScope]);

        var cachedWithoutSchedule = new YouTubeBroadcastSummary(
            "vid-200",
            "UC_DEF",
            "Def",
            "chat-200",
            "Cached",
            _clock.UtcNow);
        var scheduled = new DateTimeOffset(2026, 11, 20, 16, 45, 0, TimeSpan.Zero);
        var fresh = cachedWithoutSchedule with
        {
            Description = "Сохранить описание",
            ScheduledStartTimeUtc = scheduled,
        };

        var source = Substitute.For<IYouTubeActiveBroadcastSource>();
        source.CurrentBroadcast.Returns(cachedWithoutSchedule);

        YouTubeBroadcastUpdateRequest? captured = null;
        var client = Substitute.For<IYouTubeLiveBroadcastClient>();
        client.ListActiveBroadcastsAsync(Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns([fresh]);
        client.UpdateBroadcastMetadataAsync(
                Arg.Do<YouTubeBroadcastUpdateRequest>(r => captured = r),
                Arg.Any<string?>(),
                Arg.Any<CancellationToken>())
            .Returns(YouTubeBroadcastUpdateResult.Success());

        var applier = new YouTubeBroadcastProfileApplier(
            _catalogStore,
            _youtubeAccountsStore,
            source,
            client,
            NullLogger<YouTubeBroadcastProfileApplier>.Instance);

        var profile = new BroadcastProfile { Name = "Общий", Title = "Общее название" };

        var result = await applier.ApplyAsync(profile, youtubeMetadata: null, CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(result.Succeeded, Is.True);
            Assert.That(captured, Is.Not.Null);
            Assert.That(captured!.Title, Is.EqualTo("Общее название"));
            Assert.That(captured.Description, Is.EqualTo("Сохранить описание"));
            Assert.That(captured.ScheduledStartTimeUtc, Is.EqualTo(scheduled));
            Assert.That(captured.PrivacyStatus, Is.Null);
        });
    }

    [Test]
    public async Task YouTubeBroadcastProfileApplier_MissingAuth_ReadOnlyScope_And_NoActiveBroadcast_Return_Explicit_Messages()
    {
        var source = Substitute.For<IYouTubeActiveBroadcastSource>();
        var client = Substitute.For<IYouTubeLiveBroadcastClient>();
        client.ListActiveBroadcastsAsync(Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(Array.Empty<YouTubeBroadcastSummary>());

        var applier = new YouTubeBroadcastProfileApplier(
            _catalogStore,
            _youtubeAccountsStore,
            source,
            client,
            NullLogger<YouTubeBroadcastProfileApplier>.Instance);

        var profile = new BroadcastProfile { Name = "Тест", Title = "Заголовок" };

        var noAuth = await applier.ApplyAsync(profile, null, CancellationToken.None);

        SeedYouTubeBroadcaster(null, "ro-token", [YouTubeScopes.ReadOnly]);
        var readOnly = await applier.ApplyAsync(profile, null, CancellationToken.None);

        SeedYouTubeBroadcaster(null, "rw-token", [YouTubeScopes.ForceSsl]);
        var noBroadcast = await applier.ApplyAsync(profile, null, CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(noAuth.Succeeded, Is.False);
            Assert.That(noAuth.ErrorMessage, Is.EqualTo("Нет авторизации владельца канала YouTube"));

            Assert.That(readOnly.Succeeded, Is.False);
            Assert.That(readOnly.ErrorMessage, Does.Contain("youtube.force-ssl"));

            Assert.That(noBroadcast.Succeeded, Is.False);
            Assert.That(noBroadcast.ErrorMessage, Is.EqualTo("На YouTube нет активной трансляции"));
        });

        await client.DidNotReceiveWithAnyArgs().UpdateBroadcastMetadataAsync(null!, null, CancellationToken.None);
    }

    [Test]
    public async Task YouTubeBroadcastProfileApplier_FallsBack_To_SameChannel_Bot_With_ForceSsl_When_Broadcaster_Is_ReadOnly()
    {
        _catalogStore.Save(new ChannelsSettings
        {
            Channels =
            [
                new ChannelCatalogEntry
                {
                    Platform = StreamingPlatform.YouTube,
                    ChannelId = "UC_MAIN",
                    DisplayName = "Main YT",
                    IsConfigured = true,
                    BotContextId = "yt-bot-ctx",
                    BroadcasterContextId = "yt-broadcaster-ctx",
                },
            ],
            Platforms =
            [
                new PlatformChannelRoutingSettings
                {
                    Platform = StreamingPlatform.YouTube,
                    ActiveChannelId = "UC_MAIN",
                },
            ],
        });

        SeedYouTubeBroadcaster("yt-broadcaster-ctx", "ro-token", [YouTubeScopes.ReadOnly]);
        _youtubeAccountsStore.Mutate(PlatformAuthRole.Bot, "yt-bot-ctx", account =>
        {
            account.AccessToken = "bot-rw-token";
            account.StoredScopes = [YouTubeScopes.ForceSsl];
            account.ChannelId = "UC_MAIN";
            account.ChannelTitle = "Main YT";
        });

        var scheduledStart = new DateTimeOffset(2026, 11, 20, 17, 0, 0, TimeSpan.Zero);
        var broadcast = new YouTubeBroadcastSummary(
            "vid-300",
            "UC_MAIN",
            "Main YT",
            "chat-300",
            "Старое название",
            scheduledStart.AddMinutes(5),
            ScheduledStartTimeUtc: scheduledStart);

        var source = Substitute.For<IYouTubeActiveBroadcastSource>();
        source.GetActiveBroadcast(new(StreamingPlatform.YouTube, "UC_MAIN")).Returns(broadcast);

        PlatformAuthRole? capturedRole = null;
        string? capturedContext = null;
        var client = Substitute.For<IYouTubeLiveBroadcastClient>();
        client.UpdateBroadcastMetadataAsync(
                Arg.Any<YouTubeBroadcastUpdateRequest>(),
                Arg.Do<PlatformAuthRole>(r => capturedRole = r),
                Arg.Do<string?>(c => capturedContext = c),
                Arg.Any<CancellationToken>())
            .Returns(YouTubeBroadcastUpdateResult.Success());

        var applier = new YouTubeBroadcastProfileApplier(
            _catalogStore,
            _youtubeAccountsStore,
            source,
            client,
            NullLogger<YouTubeBroadcastProfileApplier>.Instance);

        var result = await applier.ApplyAsync(new BroadcastProfile { Name = "Профиль", Title = "Новое название" }, null, CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(result.Succeeded, Is.True);
            Assert.That(capturedRole, Is.EqualTo(PlatformAuthRole.Bot));
            Assert.That(capturedContext, Is.EqualTo("yt-bot-ctx"));
        });

        _youtubeAccountsStore.Mutate(PlatformAuthRole.Bot, "yt-bot-ctx", account =>
        {
            account.ChannelId = "UC_OTHER_BOT";
        });

        var foreignBotResult = await applier.ApplyAsync(new BroadcastProfile { Name = "Профиль", Title = "Новое название" }, null, CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(foreignBotResult.Succeeded, Is.False);
            Assert.That(foreignBotResult.ErrorMessage, Does.Contain("youtube.force-ssl"));
        });
    }

    [Test]
    public async Task BroadcastProfilesManager_ApplyAsync_TwitchAndYouTube_FullSuccess_And_PartialFailure()
    {
        var manager = CreateManager();
        var profile = new BroadcastProfile { Name = "Мульти", Title = "Эфир #{n}", CurrentNumber = 5 };
        manager.Upsert(
            profile,
            [StreamingPlatform.Twitch, StreamingPlatform.YouTube],
            new BroadcastProfileYouTubeMetadata { Description = "Серия {n}" });

        BroadcastProfileApplied? appliedEvent = null;
        _eventBus.PublishAsync(Arg.Do<BroadcastProfileApplied>(e => appliedEvent = e), Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);

        await manager.ApplyAsync(profile.Id, CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(appliedEvent, Is.Not.Null);
            Assert.That(appliedEvent!.PlatformResults, Has.Count.EqualTo(2));
            Assert.That(appliedEvent.PlatformResults!.All(static r => r.Succeeded), Is.True);
            Assert.That(BroadcastProfileApplySummary.IsPartial(appliedEvent.PlatformResults), Is.False);
        });

        _youtubeApplier.ApplyAsync(
                Arg.Any<BroadcastProfile>(),
                Arg.Any<BroadcastProfileYouTubeMetadata?>(),
                Arg.Any<CancellationToken>())
            .Returns(new BroadcastProfilePlatformApplyResult(StreamingPlatform.YouTube, false, "Нет прав записи YouTube"));

        appliedEvent = null;
        await manager.ApplyAsync(profile.Id, CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(appliedEvent, Is.Not.Null);
            Assert.That(BroadcastProfileApplySummary.IsPartial(appliedEvent!.PlatformResults), Is.True);
            Assert.That(appliedEvent.PlatformResults!.Single(r => r.Platform == StreamingPlatform.Twitch).Succeeded, Is.True);
            Assert.That(appliedEvent.PlatformResults!.Single(r => r.Platform == StreamingPlatform.YouTube).Succeeded, Is.False);
            Assert.That(
                BroadcastProfileApplySummary.FormatPartialStatus(profile.Name, appliedEvent.PlatformResults!),
                Does.Contain("Twitch обновлён").And.Contain("YouTube – Нет прав записи YouTube"));
        });
    }

    [Test]
    public async Task BroadcastProfilesManager_ApplyAsync_YouTubeOnly_And_AllTargetsFail()
    {
        var manager = CreateManager();
        var profile = new BroadcastProfile { Name = "Только YT", Title = "Без номера", CurrentNumber = 3 };
        manager.Upsert(
            profile,
            [StreamingPlatform.YouTube],
            new BroadcastProfileYouTubeMetadata { TitleOverride = "YT #{n}" });

        BroadcastProfileApplied? appliedEvent = null;
        _eventBus.PublishAsync(Arg.Do<BroadcastProfileApplied>(e => appliedEvent = e), Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);

        await manager.ApplyAsync(profile.Id, CancellationToken.None);

        await _twitchApplier.DidNotReceiveWithAnyArgs().ApplyAsync(null!, CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(appliedEvent, Is.Not.Null);
            Assert.That(appliedEvent!.PlatformResults, Has.Count.EqualTo(1));
            Assert.That(appliedEvent.PlatformResults![0].Platform, Is.EqualTo(StreamingPlatform.YouTube));
            Assert.That(_profilesStore.Load().Profiles.Single().LastApplyAt, Is.EqualTo(_clock.UtcNow));
        });

        _twitchApplier.ApplyAsync(Arg.Any<BroadcastProfile>(), Arg.Any<CancellationToken>()).Returns(false);
        _youtubeApplier.ApplyAsync(
                Arg.Any<BroadcastProfile>(),
                Arg.Any<BroadcastProfileYouTubeMetadata?>(),
                Arg.Any<CancellationToken>())
            .Returns(new BroadcastProfilePlatformApplyResult(StreamingPlatform.YouTube, false, "На YouTube нет активной трансляции"));

        var bothProfile = new BroadcastProfile { Name = "Оба упали", Title = "Тест" };
        manager.Upsert(bothProfile, [StreamingPlatform.Twitch, StreamingPlatform.YouTube]);

        BroadcastProfileApplyFailed? failedEvent = null;
        _eventBus.PublishAsync(Arg.Do<BroadcastProfileApplyFailed>(e => failedEvent = e), Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);

        await manager.ApplyAsync(bothProfile.Id, CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(failedEvent, Is.Not.Null);
            Assert.That(failedEvent!.PlatformResults, Has.Count.EqualTo(2));
            Assert.That(failedEvent.PlatformResults!.All(static r => !r.Succeeded), Is.True);
            Assert.That(failedEvent.ErrorMessage, Does.Contain("YouTube"));
        });
    }

    [Test]
    public async Task BroadcastProfilesManager_Upsert_And_Remove_Persist_And_Rollback_YouTubeMetadata()
    {
        var manager = CreateManager();
        var profile = new BroadcastProfile { Name = "С метаданными", Title = "Тест" };

        var written = manager.Upsert(
            profile,
            [StreamingPlatform.Twitch, StreamingPlatform.YouTube],
            new BroadcastProfileYouTubeMetadata
            {
                TitleOverride = "YT Заголовок",
                Description = "YT Описание",
                PrivacyStatus = YouTubeBroadcastPrivacyStatus.Private,
            });

        Assert.Multiple(() =>
        {
            Assert.That(written, Is.True);
            Assert.That(manager.GetYouTubeMetadata(profile.Id)?.TitleOverride, Is.EqualTo("YT Заголовок"));
            Assert.That(manager.GetYouTubeMetadata(profile.Id)?.PrivacyStatus, Is.EqualTo(YouTubeBroadcastPrivacyStatus.Private));
        });

        manager.Remove(profile.Id);
        Assert.That(manager.GetYouTubeMetadata(profile.Id), Is.Null);

        var gate = new SettingsWriteGate();
        var blockedYouTubePath = Path.Combine(_tempDir, "blocked-youtube.json");
        await gate.RunExternalWriteAsync(
            () =>
            {
                File.WriteAllText(blockedYouTubePath, "{}");
                return Task.FromResult(0);
            },
            static _ => ["blocked-youtube.json"]);

        var blockedStore = new BroadcastProfileYouTubeStore(null, blockedYouTubePath, gate);
        var gatedManager = new BroadcastProfilesManager(
            _profilesStore,
            _twitchApplier,
            _eventBus,
            _clock,
            NullLogger<BroadcastProfilesManager>.Instance,
            _platformsStore,
            blockedStore,
            _youtubeApplier);

        var rollbackProfile = new BroadcastProfile { Name = "Откат", Title = "Тест" };
        var rollbackWritten = gatedManager.Upsert(
            rollbackProfile,
            [StreamingPlatform.Twitch, StreamingPlatform.YouTube],
            new BroadcastProfileYouTubeMetadata { TitleOverride = "Не запишется" });

        Assert.Multiple(() =>
        {
            Assert.That(rollbackWritten, Is.False);
            Assert.That(_profilesStore.Load().Profiles, Is.Empty);
            Assert.That(_platformsStore.Load().Profiles, Does.Not.ContainKey(rollbackProfile.Id));
        });
    }

    private BroadcastProfilesManager CreateManager()
    {
        return new(
            _profilesStore,
            _twitchApplier,
            _eventBus,
            _clock,
            NullLogger<BroadcastProfilesManager>.Instance,
            _platformsStore,
            _youtubeStore,
            _youtubeApplier);
    }

    private void SeedActiveYouTubeChannel(string channelId, string broadcasterContextId)
    {
        _catalogStore.Save(new ChannelsSettings
        {
            Channels =
            [
                new ChannelCatalogEntry
                {
                    Platform = StreamingPlatform.YouTube,
                    ChannelId = channelId,
                    DisplayName = "Main YT",
                    IsConfigured = true,
                    BroadcasterContextId = broadcasterContextId,
                },
            ],
            Platforms =
            [
                new PlatformChannelRoutingSettings
                {
                    Platform = StreamingPlatform.YouTube,
                    ActiveChannelId = channelId,
                },
            ],
        });
    }

    private void SeedYouTubeBroadcaster(string? contextId, string accessToken, string[] storedScopes)
    {
        _youtubeAccountsStore.Mutate(PlatformAuthRole.Broadcaster, contextId, account =>
        {
            account.AccessToken = accessToken;
            account.StoredScopes = storedScopes;
            account.ChannelId = "UC_MAIN";
            account.ChannelTitle = "Main YT";
        });
    }
}
