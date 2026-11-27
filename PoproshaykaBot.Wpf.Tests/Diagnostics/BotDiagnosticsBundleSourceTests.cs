using KeepShell.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using PoproshaykaBot.Core.Broadcast;
using PoproshaykaBot.Core.Chat;
using PoproshaykaBot.Core.Debugging;
using PoproshaykaBot.Core.Diagnostics;
using PoproshaykaBot.Core.Infrastructure.Di;
using PoproshaykaBot.Core.Infrastructure.Diagnostics;
using PoproshaykaBot.Core.Infrastructure.Hosting;
using PoproshaykaBot.Core.Infrastructure.Logging;
using PoproshaykaBot.Core.Infrastructure.Runtime;
using PoproshaykaBot.Core.Obs;
using PoproshaykaBot.Core.Platforms.Auth;
using PoproshaykaBot.Core.Polls;
using PoproshaykaBot.Core.Server;
using PoproshaykaBot.Core.Settings;
using PoproshaykaBot.Core.Settings.Stores;
using PoproshaykaBot.Core.Statistics;
using PoproshaykaBot.Core.Streaming;
using PoproshaykaBot.Core.Twitch;
using PoproshaykaBot.Core.Twitch.GameArt;
using KeepShell.Updates;
using PoproshaykaBot.Core.Update;
using PoproshaykaBot.Core.Users.Avatars;
using PoproshaykaBot.Core.YouTube.Quota;
using PoproshaykaBot.Wpf.Infrastructure.Diagnostics;

namespace PoproshaykaBot.Wpf.Tests.Diagnostics;

[TestFixture]
public class BotDiagnosticsBundleSourceTests
{
    [Test]
    public async Task Снимок_без_YouTube_источников_пишет_строки_отсутствия_данных()
    {
        await using var provider = BuildProvider(withYouTubeSources: false);
        var source = new BotDiagnosticsBundleSource(
            provider.GetServices<ISettingsSnapshotContributor>(),
            provider.GetRequiredService<DiagnosticsSnapshotSource>(),
            NullLogger<BotDiagnosticsBundleSource>.Instance);

        var snapshotEntry = source.Collect().Single(entry => entry.Name == "bot-snapshot.txt");

        using (Assert.EnterMultipleScope())
        {
            Assert.That(snapshotEntry.Text, Does.Contain("Мониторинг YouTube: данных нет"));
            Assert.That(snapshotEntry.Text, Does.Contain("Чат YouTube: данных нет"));
            Assert.That(snapshotEntry.Text, Does.Contain("Квота YouTube: данных нет"));
        }
    }

    [Test]
    public async Task Снимок_с_YouTube_источниками_форматирует_мониторинг_чат_и_квоту()
    {
        await using var provider = BuildProvider(withYouTubeSources: true);
        var source = new BotDiagnosticsBundleSource(
            provider.GetServices<ISettingsSnapshotContributor>(),
            provider.GetRequiredService<DiagnosticsSnapshotSource>(),
            NullLogger<BotDiagnosticsBundleSource>.Instance);

        var snapshotEntry = source.Collect().Single(entry => entry.Name == "bot-snapshot.txt");

        using (Assert.EnterMultipleScope())
        {
            Assert.That(
                snapshotEntry.Text,
                Does.Contain("Мониторинг YouTube: онлайн, канал UCtarget, видео vid-1, чат chat-1, состояние: несколько активных трансляций (2)"));
            Assert.That(
                snapshotEntry.Text,
                Does.Contain($"Чат YouTube: {DiagnosticsFormat.Describe(ConnectionState.Connected)} (gRPC), канал @creator, чат chat-1, сообщений {DiagnosticsFormat.Number(42)}, разрывов курсора 1"));
            Assert.That(
                snapshotEntry.Text,
                Does.Contain(
                    $"Квота YouTube (proj-1): {DiagnosticsFormat.Number(125)} из {DiagnosticsFormat.Number(10_000)} "
                    + $"(в резерве {DiagnosticsFormat.Number(10)}, бот {DiagnosticsFormat.Number(25)}, ведущий {DiagnosticsFormat.Number(100)}"));
            Assert.That(snapshotEntry.Text, Does.Contain("запись заморожена до перезапуска"));
        }
    }

    [Test]
    public async Task Снимок_форматирует_нехватку_авторизации_бота_YouTube()
    {
        await using var provider = BuildProvider(
            withYouTubeSources: true,
            youtubeMonitor: new StubYouTubeMonitorMissingBotAuth());
        var source = new BotDiagnosticsBundleSource(
            provider.GetServices<ISettingsSnapshotContributor>(),
            provider.GetRequiredService<DiagnosticsSnapshotSource>(),
            NullLogger<BotDiagnosticsBundleSource>.Instance);

        var snapshotEntry = source.Collect().Single(entry => entry.Name == "bot-snapshot.txt");

        Assert.That(snapshotEntry.Text, Does.Contain("состояние: нужна авторизация бота"));
    }

