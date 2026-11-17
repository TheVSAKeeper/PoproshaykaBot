using Microsoft.Extensions.Logging.Abstractions;
using PoproshaykaBot.Core.Platforms;
using PoproshaykaBot.Core.Settings;
using PoproshaykaBot.Core.Settings.Stores;
using PoproshaykaBot.Core.Statistics;
using PoproshaykaBot.Core.Tests.Twitch.EventSub;
using System.Text.Json;

namespace PoproshaykaBot.Core.Tests.Statistics;

[TestFixture]
public sealed class LegacyUserStatisticsMigrationTests
{
    private static readonly JsonSerializerOptions WebJson = new(JsonSerializerDefaults.Web);

    private string _directory = null!;
    private ManualTimeProvider _time = null!;

    [SetUp]
    public void SetUp()
    {
        _directory = Path.Combine(Path.GetTempPath(), $"poproshayka-legacy-migration-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_directory);
        _time = new();
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
    public async Task Provable_single_channel_history_migrates_exact_totals_into_ChannelCounters_without_modifying_v1_files()
    {
        var channelId = "100500";
        var v1Users = new List<UserStatistics>
        {
            new()
            {
                UserId = "u1",
                Name = "Alice",
                MessageCount = 12,
                BonusPoints = 30,
                PenaltyPoints = 5,
                FirstSeen = new(2026, 9, 10, 10, 0, 0, DateTimeKind.Utc),
                LastSeen = new(2026, 9, 10, 12, 0, 0, DateTimeKind.Utc),
            },
        };
        var legacyPath = Path.Combine(_directory, "users_statistics.json");
        var legacyBytes = JsonSerializer.SerializeToUtf8Bytes(v1Users, WebJson);
        await File.WriteAllBytesAsync(legacyPath, legacyBytes);

        var historyStore = new BroadcastHistoryStore(
            null,
            Path.Combine(_directory, "broadcasts.json"),
            Path.Combine(_directory, "stream_sessions.json"));
        historyStore.TryAppend(new BroadcastRecord
        {
            Id = Guid.NewGuid(),
            StartedAt = new(2026, 9, 10, 9, 30, 0, TimeSpan.Zero),
            EndedAt = new(2026, 9, 10, 12, 30, 0, TimeSpan.Zero),
            Platforms =
            [
                new BroadcastPlatformRecord
                {
                    Platform = StreamingPlatform.Twitch,
                    Channel = "streamer_login",
                    ChannelId = channelId,
                    StartedAt = new(2026, 9, 10, 9, 30, 0, TimeSpan.Zero),
                    EndedAt = new(2026, 9, 10, 12, 30, 0, TimeSpan.Zero),
                    MessageCount = 50,
                    ChatterCount = 1,
                    Chatters =
                    [
                        new StreamSessionChatter { UserId = "u1", DisplayName = "Alice", MessageCount = 8 },
                    ],
                },
            ],
        });

        var graph = new StatisticsTestGraph(
            _directory,
            broadcastHistoryStore: historyStore,
            identityResolver: new KnownChannelIdentityResolver());
        await graph.UserLoader.EnsureLoadedAsync();

        var channelKey = new ChannelKey(StreamingPlatform.Twitch, channelId);
        var slice = graph.UserRepository.QuerySlice(StatisticsSlice.ForChannel(channelKey));

        using (Assert.EnterMultipleScope())
        {
            Assert.That(graph.UserLoader.IsLoaded, Is.True);
            Assert.That(slice, Has.Count.EqualTo(1));
            Assert.That(slice[0].MessageCount, Is.EqualTo(12UL), "Совпадение со старой таблицей должно быть точным, без двойного сложения с broadcasts.json");
            Assert.That(slice[0].BonusPoints, Is.EqualTo(30UL));
            Assert.That(slice[0].PenaltyPoints, Is.EqualTo(5UL));
            Assert.That(graph.UserRepository.GetUnattributedPlatforms(), Is.Empty);
            Assert.That(await File.ReadAllBytesAsync(legacyPath), Is.EqualTo(legacyBytes), "Исходный v1-файл не изменяется и не удаляется");
        }
    }

    [Test]
    public async Task Empty_broadcast_history_puts_all_legacy_users_into_UnattributedCounters_even_when_channel_is_configured()
    {
        var legacyPath = Path.Combine(_directory, "users_statistics.json");
        await File.WriteAllTextAsync(legacyPath, JsonSerializer.Serialize(new List<UserStatistics>
        {
            new() { UserId = "u1", Name = "Alice", MessageCount = 7, FirstSeen = DateTime.UtcNow, LastSeen = DateTime.UtcNow },
        }, WebJson));

        var catalog = new ChannelCatalogStore(null, Path.Combine(_directory, "channels.json"));
        catalog.EnsureChannelKnown(new(StreamingPlatform.Twitch, "999"), "current_channel");

        var settingsManager = new SettingsManager(NullLogger<SettingsManager>.Instance, Path.Combine(_directory, "settings.json"));
        settingsManager.Current.Twitch.Channel = "current_channel";

        var graph = new StatisticsTestGraph(
            _directory,
            identityResolver: new KnownChannelIdentityResolver(catalog, settingsManager: settingsManager));
        await graph.UserLoader.EnsureLoadedAsync();

        var unattributed = graph.UserRepository.QuerySlice(StatisticsSlice.Unattributed(StreamingPlatform.Twitch));
        var byChannel = graph.UserRepository.QuerySlice(StatisticsSlice.ForChannel(new(StreamingPlatform.Twitch, "999")));

        using (Assert.EnterMultipleScope())
        {
            Assert.That(byChannel, Is.Empty, "При пустой истории угадывание канала запрещено");
            Assert.That(unattributed, Has.Count.EqualTo(1));
            Assert.That(unattributed[0].MessageCount, Is.EqualTo(7UL));
        }
    }

    [Test]
    public async Task Multi_channel_viewer_or_prehistoric_tail_and_ambiguous_login_fall_back_to_UnattributedCounters()
    {
        var preHistoryViewer = new UserStatistics
        {
            UserId = "u_tail",
            Name = "Prehistoric",
            MessageCount = 20,
            FirstSeen = new(2026, 8, 1, 10, 0, 0, DateTimeKind.Utc),
            LastSeen = new(2026, 9, 10, 12, 0, 0, DateTimeKind.Utc),
        };
        var multiChannelViewer = new UserStatistics
        {
            UserId = "u_both",
            Name = "BothChannels",
            MessageCount = 15,
            FirstSeen = new(2026, 9, 10, 10, 0, 0, DateTimeKind.Utc),
            LastSeen = new(2026, 9, 11, 12, 0, 0, DateTimeKind.Utc),
        };

        await File.WriteAllTextAsync(Path.Combine(_directory, "users_statistics.json"), JsonSerializer.Serialize(new[]
        {
            preHistoryViewer,
            multiChannelViewer,
        }, WebJson));

        var catalog = new ChannelCatalogStore(null, Path.Combine(_directory, "channels.json"));
        catalog.Save(new ChannelsSettings
        {
            Channels =
            [
                new ChannelCatalogEntry { Platform = StreamingPlatform.Twitch, ChannelId = "100", DisplayName = "shared_login" },
                new ChannelCatalogEntry { Platform = StreamingPlatform.Twitch, ChannelId = "200", DisplayName = "shared_login" },
            ],
        });

        var historyStore = new BroadcastHistoryStore(
            null,
            Path.Combine(_directory, "broadcasts.json"),
            Path.Combine(_directory, "stream_sessions.json"));
        historyStore.TryAppend(new BroadcastRecord
        {
            Id = Guid.NewGuid(),
            StartedAt = new(2026, 9, 10, 9, 0, 0, TimeSpan.Zero),
            EndedAt = new(2026, 9, 10, 13, 0, 0, TimeSpan.Zero),
            Platforms =
            [
                new BroadcastPlatformRecord
                {
                    Platform = StreamingPlatform.Twitch,
                    Channel = "shared_login",
                    StartedAt = new(2026, 9, 10, 9, 0, 0, TimeSpan.Zero),
                    EndedAt = new(2026, 9, 10, 13, 0, 0, TimeSpan.Zero),
                    Chatters =
                    [
                        new StreamSessionChatter { UserId = "u_tail", DisplayName = "Prehistoric", MessageCount = 5 },
                        new StreamSessionChatter { UserId = "u_both", DisplayName = "BothChannels", MessageCount = 5 },
                    ],
                },
            ],
        });
        historyStore.TryAppend(new BroadcastRecord
        {
            Id = Guid.NewGuid(),
            StartedAt = new(2026, 9, 11, 9, 0, 0, TimeSpan.Zero),
            EndedAt = new(2026, 9, 11, 13, 0, 0, TimeSpan.Zero),
            Platforms =
            [
                new BroadcastPlatformRecord
                {
                    Platform = StreamingPlatform.Twitch,
                    Channel = "other_ch",
                    ChannelId = "300",
                    StartedAt = new(2026, 9, 11, 9, 0, 0, TimeSpan.Zero),
                    EndedAt = new(2026, 9, 11, 13, 0, 0, TimeSpan.Zero),
                    Chatters =
                    [
                        new StreamSessionChatter { UserId = "u_both", DisplayName = "BothChannels", MessageCount = 10 },
                    ],
                },
            ],
        });

        var graph = new StatisticsTestGraph(
            _directory,
            broadcastHistoryStore: historyStore,
            identityResolver: new KnownChannelIdentityResolver(catalog));
        await graph.UserLoader.EnsureLoadedAsync();

        var unattributed = graph.UserRepository.QuerySlice(StatisticsSlice.Unattributed(StreamingPlatform.Twitch));
        var all = graph.UserRepository.QuerySlice(StatisticsSlice.All());

        using (Assert.EnterMultipleScope())
        {
            Assert.That(unattributed, Has.Count.EqualTo(2));
            Assert.That(all.Sum(static v => (long)v.MessageCount), Is.EqualTo(35L));
        }
    }

    [Test]
    public async Task Second_startup_reads_existing_catalog_and_ignores_subsequent_v1_edits()
    {
        var fileStore = new StatisticsFileStore(NullLogger<StatisticsFileStore>.Instance, _directory);
        await fileStore.SaveUserStatisticsCatalogAsync(new UserStatisticsCatalogDocument
        {
            SchemaVersion = UserStatisticsCatalogDocument.CurrentSchemaVersion,
            Migration = new()
            {
                Version = UserStatisticsCatalogDocument.CurrentMigrationVersion,
                CompletedAtUtc = _time.GetUtcNow(),
                MigratedPlatforms = [StreamingPlatform.Twitch],
            },
            Accounts =
            [
                new() { Platform = StreamingPlatform.Twitch, UserId = "u1", Name = "CatalogUser", FirstSeen = DateTime.UtcNow, LastSeen = DateTime.UtcNow },
            ],
            ChannelCounters =
            [
                new() { Platform = StreamingPlatform.Twitch, ChannelId = "10", UserId = "u1", MessageCount = 4 },
            ],
        });

        await File.WriteAllTextAsync(Path.Combine(_directory, "users_statistics.json"), JsonSerializer.Serialize(new[]
        {
            new UserStatistics { UserId = "legacy_only", Name = "LegacyExtra", MessageCount = 99 },
        }, WebJson));

        var graph = new StatisticsTestGraph(_directory);
        await graph.UserLoader.EnsureLoadedAsync();
        var all = graph.UserRepository.QuerySlice(StatisticsSlice.All());

        using (Assert.EnterMultipleScope())
        {
            Assert.That(all, Has.Count.EqualTo(1));
            Assert.That(all[0].DisplayName, Is.EqualTo("CatalogUser"));
            Assert.That(all[0].MessageCount, Is.EqualTo(4UL));
        }
    }

    [Test]
    public async Task Corrupted_catalog_or_future_schema_version_abandons_load_without_falling_back_to_v1()
    {
        await File.WriteAllTextAsync(Path.Combine(_directory, "users_statistics.json"), JsonSerializer.Serialize(new[]
        {
            new UserStatistics { UserId = "u1", Name = "LegacyUser", MessageCount = 5 },
        }, WebJson));
        var catalogPath = Path.Combine(_directory, "user_statistics_catalog.json");
        await File.WriteAllTextAsync(catalogPath, "{\"schemaVersion\": 99, \"accounts\": []}");

        var graph = new StatisticsTestGraph(_directory);
        await graph.UserLoader.EnsureLoadedAsync();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(graph.UserLoader.IsLoaded, Is.False);
            Assert.That(graph.UserRepository.GetKnownAccounts(), Is.Empty);
        }
    }
}
