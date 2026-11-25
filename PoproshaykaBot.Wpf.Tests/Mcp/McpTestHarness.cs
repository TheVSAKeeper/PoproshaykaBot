using KeepShell.Bootstrap;
using KeepShell.Diagnostics;
using KeepShell.Mcp;
using KeepShell.Services.Platform;
using KeepShell.Testing;
using KeepShell.ViewModels;
using MahApps.Metro.IconPacks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using ModelContextProtocol;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using PoproshaykaBot.Core.Broadcast;
using PoproshaykaBot.Core.Chat;
using PoproshaykaBot.Core.Chat.Commands;
using PoproshaykaBot.Core.Debugging;
using PoproshaykaBot.Core.Diagnostics;
using PoproshaykaBot.Core.Infrastructure.Di;
using PoproshaykaBot.Core.Infrastructure.Diagnostics;
using PoproshaykaBot.Core.Infrastructure.Events;
using PoproshaykaBot.Core.Infrastructure.Events.Lifecycle;
using PoproshaykaBot.Core.Infrastructure.Hosting;
using PoproshaykaBot.Core.Infrastructure.Logging;
using PoproshaykaBot.Core.Obs;
using PoproshaykaBot.Core.Platforms;
using PoproshaykaBot.Core.Platforms.Auth;
using PoproshaykaBot.Core.Polls;
using PoproshaykaBot.Core.Server;
using PoproshaykaBot.Core.Settings;
using PoproshaykaBot.Core.Settings.Obs;
using PoproshaykaBot.Core.Settings.Onboarding;
using PoproshaykaBot.Core.Settings.Stores;
using PoproshaykaBot.Core.Statistics;
using PoproshaykaBot.Core.Streaming;
using PoproshaykaBot.Core.Twitch;
using PoproshaykaBot.Core.Twitch.GameArt;
using PoproshaykaBot.Core.Update;
using PoproshaykaBot.Core.Users.Avatars;
using PoproshaykaBot.Core.YouTube;
using PoproshaykaBot.Wpf.Bootstrap;
using PoproshaykaBot.Wpf.Infrastructure;
using PoproshaykaBot.Wpf.Infrastructure.Diagnostics;
using PoproshaykaBot.Wpf.Mcp;
using PoproshaykaBot.Wpf.ViewModels;
using System.Globalization;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using KeepShellMcpOptions = KeepShell.Mcp.McpServerOptions;

namespace PoproshaykaBot.Wpf.Tests.Mcp;

internal sealed class McpTestHarness : IAsyncDisposable
{
    public const string DefaultToken = "mcp-test-token-a1b2c3d4e5f60718293a4b5c";

    private static readonly TimeSpan StartTimeout = TimeSpan.FromSeconds(30);

    private readonly ServiceProvider _coreProvider;
    private readonly PerformanceMonitor _performanceMonitor;
    private readonly ChatHistoryManager _chatHistoryManager;
    private readonly List<IAsyncDisposable> _disposables = [];
    private bool _disposed;

    private McpTestHarness(
        string tempDirectory,
        string logsDirectory,
        string discoveryFilePath,
        McpServerHost host,
        McpPreferences preferences,
        KeepShellMcpOptions options,
        BotAutomation botAutomation,
        ShellAutomation shellAutomation,
        McpDispatch dispatch,
        WpfLogSink logSink,
        SettingsManager settingsManager,
        AccountsStore accountsStore,
        YouTubeAccountsStore youTubeAccountsStore,
        ObsIntegrationStore obsIntegrationStore,
        ChannelCatalogStore channelCatalogStore,
        IBotConnectionController connectionController,
        FakeOnboardingWizardLauncher onboardingWizard,
        ServiceProvider coreProvider,
        PerformanceMonitor performanceMonitor,
        ChatHistoryManager chatHistoryManager)
    {
        TempDirectory = tempDirectory;
        LogsDirectory = logsDirectory;
        DiscoveryFilePath = discoveryFilePath;
        Host = host;
        Preferences = preferences;
        Options = options;
        BotAutomation = botAutomation;
        ShellAutomation = shellAutomation;
        Dispatch = dispatch;
        LogSink = logSink;
        SettingsManager = settingsManager;
        AccountsStore = accountsStore;
        YouTubeAccountsStore = youTubeAccountsStore;
        ObsIntegrationStore = obsIntegrationStore;
        ChannelCatalogStore = channelCatalogStore;
        ConnectionController = connectionController;
        OnboardingWizard = onboardingWizard;
        _coreProvider = coreProvider;
        _performanceMonitor = performanceMonitor;
        _chatHistoryManager = chatHistoryManager;
    }