    [Test]
    public async Task Снимок_форматирует_неподтверждённый_доступ_аккаунта_YouTube()
    {
        await using var provider = BuildProvider(
            withYouTubeSources: true,
            youtubeMonitor: new StubYouTubeMonitorAccessUnverified());
        var source = new BotDiagnosticsBundleSource(
            provider.GetServices<ISettingsSnapshotContributor>(),
            provider.GetRequiredService<DiagnosticsSnapshotSource>(),
            NullLogger<BotDiagnosticsBundleSource>.Instance);

        var snapshotEntry = source.Collect().Single(entry => entry.Name == "bot-snapshot.txt");

        Assert.That(snapshotEntry.Text, Does.Contain("состояние: доступ аккаунта не подтверждён"));
    }

    [Test]
    public async Task Снимок_включает_прошлый_сбой_фоновые_исключения_мониторы_и_хранилища_без_ленивого_чтения()
    {
        await using var provider = BuildProvider(withYouTubeSources: false);
        var previousRun = new PreviousRunState(
            WasUnclean: true,
            new RunStateRecord(
                1234,
                new DateTimeOffset(2026, 11, 22, 10, 0, 0, TimeSpan.Zero),
                "3.1.0.15",
                "коммит abc1234",
                RunStateStatus.MemoryWatchdog,
                null,
                new DateTimeOffset(2026, 11, 22, 12, 0, 0, TimeSpan.Zero)));
        var tracker = new UnobservedTaskExceptionTracker();
        tracker.Record(new AggregateException(new InvalidOperationException("do-not-leak-message")));
        tracker.Record(new AggregateException(new OperationCanceledException()));

        var userLoader = provider.GetRequiredService<UserStatisticsLoader>();
        var pollStore = provider.GetRequiredService<PollHistoryStore>();

        var source = new BotDiagnosticsBundleSource(
            provider.GetServices<ISettingsSnapshotContributor>(),
            provider.GetRequiredService<DiagnosticsSnapshotSource>(),
            NullLogger<BotDiagnosticsBundleSource>.Instance,
            previousRun: previousRun,
            unobservedExceptions: tracker,
            updateEnvironment: provider.GetRequiredService<IUpdateEnvironment>(),
            userStatisticsLoader: userLoader,
            userStatisticsRepository: provider.GetRequiredService<UserStatisticsRepository>(),
            broadcastHistoryStore: provider.GetRequiredService<BroadcastHistoryStore>(),
            activeBroadcastStore: provider.GetRequiredService<ActiveBroadcastStore>(),
            pollHistoryStore: pollStore,
            webView2VersionProvider: static () => "130.0.2849.80",
            displayMonitorProvider: static () =>
            [
                new DisplayMonitorInfo(1, true, 0, 0, 2560, 1440, 144, 144),
                new DisplayMonitorInfo(2, false, 2560, 0, 1920, 1080, null, null),
            ]);

        var entries = source.Collect().ToList();
        var settingsEntry = entries.Single(entry => entry.Name == "settings.txt");
        var snapshotEntry = entries.Single(entry => entry.Name == "bot-snapshot.txt");

        using (Assert.EnterMultipleScope())
        {
            Assert.That(settingsEntry.Text, Does.Contain("BroadcastProfileYouTubeSettings:"));
            Assert.That(settingsEntry.Text, Does.Contain("RecentCategoriesSettings:"));
            Assert.That(settingsEntry.Text, Does.Contain("Account.Contexts:"));
            Assert.That(snapshotEntry.Text, Does.Contain("Прошлый запуск: нештатно (статус MemoryWatchdog, PID 1234"));
            Assert.That(snapshotEntry.Text, Does.Contain("WebView2 Runtime: 130.0.2849.80"));
            Assert.That(snapshotEntry.Text, Does.Contain("монитор 1, основной: 0,0 2560x1440, DPI 144x144 (150 %)"));
            Assert.That(snapshotEntry.Text, Does.Contain("монитор 2: 2560,0 1920x1080, DPI недоступен"));
            Assert.That(snapshotEntry.Text, Does.Contain("Необработанные фоновые задачи: сбоев 1, отмен 1"));
            Assert.That(snapshotEntry.Text, Does.Contain("System.InvalidOperationException"));
            Assert.That(snapshotEntry.Text, Does.Not.Contain("do-not-leak-message"));
            Assert.That(snapshotEntry.Text, Does.Contain("Статистика пользователей: состояние NotRead"));
            Assert.That(snapshotEntry.Text, Does.Contain("История голосований: чтение NotRead"));
            Assert.That(userLoader.LoadState, Is.EqualTo(StatisticsLoadState.NotRead));
            Assert.That(pollStore.IsLoaded, Is.False);
        }
    }

