using Microsoft.Extensions.Logging.Abstractions;
using PoproshaykaBot.Core.Broadcast;
using PoproshaykaBot.Core.Debugging;
using PoproshaykaBot.Core.Infrastructure.Events;
using PoproshaykaBot.Core.Infrastructure.Events.Lifecycle;
using PoproshaykaBot.Core.Infrastructure.Events.Streaming;
using PoproshaykaBot.Core.Infrastructure.Hosting;
using PoproshaykaBot.Core.Platforms;
using PoproshaykaBot.Core.Streaming;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;

namespace PoproshaykaBot.Wpf.Tests.Mcp;

[TestFixture]
[Category("Mcp")]
[NonParallelizable]
public sealed class McpLifecycleScenarioTests
{
    private static readonly TimeSpan StateTimeout = TimeSpan.FromSeconds(15);

    [Test]
    public async Task Scenario1_OccupiedPortSetsLastError_AndRecoversAfterRelease()
    {
        var blocker = new TcpListener(IPAddress.Loopback, 0);
        blocker.Start();
        var occupiedPort = ((IPEndPoint)blocker.LocalEndpoint).Port;

        try
        {
            await using var harness = await McpTestHarness.StartAsync(
                allowMutations: false,
                port: occupiedPort,
                useRuntimeOverrides: false,
                startServer: false);

            harness.Host.Apply();

            var failed = await McpTestHarness.WaitUntilAsync(
                () => !harness.Host.IsStarting && harness.Host.LastError is not null,
                StateTimeout);

            using (Assert.EnterMultipleScope())
            {
                Assert.That(failed, Is.True, "При занятом порте сервер должен завершить попытку старта с LastError.");
                Assert.That(harness.Host.IsRunning, Is.False);
                Assert.That(harness.Host.LastError, Is.Not.Null.And.Not.Empty);
            }

            blocker.Stop();
            harness.Host.Apply();

            var recovered = await McpTestHarness.WaitUntilAsync(
                () => harness.Host.IsRunning && !harness.Host.IsStarting && harness.Host.LastError is null,
                StateTimeout);

            Assert.That(recovered, Is.True, $"После освобождения порта сервер обязан подняться: {harness.Host.LastError}");

            await using var client = await harness.ConnectClientAsync();
            var tools = await client.ListToolsAsync();

            using (Assert.EnterMultipleScope())
            {
                Assert.That(harness.Host.IsRunning, Is.True);
                Assert.That(harness.Host.BoundPort, Is.EqualTo(occupiedPort));
                Assert.That(tools, Is.Not.Empty);
            }
        }
        finally
        {
            blocker.Stop();
        }
    }

    [Test]
    public async Task Scenario2_LiveTokenAndPortRotation_UpdatesAccessAndEndpoint()
    {
        var initialPort = McpTestHarness.AllocateFreePort();
        const string initialToken = "mcp-rotation-initial-token-11111111";
        const string rotatedToken = "mcp-rotation-updated-token-22222222";

        await using var harness = await McpTestHarness.StartAsync(
            allowMutations: false,
            token: initialToken,
            port: initialPort,
            useRuntimeOverrides: false);

        await using (var initialClient = await harness.ConnectClientAsync(initialToken))
        {
            using var initialState = await McpTestHarness.CallToolJsonAsync(initialClient, "get_bot_state");
            Assert.That(initialState.RootElement.TryGetProperty("phase", out _), Is.True);
        }

        var oldEndpoint = harness.Endpoint;
        harness.Preferences.Token = rotatedToken;

        using var http = McpTestHarness.CreateRawHttpClient();
        using var oldTokenRequest = new HttpRequestMessage(HttpMethod.Post, oldEndpoint)
        {
            Content = McpTestHarness.CreateListToolsPayload(),
        };
        oldTokenRequest.Headers.Add("Authorization", $"Bearer {initialToken}");
        using var oldTokenResponse = await http.SendAsync(oldTokenRequest);

        await using (var rotatedTokenClient = await harness.ConnectClientAsync(rotatedToken))
        {
            using var stateAfterToken = await McpTestHarness.CallToolJsonAsync(rotatedTokenClient, "get_bot_state");
            using (Assert.EnterMultipleScope())
            {
                Assert.That(oldTokenResponse.StatusCode, Is.EqualTo(HttpStatusCode.Unauthorized));
                Assert.That(harness.BoundPort, Is.EqualTo(initialPort));
                Assert.That(stateAfterToken.RootElement.TryGetProperty("phase", out _), Is.True);
            }
        }

        var nextPort = McpTestHarness.AllocateFreePort();
        while (nextPort == initialPort)
        {
            nextPort = McpTestHarness.AllocateFreePort();
        }

        harness.Preferences.Port = nextPort;

        var moved = await McpTestHarness.WaitUntilAsync(
            () => harness.Host.IsRunning && !harness.Host.IsStarting && harness.Host.BoundPort == nextPort,
            StateTimeout);

        Assert.That(moved, Is.True, $"Сервер должен переехать на порт {nextPort}: {harness.Host.LastError}");

        using var oldPortRequest = new HttpRequestMessage(HttpMethod.Post, oldEndpoint)
        {
            Content = McpTestHarness.CreateListToolsPayload(),
        };
        oldPortRequest.Headers.Add("Authorization", $"Bearer {rotatedToken}");

        HttpRequestException? oldPortException = null;
        try
        {
            using var unexpected = await http.SendAsync(oldPortRequest);
            Assert.Fail($"Старый порт {initialPort} не должен отвечать после смены порта (получен статус {unexpected.StatusCode}).");
        }
        catch (HttpRequestException exception)
        {
            oldPortException = exception;
        }

        await using var nextPortClient = await harness.ConnectClientAsync(rotatedToken);
        using var stateAfterPort = await McpTestHarness.CallToolJsonAsync(nextPortClient, "get_bot_state");

        using (Assert.EnterMultipleScope())
        {
            Assert.That(oldPortException, Is.Not.Null);
            Assert.That(harness.BoundPort, Is.EqualTo(nextPort));
            Assert.That(stateAfterPort.RootElement.TryGetProperty("phase", out _), Is.True);
        }
    }