    public string TempDirectory { get; }

    public string LogsDirectory { get; }

    public string DiscoveryFilePath { get; }

    public McpServerHost Host { get; }

    public McpPreferences Preferences { get; }

    public KeepShellMcpOptions Options { get; }

    public BotAutomation BotAutomation { get; }

    public ShellAutomation ShellAutomation { get; }

    public McpDispatch Dispatch { get; }

    public WpfLogSink LogSink { get; }

    public SettingsManager SettingsManager { get; }

    public AccountsStore AccountsStore { get; }

    public YouTubeAccountsStore YouTubeAccountsStore { get; }

    public ObsIntegrationStore ObsIntegrationStore { get; }

    public ChannelCatalogStore ChannelCatalogStore { get; }

    public IBotConnectionController ConnectionController { get; }

    public FakeOnboardingWizardLauncher OnboardingWizard { get; }

    public string Endpoint => Host.Endpoint ?? throw new InvalidOperationException("MCP-сервер не запущен.");

    public int BoundPort => Host.BoundPort ?? throw new InvalidOperationException("MCP-сервер не привязан к порту.");

    public string Token => Preferences.EffectiveToken;

    public static async Task<McpTestHarness> StartAsync(
        bool allowMutations = false,
        string? token = null,
        int? port = 0,
        bool useRuntimeOverrides = true,
        bool isElevated = false,
        string serverName = "poproshaykabot",
        IBotConnectionController? connectionController = null,
        IStreamStatus? streamStatus = null,
        McpDispatch? dispatch = null,
        TimeSpan? shutdownTimeout = null,
        IReadOnlyList<Type>? extraToolTypes = null,
        Action<IServiceCollection>? configureExtraTools = null,
        Action<SettingsManager, AccountsStore, YouTubeAccountsStore, ObsIntegrationStore>? seedStores = null,
        bool startServer = true,
        UnhandledExceptionJournal? unhandledExceptionJournal = null,
        CancellationToken cancellationToken = default)
    {
        var tempDirectory = Path.Combine(Path.GetTempPath(), $"PoproshaykaBot-mcp-test-{Guid.NewGuid():N}");
        var logsDirectory = Path.Combine(tempDirectory, "logs");
        var shotsDirectory = Path.Combine(tempDirectory, "shots");
        var settingsDirectory = Path.Combine(tempDirectory, "settings");
        var discoveryFilePath = Path.Combine(tempDirectory, "mcp-discovery.json");

        Directory.CreateDirectory(logsDirectory);
        Directory.CreateDirectory(shotsDirectory);
        Directory.CreateDirectory(settingsDirectory);

        var effectiveToken = token ?? DefaultToken;
        var coreProvider = BuildCoreDiagnosticsProvider();

        var eventBus = new InMemoryEventBus(NullLogger<InMemoryEventBus>.Instance);
        var settingsManager = new SettingsManager(NullLogger<SettingsManager>.Instance, Path.Combine(settingsDirectory, "settings.json"));
        var accountsStore = new AccountsStore(filePath: Path.Combine(settingsDirectory, "accounts.json"));
        var youTubeAccountsStore = new YouTubeAccountsStore(filePath: Path.Combine(settingsDirectory, "youtube-accounts.json"));
        var obsIntegrationStore = new ObsIntegrationStore(eventBus, filePath: Path.Combine(settingsDirectory, "obs-integration.json"));
        var channelCatalogStore = new ChannelCatalogStore(NullLogger<ChannelCatalogStore>.Instance, Path.Combine(settingsDirectory, "channels.json"));
        var commandSettingsStore = new CommandSettingsStore(NullLogger<CommandSettingsStore>.Instance, Path.Combine(settingsDirectory, "commands.json"));

        seedStores?.Invoke(settingsManager, accountsStore, youTubeAccountsStore, obsIntegrationStore);

        var options = new KeepShellMcpOptions
        {
            ServerName = serverName,
            AppName = AppInfo.Name,
            AppVersion = AppInfo.Version,
            DefaultPort = 7656,
            ShotsDirectory = shotsDirectory,
            DiskLogDirectory = logsDirectory,
            DiskLogGlobs = [AppInfo.LogFileGlob],
            DiscoveryFilePath = discoveryFilePath,
            ShutdownTimeout = shutdownTimeout ?? TimeSpan.FromSeconds(2),
            ToolTypes = [typeof(PoproshaykaBotTools), .. extraToolTypes ?? []],
            ConfigureTools = null,
        };

        var uiSettings = new MemorySettings();
        var preferences = new McpPreferences(uiSettings, options, isElevated);

        if (useRuntimeOverrides)
        {
            preferences.ApplyRuntimeOverrides(
                enabled: true,
                port: port ?? 0,
                token: effectiveToken,
                allowMutations: allowMutations);
        }
        else
        {
            preferences.Token = effectiveToken;
            preferences.AllowMutations = allowMutations;

            if (port is > 0)
            {
                preferences.Port = port.Value;
            }

            preferences.Enabled = true;
        }

        var secretProviders = CreateSecretProviders(settingsManager, accountsStore, youTubeAccountsStore, obsIntegrationStore, preferences);
        var secretSource = new BotDiagnosticsSecretSource(secretProviders, NullLogger<BotDiagnosticsSecretSource>.Instance);
        IDiagnosticsSecretSource[] secretSources = [secretSource];

        var diagnosticsOptions = new DiagnosticsOptions
        {
            AppName = AppInfo.Name,
            AppVersion = AppInfo.Version,
            DataDirectory = tempDirectory,
            BundleLogFiles = 3,
            BundleLogTailLines = 2000,
            BundleHitchRows = 100,
        };
        var loggingOptions = new KeepShellLoggingOptions
        {
            LogsDirectory = logsDirectory,
            FileNamePrefix = AppInfo.LogFilePrefix,
        };
        var performanceMonitor = new PerformanceMonitor(diagnosticsOptions, new ManualUiDispatcher(), NullLogger<PerformanceMonitor>.Instance);
        var performanceFormat = new PerformanceFormat(diagnosticsOptions);
        var diagnosticsSource = coreProvider.GetRequiredService<DiagnosticsSnapshotSource>();

        var snapshotContributors = new ISettingsSnapshotContributor[]
        {
            new DelegateSettingsSnapshotContributor(() =>
            [
                new(
                    "Twitch",
                    $"канал {settingsManager.Current.Twitch.Channel}, порт HTTP {settingsManager.Current.Twitch.HttpServerPort}"),
            ]),
        };
        var bundleSource = new BotDiagnosticsBundleSource(
            snapshotContributors,
            diagnosticsSource,
            NullLogger<BotDiagnosticsBundleSource>.Instance);
        var diagnosticsCollector = new DiagnosticsCollector(
            performanceMonitor,
            performanceFormat,
            diagnosticsOptions,
            loggingOptions,
            [bundleSource],
            secretSources,
            [],
            NullLogger<DiagnosticsCollector>.Instance);

        var controller = connectionController ?? new FakeBotConnectionController();
        var onboardingWizard = new FakeOnboardingWizardLauncher();
        var chatHistoryManager = new ChatHistoryManager(settingsManager, eventBus);
        var commandProcessor = new ChatCommandProcessor(
            [new FakeHelpCommand()],
            commandSettingsStore,
            new CommandUsageRepository(TimeProvider.System),
            NullLogger<ChatCommandProcessor>.Instance);
        var onboardingChecklist = new OnboardingChecklist(
            settingsManager,
            accountsStore,
            channelCatalogStore,
            youtubeAccounts: youTubeAccountsStore);

        var botAutomation = new BotAutomation(
            controller,
            new FakeTargetChannelProvider(new("streamer", "streamer", false, true)),
            streamStatus ?? new FakeStreamStatus(StreamStatus.Offline, null),
            new FakeHttpServerStatus(false, null),
            new FakeObsSceneController(ObsConnectionSnapshot.Disconnected()),
            settingsManager,
            NullLogger<BotAutomation>.Instance,
            diagnosticsSource: diagnosticsSource,
            channelCatalog: channelCatalogStore,
            onboardingWizard: onboardingWizard,
            connectNotificationGate: new McpConnectNotificationGate(),
            settingsSnapshotContributors: snapshotContributors,
            onboardingChecklist: onboardingChecklist,
            accountDirectories: [accountsStore, youTubeAccountsStore],
            chatHistoryManager: chatHistoryManager,
            commandProcessor: commandProcessor,
            commandSettingsStore: commandSettingsStore,
            obsIntegrationStore: obsIntegrationStore,
            diagnosticsCollector: diagnosticsCollector);

        var hostOptions = new KeepShellMcpOptions
        {
            ServerName = options.ServerName,
            AppName = options.AppName,
            AppVersion = options.AppVersion,
            DefaultPort = options.DefaultPort,
            ShotsDirectory = options.ShotsDirectory,
            DiskLogDirectory = options.DiskLogDirectory,
            DiskLogGlobs = options.DiskLogGlobs,
            DiscoveryFilePath = options.DiscoveryFilePath,
            ShutdownTimeout = options.ShutdownTimeout,
            ToolTypes = options.ToolTypes,
            ConfigureTools = (_, tools) =>
            {
                tools.AddSingleton(botAutomation);
                configureExtraTools?.Invoke(tools);
            },
        };

        var logSink = new WpfLogSink(500);
        var effectiveDispatch = dispatch ?? new McpDispatch(TimeSpan.FromSeconds(5));
        var shell = new FakeShellViewModel();
        var shellAutomation = new ShellAutomation(
            new Lazy<ShellViewModelBase>(() => shell),
            logSink,
            new FakeApplicationLifetime(),
            preferences,
            hostOptions,
            effectiveDispatch,
            NullLogger<ShellAutomation>.Instance,
            unhandledExceptionJournal: unhandledExceptionJournal,
            performanceMonitor: performanceMonitor,
            diagnosticsCollector: diagnosticsCollector,
            secretSources: secretSources);

        var host = new McpServerHost(
            preferences,
            shellAutomation,
            effectiveDispatch,
            hostOptions,
            new EmptyServiceProvider(),
            NullLoggerFactory.Instance,
            NullLogger<McpServerHost>.Instance);

        var harness = new McpTestHarness(
            tempDirectory,
            logsDirectory,
            discoveryFilePath,
            host,
            preferences,
            hostOptions,
            botAutomation,
            shellAutomation,
            effectiveDispatch,
            logSink,
            settingsManager,
            accountsStore,
            youTubeAccountsStore,
            obsIntegrationStore,
            channelCatalogStore,
            controller,
            onboardingWizard,
            coreProvider,
            performanceMonitor,
            chatHistoryManager);

        if (startServer)
        {
            try
            {
                await harness.StartServerAsync(cancellationToken);
            }
            catch
            {
                await harness.DisposeAsync();
                throw;
            }
        }

        return harness;
    }