    private static ServiceProvider BuildProvider(
        bool withYouTubeSources,
        IYouTubeMonitorDiagnostics? youtubeMonitor = null)
    {
        var services = new ServiceCollection();
        services
            .AddCoreInfrastructure(new UiLogSink(), disposeSerilog: false)
            .AddStatistics()
            .AddSettingsStores()
            .AddDebugChannel(DebugChannelOverride.None)
            .AddTwitchClients()
            .AddGameBoxArt(cacheOnly: true)
            .AddUserAvatars(cacheOnly: true)
            .AddChatPipeline()
            .AddStreamMonitoring()
            .AddBroadcasting()
            .AddPolls()
            .AddHttpServer()
            .AddObsIntegration()
            .AddSelfUpdate();

        if (withYouTubeSources)
        {
            services.AddSingleton<IYouTubeMonitorDiagnostics>(youtubeMonitor ?? new StubYouTubeMonitorDiagnostics());
            services.AddSingleton<IYouTubeChatDiagnostics>(new StubYouTubeChatDiagnostics());
            services.AddSingleton<IYouTubeQuotaGovernor>(new StubYouTubeQuotaGovernor());
        }

        services.AddDiagnostics();
        services.AddSingleton<BotConnectionManager>();
        services.AddSingleton<IBotConnectionController>(sp => sp.GetRequiredService<BotConnectionManager>());

        return services.BuildServiceProvider(new ServiceProviderOptions
        {
            ValidateOnBuild = true,
            ValidateScopes = true,
        });
    }

    private sealed class StubYouTubeMonitorMissingBotAuth : IYouTubeMonitorDiagnostics
    {
        public YouTubeMonitoringStatus CaptureDiagnostics() =>
            new(
                Enabled: true,
                StreamStatus.Unknown,
                YouTubeMonitorIssue.MissingBotAuth,
                "UCtarget",
                null,
                null,
                0,
                DateTimeOffset.UtcNow,
                0);
    }

    private sealed class StubYouTubeMonitorAccessUnverified : IYouTubeMonitorDiagnostics
    {
        public YouTubeMonitoringStatus CaptureDiagnostics() =>
            new(
                Enabled: true,
                StreamStatus.Unknown,
                YouTubeMonitorIssue.AccessUnverified,
                "UCtarget",
                null,
                null,
                0,
                DateTimeOffset.UtcNow,
                0);
    }

    private sealed class StubYouTubeMonitorDiagnostics : IYouTubeMonitorDiagnostics
    {
        public YouTubeMonitoringStatus CaptureDiagnostics() =>
            new(
                Enabled: true,
                StreamStatus.Online,
                YouTubeMonitorIssue.MultipleActiveBroadcasts,
                "UCtarget",
                "vid-1",
                "chat-1",
                2,
                DateTimeOffset.UtcNow,
                0);
    }

    private sealed class StubYouTubeChatDiagnostics : IYouTubeChatDiagnostics
    {
        public YouTubeChatStatus CaptureDiagnostics() =>
            new(
                ConnectionState.Connected,
                YouTubeChatTransport.GrpcStream,
                ChatReadIssue.None,
                "@creator",
                "chat-1",
                DateTimeOffset.UtcNow.AddMinutes(-5),
                DateTimeOffset.UtcNow.AddSeconds(-2),
                DateTimeOffset.UtcNow.AddSeconds(-1),
                42,
                1,
                0);
    }

    private sealed class StubYouTubeQuotaGovernor : IYouTubeQuotaGovernor
    {
        public bool CanAfford(YouTubeQuotaPriority priority, int cost) => false;

        public bool TryReserve(
            PlatformAuthRole role,
            YouTubeQuotaPriority priority,
            int cost,
            out YouTubeQuotaLease lease,
            YouTubeQuotaCategory category = YouTubeQuotaCategory.Other)
        {
            lease = default!;
            return false;
        }

        public void MarkExhaustedFromServer(PlatformAuthRole role)
        {
        }

        public YouTubeQuotaStatus CaptureDiagnostics() =>
            new(
                "proj-1",
                new DateOnly(2026, 11, 15),
                DateTimeOffset.UtcNow.AddHours(3),
                10_000,
                125,
                10,
                25,
                100,
                7_500,
                ExhaustedByServer: false,
                YouTubeQuotaReliability.Reliable,
                CanWriteFile: false);
    }
}