    [Test]
    public async Task Scenario4_DisconnectBot_DuringOnlineStreamWithAutoStop_DisconnectsCleanlyWithoutDoubleStop()
    {
        var eventBus = new InMemoryEventBus(NullLogger<InMemoryEventBus>.Instance);
        var sharedHost = new AppHost([], NullLogger<AppHost>.Instance);
        var component = new CountingHostedComponent();
        var platformHost = new PlatformHost(StreamingPlatform.Twitch, [component], NullLogger<PlatformHost>.Instance);
        var adapter = new StubPlatformConnectionAdapter(StreamingPlatform.Twitch, platformHost);

        await using var connectionManager = new BotConnectionManager(
            sharedHost,
            [adapter],
            chatPipelineHandler: null,
            eventBus,
            NullLogger<BotConnectionManager>.Instance);

        var offlineCoordinator = new ImmediateOfflineStopCoordinator();
        var settingsProvider = new StubAutomationSettingsProvider(new BotLifecycleAutomationSettings
        {
            AutoConnectOnStreamOnline = true,
            AutoDisconnectOnStreamOffline = true,
        });
        var targetProvider = new StaticTargetChannelProvider(new("streamer", "streamer", false, true));

        using var automationHandler = new BotLifecycleAutomationHandler(
            connectionManager,
            settingsProvider,
            offlineCoordinator,
            targetProvider,
            eventBus,
            NullLogger<BotLifecycleAutomationHandler>.Instance);

        var onlineStream = new StreamInfo
        {
            Title = "Онлайн-трансляция",
            GameName = "Just Chatting",
            ViewerCount = 120,
            StartedAt = DateTime.UtcNow,
        };

        await eventBus.PublishAsync(new StreamWentOnline(StreamingPlatform.Twitch, "streamer", onlineStream));

        var connected = await McpTestHarness.WaitUntilAsync(
            () => connectionManager.CurrentPhase == BotLifecyclePhase.Connected,
            StateTimeout);
        Assert.That(connected, Is.True, "Автоподключение при онлайн-стриме должно довести бота до фазы Connected.");

        await using var harness = await McpTestHarness.StartAsync(
            allowMutations: true,
            connectionController: connectionManager,
            streamStatus: new McpTestHarness.FakeStreamStatus(StreamStatus.Online, onlineStream));
        await using var client = await harness.ConnectClientAsync();

        using var disconnectDoc = await McpTestHarness.CallToolJsonAsync(client, "disconnect_bot");
        var phaseAfterDisconnect = disconnectDoc.RootElement.GetProperty("phase").GetString();

        await eventBus.PublishAsync(new StreamWentOffline(StreamingPlatform.Twitch, "streamer", IsCatchUp: false));

        using var stateDoc = await McpTestHarness.CallToolJsonAsync(client, "get_bot_state");

        using (Assert.EnterMultipleScope())
        {
            Assert.That(disconnectDoc.RootElement.GetProperty("accepted").GetBoolean(), Is.True);
            Assert.That(phaseAfterDisconnect, Is.EqualTo(nameof(BotLifecyclePhase.Disconnected)));
            Assert.That(connectionManager.CurrentPhase, Is.EqualTo(BotLifecyclePhase.Disconnected));
            Assert.That(stateDoc.RootElement.GetProperty("phase").GetString(), Is.EqualTo(nameof(BotLifecyclePhase.Disconnected)));
            Assert.That(component.StopCalls, Is.EqualTo(1), "Повторного останова компонентов быть не должно.");
            Assert.That(offlineCoordinator.ScheduledCount, Is.Zero, "После ручного отключения переход стрима в офлайн не должен планировать авто-останов.");
        }
    }

