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
using PoproshaykaBot.Core.Obs;
using PoproshaykaBot.Core.Platforms.Auth;
using PoproshaykaBot.Core.Polls;
using PoproshaykaBot.Core.Server;
using PoproshaykaBot.Core.Settings;
using PoproshaykaBot.Core.Statistics;
using PoproshaykaBot.Core.Streaming;
using PoproshaykaBot.Core.Twitch;
using PoproshaykaBot.Core.Twitch.GameArt;
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
            out YouTubeQuotaLease lease)
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