    public async Task StartServerAsync(CancellationToken cancellationToken = default)
    {
        Host.Apply();

        var started = await WaitUntilAsync(
            () => Host.IsRunning || (Host.LastError is not null && !Host.IsStarting),
            StartTimeout,
            cancellationToken);

        if (!started)
        {
            throw new AssertionException($"MCP-сервер не поднялся за {StartTimeout.TotalSeconds:0} с.");
        }

        if (!Host.IsRunning)
        {
            throw new AssertionException($"MCP-сервер не поднялся: {Host.LastError}");
        }
    }

    public Task<McpClient> ConnectClientAsync(string? tokenOverride = null, CancellationToken cancellationToken = default)
    {
        return ConnectClientWithHeadersAsync(
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["Authorization"] = $"Bearer {tokenOverride ?? Token}",
            },
            cancellationToken);
    }

    public Task<McpClient> ConnectClientWithHeaderAsync(
        string headerName,
        string headerValue,
        CancellationToken cancellationToken = default)
    {
        return ConnectClientWithHeadersAsync(
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                [headerName] = headerValue,
            },
            cancellationToken);
    }

    public async Task<McpClient> ConnectClientWithHeadersAsync(
        Dictionary<string, string> headers,
        CancellationToken cancellationToken = default)
    {
        var transport = new HttpClientTransport(new HttpClientTransportOptions
        {
            Endpoint = new(Endpoint),
            TransportMode = HttpTransportMode.StreamableHttp,
            AdditionalHeaders = headers,
        });

        try
        {
            var client = await McpClient.CreateAsync(transport, null, null, cancellationToken);
            _disposables.Add(transport);
            return client;
        }
        catch
        {
            await transport.DisposeAsync();
            throw;
        }
    }

    public static async Task<JsonDocument> CallToolJsonAsync(
        McpClient client,
        string name,
        Dictionary<string, object?>? args = null,
        CancellationToken cancellationToken = default)
    {
        var result = await client.CallToolAsync(name, args, null, null, cancellationToken);
        var text = ExtractText(result);

        Assert.That(result.IsError, Is.Not.True, $"Инструмент {name} вернул ошибку: {text}");
        Assert.That(text, Is.Not.Null.And.Not.Empty, $"Инструмент {name} вернул пустой ответ.");

        return JsonDocument.Parse(text);
    }

    public static async Task<(bool IsError, string Text)> CallToolExpectResultAsync(
        McpClient client,
        string name,
        Dictionary<string, object?>? args = null,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var result = await client.CallToolAsync(name, args, null, null, cancellationToken);
            return (result.IsError == true, ExtractText(result));
        }
        catch (McpException exception)
        {
            return (true, exception.Message);
        }
    }

    public static string ExtractText(CallToolResult result)
    {
        return string.Join(
            Environment.NewLine,
            result.Content.OfType<TextContentBlock>().Select(static block => block.Text));
    }

    public static HttpClient CreateRawHttpClient()
    {
        return new()
        {
            Timeout = TimeSpan.FromSeconds(10),
        };
    }

    public static StringContent CreateListToolsPayload()
    {
        return new("{\"jsonrpc\":\"2.0\",\"id\":1,\"method\":\"tools/list\"}", Encoding.UTF8, "application/json");
    }

    public static async Task<int> SendRawHttpStatusAsync(int port, string rawRequest, CancellationToken cancellationToken = default)
    {
        using var tcp = new TcpClient();
        await tcp.ConnectAsync(IPAddress.Loopback, port, cancellationToken);
        await using var stream = tcp.GetStream();

        var bytes = Encoding.ASCII.GetBytes(rawRequest);
        await stream.WriteAsync(bytes.AsMemory(), cancellationToken);
        await stream.FlushAsync(cancellationToken);

        using var reader = new StreamReader(stream, Encoding.ASCII);
        var statusLine = await reader.ReadLineAsync(cancellationToken);

        Assert.That(statusLine, Is.Not.Null.And.StartsWith("HTTP/1."));

        var parts = statusLine!.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return int.Parse(parts[1], CultureInfo.InvariantCulture);
    }

    public static int AllocateFreePort()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();

        return port;
    }

    public static async Task<bool> WaitUntilAsync(
        Func<bool> predicate,
        TimeSpan timeout,
        CancellationToken cancellationToken = default)
    {
        var deadline = DateTime.UtcNow + timeout;

        while (DateTime.UtcNow < deadline)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (predicate())
            {
                return true;
            }

            await Task.Delay(25, cancellationToken);
        }

        return predicate();
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        foreach (var disposable in _disposables)
        {
            await disposable.DisposeAsync();
        }

        _disposables.Clear();
        await Host.DisposeAsync();
        _chatHistoryManager.Dispose();
        _performanceMonitor.Dispose();
        await _coreProvider.DisposeAsync();

        if (Directory.Exists(TempDirectory))
        {
            try
            {
                Directory.Delete(TempDirectory, recursive: true);
            }
            catch (IOException)
            {
                await TestContext.Progress.WriteLineAsync($"Не удалось удалить временный каталог теста {TempDirectory}");
            }
        }
    }

    private static ServiceProvider BuildCoreDiagnosticsProvider()
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
            .AddSelfUpdate()
            .AddDiagnostics();

        services.AddSingleton<BotConnectionManager>();
        services.AddSingleton<IBotConnectionController>(static provider => provider.GetRequiredService<BotConnectionManager>());

        return services.BuildServiceProvider(new ServiceProviderOptions
        {
            ValidateOnBuild = true,
            ValidateScopes = true,
        });
    }

    private static IReadOnlyList<ISecretValueProvider> CreateSecretProviders(
        SettingsManager settings,
        AccountsStore accounts,
        YouTubeAccountsStore youTubeAccounts,
        ObsIntegrationStore obs,
        McpPreferences mcpPreferences)
    {
        var providers = new List<ISecretValueProvider>
        {
            new DelegateSecretValueProvider("настройки Twitch", () => [settings.Current.Twitch.ClientSecret]),
            new DelegateSecretValueProvider("настройки YouTube", () => [youTubeAccounts.LoadClientSecret()]),
            new DelegateSecretValueProvider("настройки OBS", () => [obs.Load().Password]),
            new DelegateSecretValueProvider("настройки MCP", () => [mcpPreferences.Token, mcpPreferences.EffectiveToken]),
        };

        foreach (var role in PlatformAuthRoles.Active)
        {
            providers.Add(new DelegateSecretValueProvider($"токены роли {role}", () =>
            {
                var account = accounts.Load(role);
                return [account.AccessToken, account.RefreshToken];
            }));

            providers.Add(new DelegateSecretValueProvider($"токены YouTube роли {role}", () =>
            {
                var account = youTubeAccounts.Load(role);
                return [account.AccessToken, account.RefreshToken];
            }));
        }

        return providers;
    }

    internal sealed class FakeBotConnectionController : IBotConnectionController
    {
        public bool IsBusy => CurrentPhase is BotLifecyclePhase.Connecting or BotLifecyclePhase.Disconnecting;

        public BotLifecyclePhase CurrentPhase { get; set; } = BotLifecyclePhase.Idle;

        public int StartCalls { get; private set; }

        public int StopCalls { get; private set; }

        public Func<BotStopMode, Task>? StopHandler { get; set; }

        public bool IsPlatformBusy(StreamingPlatform platform) => IsBusy;

        public BotLifecyclePhase GetPhase(StreamingPlatform platform) => CurrentPhase;

        public void StartConnection()
        {
            StartCalls++;
            CurrentPhase = BotLifecyclePhase.Connecting;
        }

        public void StartConnection(StreamingPlatform platform) => StartConnection();

        public void CancelConnection()
        {
            CurrentPhase = BotLifecyclePhase.Cancelled;
        }

        public void CancelConnection(StreamingPlatform platform) => CancelConnection();

        public Task WaitForConnectionAsync() => Task.CompletedTask;

        public Task WaitForConnectionAsync(StreamingPlatform platform) => WaitForConnectionAsync();

        public Task StopAsync(BotStopMode mode = BotStopMode.Graceful)
        {
            StopCalls++;

            if (StopHandler is not null)
            {
                return StopHandler(mode);
            }

            CurrentPhase = BotLifecyclePhase.Disconnected;
            return Task.CompletedTask;
        }

        public Task StopAsync(StreamingPlatform platform, BotStopMode mode = BotStopMode.Graceful) => StopAsync(mode);
    }

    internal sealed class FakeOnboardingWizardLauncher : IOnboardingWizardLauncher
    {
        public event EventHandler? Closed;

        public bool HasBeenShown { get; private set; }

        public bool IsOpen { get; set; }

        public void Show()
        {
            HasBeenShown = true;
            IsOpen = true;
        }

        public void Close()
        {
            IsOpen = false;
            Closed?.Invoke(this, EventArgs.Empty);
        }
    }

    internal sealed class FakeStreamStatus(StreamStatus status, StreamInfo? stream) : IStreamStatus
    {
        public StreamStatus CurrentStatus { get; } = status;

        public StreamInfo? CurrentStream { get; } = stream;

        public StreamStatusSnapshot GetSnapshot() => new(CurrentStatus, CurrentStream, null);

        public Task RefreshLiveSnapshotAsync() => Task.CompletedTask;
    }

    private sealed class FakeTargetChannelProvider(TargetChannelState current) : ITargetChannelProvider
    {
        public TargetChannelState Current { get; } = current;

        public void BeginSession()
        {
        }

        public void EndSession()
        {
        }
    }

    private sealed class FakeHttpServerStatus(bool isRunning, int? port) : IHttpServerStatus
    {
        public bool IsRunning { get; } = isRunning;

        public int? Port { get; } = port;
    }

    private sealed class FakeObsSceneController(ObsConnectionSnapshot status) : IObsSceneController
    {
        public bool IsConnected => CurrentStatus.IsConnected;

        public ObsConnectionSnapshot CurrentStatus { get; } = status;

        public Task<string?> GetCurrentSceneAsync(CancellationToken cancellationToken) => Task.FromResult<string?>(null);

        public Task SetCurrentSceneAsync(string sceneName, CancellationToken cancellationToken) => Task.CompletedTask;
    }

    private sealed class FakeHelpCommand : IChatCommand
    {
        public string Canonical => "помощь";

        public IReadOnlyCollection<string> Aliases { get; } = ["help"];

        public string Description => "Справка по командам";

        public bool CanExecute(CommandContext context) => true;

        public Task<OutgoingMessage?> ExecuteAsync(CommandContext context, CancellationToken cancellationToken)
            => Task.FromResult<OutgoingMessage?>(null);
    }

    private sealed class FakeShellViewModel : ShellViewModelBase
    {
        public FakeShellViewModel()
            : base(new ModalHostViewModel())
        {
            Sections.Add(new NavigationItem("Обзор", PackIconLucideKind.House, key: "overview"));
            Settings = new("Настройки", PackIconLucideKind.Settings, key: "settings");
        }

        public NavigationItem Settings { get; }

        protected override IEnumerable<NavigationItem> GetExtraNavigationItems()
        {
            yield return Settings;
        }

        protected override void NavigateToSettings()
        {
            Selected = Settings;
        }
    }

    private sealed class FakeApplicationLifetime : IApplicationLifetime
    {
        public bool IsMainWindowMinimized => false;

        public void Shutdown()
        {
        }
    }

    private sealed class EmptyServiceProvider : IServiceProvider
    {
        public object? GetService(Type serviceType) => null;
    }
}
