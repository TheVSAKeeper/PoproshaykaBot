using KeepShell.Diagnostics;
using KeepShell.Services.Platform;
using KeepShell.Testing;
using Microsoft.Extensions.Logging.Abstractions;
using PoproshaykaBot.Core.Chat;
using PoproshaykaBot.Core.Infrastructure;
using PoproshaykaBot.Core.Infrastructure.Events;
using PoproshaykaBot.Core.Platforms;
using PoproshaykaBot.Core.Settings;
using PoproshaykaBot.Core.Statistics;
using PoproshaykaBot.Core.Users;
using PoproshaykaBot.Wpf.Bootstrap;
using PoproshaykaBot.Wpf.Infrastructure;
using PoproshaykaBot.Wpf.Tests.Support;
using PoproshaykaBot.Wpf.ViewModels;
using System.Diagnostics;
using System.IO;
using System.Text.Json;

namespace PoproshaykaBot.Wpf.Tests.Bootstrap;

[TestFixture]
public class StartupHydrationTests
{
    private static readonly JsonSerializerOptions WebJson = new(JsonSerializerDefaults.Web);
    private string _directory = null!;

    [SetUp]
    public void SetUp()
    {
        _directory = Path.Combine(Path.GetTempPath(), "poproshayka-startup-" + Guid.NewGuid().ToString("N"));
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
    public async Task StatisticsPreload_загружает_пользователей_асинхронно_и_переживает_битый_файл()
    {
        await File.WriteAllTextAsync(Path.Combine(_directory, "users_statistics.json"), "{broken");
        var (repo, loader) = CreateLoader();
        using var preload = new StatisticsPreload(loader, static action => action());

        await preload.EnsureLoadedAsync();

        Assert.Multiple(() =>
        {
            Assert.That(preload.IsCompleted, Is.True);
            Assert.That(loader.IsLoadedFor(StreamingPlatform.Twitch), Is.False);
            Assert.That(loader.IsLoadedFor(StreamingPlatform.YouTube), Is.True);
            Assert.That(repo.GetAll(), Is.Empty);
        });
    }

    [Test]
    public async Task UserStatisticsPageViewModel_откладывает_гидратацию_при_старте_и_догружает_по_OnEnter_и_TrySelect()
    {
        WriteUsers(
        [
            new UserStatistics { Platform = StreamingPlatform.Twitch, UserId = "42", Name = "viewer42", MessageCount = 15, BonusPoints = 250 },
        ]);

        var (repo, loader) = CreateLoader();
        var backgroundQueue = new Queue<Action>();
        using var preload = new StatisticsPreload(loader, backgroundQueue.Enqueue);
        using var viewModel = CreateUsersViewModel(repo, preload);

        Assert.That(viewModel.Users, Is.Empty, "конструктор не должен собирать строки пользователей до загрузки или входа");

        await preload.EnsureLoadedAsync();
        Assert.That(viewModel.TrySelect(new PlatformUserKey(StreamingPlatform.Twitch, "42")), Is.True);
        Assert.That(viewModel.SelectedRow?.Name, Is.EqualTo("viewer42"));

        while (backgroundQueue.Count > 0)
        {
            backgroundQueue.Dequeue()();
        }

        Assert.That(viewModel.Users.Count, Is.EqualTo(1));
        Assert.That(viewModel.SelectedRow?.Name, Is.EqualTo("viewer42"));
    }

    [Test]
    public async Task UserStatisticsPageViewModel_TrySelectAfterPreloadAsync_дожидается_предзагрузки_и_выбирает_пользователя()
    {
        WriteUsers(
        [
            new UserStatistics { Platform = StreamingPlatform.Twitch, UserId = "88", Name = "deferredUser", MessageCount = 19, BonusPoints = 300 },
        ]);

        var (repo, loader) = CreateLoader();
        var uiQueue = new Queue<Action>();
        var sync = new Lock();
        void EnqueueUi(Action action)
        {
            lock (sync)
            {
                uiQueue.Enqueue(action);
            }
        }

        var dispatcher = new QueuedDispatcher(EnqueueUi);
        using var preload = new StatisticsPreload(loader, EnqueueUi);
        using var viewModel = CreateUsersViewModel(repo, preload, dispatcher);

        var key = new PlatformUserKey(StreamingPlatform.Twitch, "88");
        var selectTask = viewModel.TrySelectAfterPreloadAsync(key);

        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (!selectTask.IsCompleted && DateTime.UtcNow < deadline)
        {
            Action? next;
            lock (sync)
            {
                uiQueue.TryDequeue(out next);
            }

            if (next is not null)
            {
                next();
            }
            else
            {
                await Task.Delay(10);
            }
        }

        var selected = await selectTask.WaitAsync(TimeSpan.FromSeconds(2));

        Assert.Multiple(() =>
        {
            Assert.That(selected, Is.True);
            Assert.That(viewModel.SelectedRow?.Key, Is.EqualTo(key));
            Assert.That(viewModel.SelectedRow?.Name, Is.EqualTo("deferredUser"));
        });
    }

    [Test]
    public void StatisticsPreload_Stop_дожидается_завершения_чтения_даже_при_отменённом_токене_остановки()
    {
        WriteUsers(
        [
            new UserStatistics { Platform = StreamingPlatform.Twitch, UserId = "5", Name = "slowUser", MessageCount = 2 },
        ]);

        var (_, loader) = CreateLoader();
        using var preload = new StatisticsPreload(loader, static _ => { });
        preload.Start();

        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();

        preload.Stop(cancelled.Token);

        Assert.That(preload.IsCompleted, Is.True);
    }

    [Test]
    public async Task UserStatisticsPageViewModel_на_OnEnter_во_время_загрузки_не_падает_и_подхватывает_строки()
    {
        WriteUsers(
        [
            new UserStatistics { Platform = StreamingPlatform.Twitch, UserId = "7", Name = "early", MessageCount = 5, BonusPoints = 100 },
        ]);

        var (repo, loader) = CreateLoader();
        var uiQueue = new Queue<Action>();
        var sync = new Lock();
        var callbackQueued = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        void EnqueueUi(Action action)
        {
            lock (sync)
            {
                uiQueue.Enqueue(action);
            }

            callbackQueued.TrySetResult();
        }

        var dispatcher = new QueuedDispatcher(EnqueueUi);
        using var preload = new StatisticsPreload(loader, EnqueueUi);
        using var viewModel = CreateUsersViewModel(repo, preload, dispatcher);

        viewModel.OnEnter();
        await preload.EnsureLoadedAsync();
        await callbackQueued.Task.WaitAsync(TimeSpan.FromSeconds(5));

        while (true)
        {
            Action? next;
            lock (sync)
            {
                uiQueue.TryDequeue(out next);
            }

            if (next is null)
            {
                break;
            }

            next();
        }

        Assert.That(viewModel.Users.Select(row => row.Name), Is.EqualTo(new[] { "early" }));
    }

    [Test]
    public async Task StreamHistoryPageViewModel_откладывает_сборку_в_конструкторе_и_гидратирует_при_OnEnter_или_BoxArtLoading()
    {
        var (repo, loader) = CreateLoader();
        var historyStore = new BroadcastHistoryStore(
            null,
            Path.Combine(_directory, "broadcasts.json"),
            Path.Combine(_directory, "stream_sessions.json"));

        var start = new DateTimeOffset(new DateTime(2026, 9, 10, 18, 0, 0, DateTimeKind.Local));
        var session = BroadcastRecord.FromTwitchSession(new StreamSessionRecord
        {
            Channel = "channel",
            StartedAt = start,
            EndedAt = start.AddHours(2),
            Title = "тест",
            Game = "Just Chatting",
            MessageCount = 12,
            ChatterCount = 1,
            PeakViewers = 20,
            AverageViewers = 10,
            Chatters =
            [
                new StreamSessionChatter { UserId = "99", DisplayName = "chatter99", MessageCount = 12 },
            ],
        });
        historyStore.TryAppend(session);

        var backgroundQueue = new Queue<Action>();
        using var preload = new StatisticsPreload(loader, backgroundQueue.Enqueue);
        var boxArt = new GameBoxArtProvider(new FakeBoxArtCache(), NullLogger<GameBoxArtProvider>.Instance);
        using var streams = new StreamHistoryPageViewModel(
            historyStore,
            repo,
            new MemorySettings(),
            boxArt,
            new InMemoryEventBus(NullLogger<InMemoryEventBus>.Instance),
            preload);

        Assert.That(streams.Sessions, Is.Empty, "конструктор не должен строить проекцию до входа");

        streams.OnEnter();
        Assert.That(streams.Sessions.Count, Is.EqualTo(1));
        Assert.That(streams.TrySelectAt(0), Is.True);
        Assert.That(streams.Chatters.Single().CanOpen, Is.False, "до загрузки статистики пользователей ссылка закрыта");

        await preload.EnsureLoadedAsync();
        repo.SeedPlatform(
            StreamingPlatform.Twitch,
            [
                new UserStatistics { Platform = StreamingPlatform.Twitch, UserId = "99", Name = "chatter99", MessageCount = 8, BonusPoints = 50 },
            ]);
        while (backgroundQueue.Count > 0)
        {
            backgroundQueue.Dequeue()();
        }

        Assert.That(streams.Chatters.Single().CanOpen, Is.True, "после завершения предзагрузки ссылка в карточке стрима должна открыться");
        await streams.BoxArtLoading;
    }

    [Test]
    public void ComputeStartupElapsed_вычитает_ожидание_модалок_без_ухода_в_минус_и_пишет_в_PerformanceMonitor()
    {
        var stamp = Stopwatch.GetTimestamp();
        var clamped = App.ComputeStartupElapsed(stamp, TimeSpan.FromMinutes(10));
        var measured = App.ComputeStartupElapsed(stamp - Stopwatch.Frequency, TimeSpan.FromMilliseconds(200));

        var monitor = new PerformanceMonitor(new DiagnosticsOptions(), new ManualUiDispatcher());
        monitor.ReportStartup(measured);
        monitor.Stop();

        Assert.Multiple(() =>
        {
            Assert.That(clamped, Is.EqualTo(TimeSpan.Zero));
            Assert.That(measured.TotalSeconds, Is.GreaterThanOrEqualTo(0.7));
            Assert.That(monitor.Snapshot.StartupSeconds, Is.GreaterThanOrEqualTo(0.7));
        });
    }

    private (UserStatisticsRepository Repo, UserStatisticsLoader Loader) CreateLoader()
    {
        var repo = new UserStatisticsRepository(NullLogger<UserStatisticsRepository>.Instance);
        var store = new StatisticsFileStore(NullLogger<StatisticsFileStore>.Instance, _directory);
        var units = UserStatisticsPlatformCatalog.All
            .Select(descriptor => new PlatformUserStatisticsUnit(
                descriptor,
                repo.GetPartition(descriptor.Platform),
                store,
                NullLogger<PlatformUserStatisticsUnit>.Instance))
            .ToArray();

        return (repo, new UserStatisticsLoader(units));
    }

    private void WriteUsers(List<UserStatistics> users)
    {
        var json = JsonSerializer.Serialize(users, WebJson);
        File.WriteAllText(Path.Combine(_directory, "users_statistics.json"), json);
    }

    private UserStatisticsPageViewModel CreateUsersViewModel(
        IUserStatisticsRepository repo,
        StatisticsPreload preload,
        IUiDispatcher? dispatcher = null)
    {
        var settingsManager = new SettingsManager(NullLogger<SettingsManager>.Instance, Path.Combine(_directory, "settings.json"));
        var eventBus = new InMemoryEventBus(NullLogger<InMemoryEventBus>.Instance);

        return new(
            repo,
            null!,
            new UserRankService(settingsManager),
            new UserPointsManagementService(repo, settingsManager, eventBus, NullLogger<UserPointsManagementService>.Instance),
            new FakeChannelProvider(),
            settingsManager,
            null!,
            eventBus,
            new MemorySettings(),
            FakeAvatarCache.CreateProvider(),
            preload: preload,
            uiDispatcher: dispatcher);
    }

    private sealed class FakeChannelProvider : IChannelProvider
    {
        public string? Channel => "test";
    }

    private sealed class QueuedDispatcher(Action<Action> enqueue) : IUiDispatcher
    {
        public bool HasAccess => true;

        public void Invoke(Action action) => enqueue(action);

        public Task InvokeAsync(Action action)
        {
            enqueue(action);
            return Task.CompletedTask;
        }

        public IUiTimer CreateTimer(TimeSpan interval, Action tick) => throw new NotSupportedException();
    }
}