    [Test]
    public async Task Scenario5_DualHostCoexistence_IsolatesTokensAndServesConcurrentToolCalls()
    {
        const string tokenA = "dual-host-token-alpha-1234567890abcd";
        const string tokenB = "dual-host-token-bravo-0987654321dcba";

        await using var hostA = await McpTestHarness.StartAsync(
            allowMutations: false,
            token: tokenA,
            port: 0,
            serverName: "poproshaykabot");
        await using var hostB = await McpTestHarness.StartAsync(
            allowMutations: false,
            token: tokenB,
            port: 0,
            serverName: "mediaorcestrator");

        Assert.That(hostA.BoundPort, Is.Not.EqualTo(hostB.BoundPort));

        using var http = McpTestHarness.CreateRawHttpClient();

        using var crossReqAB = new HttpRequestMessage(HttpMethod.Post, hostA.Endpoint)
        {
            Content = McpTestHarness.CreateListToolsPayload(),
        };
        crossReqAB.Headers.Add("Authorization", $"Bearer {tokenB}");
        using var crossRespAB = await http.SendAsync(crossReqAB);

        using var crossReqBA = new HttpRequestMessage(HttpMethod.Post, hostB.Endpoint)
        {
            Content = McpTestHarness.CreateListToolsPayload(),
        };
        crossReqBA.Headers.Add("Authorization", $"Bearer {tokenA}");
        using var crossRespBA = await http.SendAsync(crossReqBA);

        await using var clientA = await hostA.ConnectClientAsync(tokenA);
        await using var clientB = await hostB.ConnectClientAsync(tokenB);

        var callATask = McpTestHarness.CallToolJsonAsync(clientA, "get_bot_state");
        var callBTask = McpTestHarness.CallToolJsonAsync(clientB, "get_bot_state");
        var docs = await Task.WhenAll(callATask, callBTask);

        using var docA = docs[0];
        using var docB = docs[1];

        using (Assert.EnterMultipleScope())
        {
            Assert.That(crossRespAB.StatusCode, Is.EqualTo(HttpStatusCode.Unauthorized));
            Assert.That(crossRespBA.StatusCode, Is.EqualTo(HttpStatusCode.Unauthorized));
            Assert.That(docA.RootElement.TryGetProperty("phase", out _), Is.True);
            Assert.That(docB.RootElement.TryGetProperty("phase", out _), Is.True);
        }
    }

    private sealed class CountingHostedComponent : IHostedComponent
    {
        public string Name => "CountingComponent";

        public int StartOrder => 0;

        public int StopCalls { get; private set; }

        public Task StartAsync(IProgress<string> progress, CancellationToken cancellationToken) => Task.CompletedTask;

        public Task StopAsync(IProgress<string> progress, CancellationToken cancellationToken)
        {
            StopCalls++;
            return Task.CompletedTask;
        }
    }

    private sealed class StubPlatformConnectionAdapter(StreamingPlatform platform, PlatformHost host) : IPlatformConnectionAdapter
    {
        public StreamingPlatform Platform { get; } = platform;

        public bool IsEnabled => true;

        public PlatformHost Host { get; } = host;

        public Task<string> BeginSessionAsync(IProgress<string> progress, CancellationToken cancellationToken)
            => Task.FromResult("streamer");

        public void EndSession()
        {
        }
    }

    private sealed class StubAutomationSettingsProvider(BotLifecycleAutomationSettings settings) : IBotLifecycleAutomationSettingsProvider
    {
        public BotLifecycleAutomationSettings Get(StreamingPlatform platform) => settings;
    }

    private sealed class ImmediateOfflineStopCoordinator : IBroadcastOfflineStopCoordinator
    {
        public int ScheduledCount { get; private set; }

        public void ScheduleOfflineStop(StreamingPlatform platform, Func<CancellationToken, Task> stopAction)
        {
            ScheduledCount++;
        }
    }

    private sealed class StaticTargetChannelProvider(TargetChannelState current) : ITargetChannelProvider
    {
        public TargetChannelState Current { get; } = current;

        public void BeginSession()
        {
        }

        public void EndSession()
        {
        }
    }
}
