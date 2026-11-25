using KeepShell.Diagnostics;
using KeepShell.Mcp;
using KeepShell.Bootstrap;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using PoproshaykaBot.Core.Broadcast;
using PoproshaykaBot.Core.Chat;
using PoproshaykaBot.Core.Debugging;
using PoproshaykaBot.Core.Diagnostics;
using PoproshaykaBot.Core.Infrastructure;
using PoproshaykaBot.Core.Infrastructure.Di;
using PoproshaykaBot.Core.Infrastructure.Diagnostics;
using PoproshaykaBot.Core.Infrastructure.Events;
using PoproshaykaBot.Core.Infrastructure.Hosting;
using PoproshaykaBot.Core.Infrastructure.Logging;
using PoproshaykaBot.Core.Infrastructure.Runtime;
using PoproshaykaBot.Core.Obs;
using PoproshaykaBot.Core.Platforms;
using PoproshaykaBot.Core.Polls;
using PoproshaykaBot.Core.Server;
using PoproshaykaBot.Core.Settings;
using PoproshaykaBot.Core.Settings.Migrations;
using PoproshaykaBot.Core.Statistics;
using PoproshaykaBot.Core.Streaming;
using PoproshaykaBot.Core.Twitch;
using PoproshaykaBot.Core.Twitch.GameArt;
using PoproshaykaBot.Core.Users.Avatars;
using PoproshaykaBot.Core.Update;
using PoproshaykaBot.Core.YouTube;
using PoproshaykaBot.Wpf.Bootstrap;
using PoproshaykaBot.Wpf.Infrastructure;
using PoproshaykaBot.Wpf.Infrastructure.Diagnostics;
using PoproshaykaBot.Wpf.Infrastructure.Platforms;
using PoproshaykaBot.Wpf.Mcp;
using PoproshaykaBot.Wpf.ViewModels;
using PoproshaykaBot.Wpf.ViewModels.Diagnostics;
using PoproshaykaBot.Wpf.ViewModels.Controls;
using PoproshaykaBot.Wpf.ViewModels.Dialogs;
using PoproshaykaBot.Wpf.ViewModels.Onboarding;
using PoproshaykaBot.Wpf.ViewModels.Settings;
using PoproshaykaBot.Wpf.ViewModels.Tiles;
using PoproshaykaBot.Wpf.Views;
using PoproshaykaBot.Wpf.Views.Onboarding;
using Serilog;
using Serilog.Debugging;
using Serilog.Events;
using Serilog.Extensions.Logging;
using System.Diagnostics;
using System.Globalization;
using System.Windows;
using System.Windows.Markup;
using System.Windows.Threading;

namespace PoproshaykaBot.Wpf;

public partial class App : Application
{
    private const string OutputTemplate = "[{Timestamp:HH:mm:ss.fff} {Level:u3}] [{SourceContext}] {Message:lj}{NewLine}{Exception}";
    private const string SessionStartLine = AppInfo.SessionStartMarker + "...";

    private static int _fatalErrorHandled;
    private static int _restartRequested;
    private static RunStateStore? _runStateStore;
    private static readonly UnobservedTaskExceptionTracker UnobservedTaskExceptions = new();

    private readonly UnhandledExceptionJournal _unhandledExceptionJournal = new();
    private ServiceProvider? _services;
    private KeepShellLogging? _logging;
    private KeepShellLoggingOptions? _loggingOptions;
    private AppLifetime? _appLifetime;
    private KestrelHttpServer? _kestrelServer;
    private readonly List<StreamMonitoringHost> _startedStreamMonitoringHosts = [];
    private MemoryWatchdog? _memoryWatchdog;
    private BindingErrorSink? _bindingErrors;
    private Mutex? _singleInstanceMutex;
    private PreviousRunState _previousRunState = PreviousRunState.Clean;

    private bool _isFinalizeUpdate;
    private string[]? _galleryArgs;
    private GalleryWorkspace? _galleryWorkspace;
    private GalleryArguments? _galleryArguments;
    private McpStartupOptions _mcpStartupOptions = McpStartupOptions.Empty;
    private volatile bool _mcpStopFailed;
    private AppExitReason _exitReason = AppExitReason.None;
    private string? _startupErrorDetails;
    private CancellationTokenSource? _themeUpdatesCts;

    internal static bool IsFatalShutdown => Volatile.Read(ref _fatalErrorHandled) == 1;

    internal static bool IsRestartRequested => Volatile.Read(ref _restartRequested) == 1;

    private static Serilog.ILogger HostLog => Log.ForContext<App>();

    internal static bool IsHeadless { get; private set; }

    private static string[] StartupArguments { get; set; } = [];

    internal static bool IsGallery { get; private set; }

    internal static DebugChannelOverride DebugChannel { get; private set; } = DebugChannelOverride.None;

    protected override async void OnStartup(StartupEventArgs e)
    {
        var startupTimestamp = Stopwatch.GetTimestamp();
        var interactiveWait = TimeSpan.Zero;

        base.OnStartup(e);

        ApplyUiCulture();

        StartupArguments = e.Args;
        var previousInstance = AppRestart.FindPreviousProcessId(e.Args);
        var handoffStart = Stopwatch.GetTimestamp();
        var previousInstanceExited = previousInstance is not { } previousProcessId
                                     || AppRestart.WaitForExit(previousProcessId, AppRestart.HandoffTimeout);
        interactiveWait += Stopwatch.GetElapsedTime(handoffStart);

        IsHeadless = e.Args.Any(arg => string.Equals(arg, "--ui-smoke", StringComparison.OrdinalIgnoreCase));
        DebugChannel = DebugChannelOverride.Parse(e.Args);
        _isFinalizeUpdate = e.Args.Any(arg => string.Equals(arg, UpdateApplier.FinalizeArgument, StringComparison.OrdinalIgnoreCase));

        string? mcpArgumentError = null;
        try
        {
            _mcpStartupOptions = ParseMcpStartupOptions(e.Args);
        }
        catch (ArgumentException exception)
        {
            mcpArgumentError = exception.Message;
            _mcpStartupOptions = McpStartupOptions.Empty;
        }

        var galleryIndex = Array.FindIndex(e.Args, static arg => string.Equals(arg, GalleryHost.ArgumentName, StringComparison.OrdinalIgnoreCase));

        if (galleryIndex >= 0)
        {
            _galleryArgs = [.. e.Args.Skip(galleryIndex + 1)];
            IsHeadless = true;
            IsGallery = true;
            _galleryWorkspace = GalleryWorkspace.Redirect();
        }

        var coreUiLogSink = new UiLogSink();
        SelfLog.Enable(message => Debug.WriteLine($"[Serilog] {message}"));

        _loggingOptions = new()
        {
            LogsDirectory = AppPaths.Combine("logs"),
            FileNamePrefix = AppInfo.LogFilePrefix,
            OutputTemplate = OutputTemplate,
            RetainedFileCountLimit = 31,
            FileSizeLimitBytes = 50L * 1024 * 1024,
            RollOnFileSizeLimit = true,
            MinimumLevel = LogEventLevel.Debug,
            MinimumLevelOverrides = new Dictionary<string, LogEventLevel>(StringComparer.Ordinal)
            {
                ["Microsoft"] = LogEventLevel.Information,
                ["Microsoft.AspNetCore"] = LogEventLevel.Warning,
                ["System.Net.Http.HttpClient"] = LogEventLevel.Warning,
            },
            WriteToDebug = true,
            Configure = configuration => configuration
                .WriteTo.Console(outputTemplate: OutputTemplate)
                .WriteTo.Sink(coreUiLogSink, LogEventLevel.Information),
        };

        _logging = KeepShellLogging.Bootstrap(_loggingOptions);

        HostLog.Information(SessionStartLine);

        if (previousInstance is { } restartedAfter)
        {
            ReportRestartHandoff(restartedAfter, previousInstanceExited);
        }

        _bindingErrors = BindingErrorSink.Attach();
        _bindingErrors.Captured += OnBindingErrorCaptured;

        AttachFatalExceptionTrap();

        StyledMessageBox.DefaultTitle = AppInfo.Name;
        ShutdownMode = ShutdownMode.OnExplicitShutdown;

        try
        {
            if (mcpArgumentError is not null)
            {
                HostLog.Error("Некорректные параметры запуска MCP: {Error}", mcpArgumentError);
                _startupErrorDetails = $"Некорректные параметры запуска MCP: {mcpArgumentError}";
                _exitReason = AppExitReason.Failed;

                if (!IsHeadless)
                {
                    ShowFatalErrorNotice();
                }

                Shutdown(1);
                return;
            }

            var uiSettingsPath = AppPaths.SettingsFile("ui-preferences.toml");
            ISettingsStore uiSettings = new SettingsStore(uiSettingsPath);

            ThemeManager.Register(ThemeManager.DefaultLight);
            ThemeManager.Register(ThemeManager.DefaultDark);

            var themePackLoader = CreateThemePackLoader();
            var restoredThemeKey = themePackLoader.Restore(uiSettings);

            if (_galleryArgs is not null && _galleryWorkspace is not null)
            {
                _galleryArguments = GalleryHost.Parse(_galleryArgs, System.IO.Path.Combine(_galleryWorkspace.OutputRoot, GalleryRunner.FolderName));
            }

            ThemeManager.Apply(restoredThemeKey ?? ThemeManager.DefaultLight.Key);
            FontScaleManager.Initialize(uiSettings.GetDouble(SettingsKeys.FontScale, FontScaleManager.DefaultScale));
            AppScaledSizes.Register(this);

            _singleInstanceMutex = SingleInstanceGate.TryAcquire(_isFinalizeUpdate);

            if (_singleInstanceMutex is null)
            {
                HostLog.Information("Обнаружен уже запущенный экземпляр приложения. Завершение работы");
                _exitReason = AppExitReason.AlreadyRunning;

                if (!IsHeadless)
                {
                    StyledMessageBox.Show("PoproshaykaBot уже запущен.\n\nОдновременно может работать только один экземпляр приложения.",
                        "Приложение уже запущено",
                        MessageBoxButton.OK,
                        MessageBoxImage.Information);
                }

                Shutdown();
                return;
            }

            _previousRunState = BeginRunStateSession(AppInfo.Version, AppBuildBadge.StartupLine);

            ReportEnvironment(e.Args, _previousRunState);

            using var splashLoggerFactory = new SerilogLoggerFactory(Log.Logger);
            using var splash = IsHeadless ? null : new StartupSplash(
                AppInfo.Name, AppInfo.Version, "Запуск приложения", 4, splashLoggerFactory.CreateLogger<App>());

            using (await BeginStartupPhaseAsync(splash, "Подготовка настроек…"))
            {
                if (_isFinalizeUpdate)
                {
                    FinalizeUpdate();
                }

                ViewLocator.InstallIntoApplication();

                if (IsHeadless)
                {
                    MigrateLegacySettingsLayout();
                }
                else
                {
                    uiSettings = OfferLegacyDataImport(uiSettings, uiSettingsPath, themePackLoader, out var modalWait);
                    interactiveWait += modalWait;
                    await Task.Run(MigrateLegacySettingsLayout);
                }
            }

            using (await BeginStartupPhaseAsync(splash, "Подготовка приложения…"))
            {
                var services = new ServiceCollection();
                services.AddSingleton(themePackLoader);
                ConfigureServices(
                    services,
                    coreUiLogSink,
                    uiSettings,
                    _loggingOptions!,
                    _bindingErrors,
                    IsGallery ? null : _mcpStartupOptions.DiscoveryFilePath);
                services.AddKeepShellLogging(_logging!);
                services.AddSingleton(_unhandledExceptionJournal);
                services.AddSingleton(_previousRunState);
                services.AddSingleton(UnobservedTaskExceptions);

                _services = services.BuildServiceProvider(new ServiceProviderOptions
                {
                    ValidateOnBuild = true,
                    ValidateScopes = true,
                });

                _services.GetRequiredService<IBroadcastDraftCoordinator>().RecoverCompletedDraftOnStartup();
                _services.ActivateEventSubscribers(typeof(InfrastructureServiceCollectionExtensions).Assembly);
                _services.GetRequiredService<ChannelCatalogReconciler>().ReconcileStartup();
                ReportConfiguration(_services);
                _services.GetRequiredService<StatisticsPreload>().Start();
                _appLifetime = _services.GetRequiredService<AppLifetime>();
            }

            if (IsHeadless)
            {
                _services.GetRequiredService<ChannelSwitchPromptBannerViewModel>().Suppress();
                HostLog.Information("Запуск в режиме UI smoke-теста. HTTP сервер и сетевые подсистемы отключены");
            }
            else
            {
                using var launchSpan = await BeginStartupPhaseAsync(splash, "Запуск сервисов…");
                var settingsManager = _services.GetRequiredService<SettingsManager>();
                await StartAppLifetimeAsync(_appLifetime);
                _kestrelServer = _services.GetRequiredService<KestrelHttpServer>();
                await StartHttpServerIfNeededAsync(settingsManager, _kestrelServer);
                await StartStreamMonitoringAsync(_services.GetServices<StreamMonitoringHost>(), _startedStreamMonitoringHosts);
                _memoryWatchdog = new(new SerilogLoggerFactory(Log.Logger).CreateLogger(nameof(MemoryWatchdog)),
                    static () =>
                    {
                        _runStateStore?.MarkFailure(RunStateStatus.MemoryWatchdog);
                        Log.CloseAndFlush();
                    },
                    _services.GetRequiredService<MemoryUsageSink>());
            }

            if (_galleryArguments is not null)
            {
                RunGallery(_galleryArguments, _services);
                return;
            }

            using (await BeginStartupPhaseAsync(splash, "Открытие главного окна…"))
            {
                var window = _services.GetRequiredService<MainWindow>();
                MainWindow = window;

                if (IsHeadless)
                {
                    OffScreenWindow.Prepare(window, window.Width, window.Height);
                }

                window.Show();

                if (splash is not null)
                {
                    await Dispatcher.Yield(DispatcherPriority.Loaded);
                }
            }

            splash?.Dispose();

            if ((!IsHeadless || _mcpStartupOptions.ForceEnable) && !IsGallery)
            {
                var mcpPreferences = _services.GetRequiredService<McpPreferences>();

                if (_mcpStartupOptions.HasOverrides)
                {
                    mcpPreferences.ApplyRuntimeOverrides(
                        enabled: _mcpStartupOptions.ForceEnable ? true : null,
                        port: _mcpStartupOptions.Port,
                        token: _mcpStartupOptions.Token,
                        allowMutations: _mcpStartupOptions.AllowMutations);
                }

                _services.GetRequiredService<McpServerHost>().Apply();
            }

            ShutdownMode = ShutdownMode.OnMainWindowClose;

            _services.GetRequiredService<PerformanceMonitor>().ReportStartup(ComputeStartupElapsed(startupTimestamp, interactiveWait));

            if (!IsHeadless && !IsGallery)
            {
                _themeUpdatesCts = new CancellationTokenSource();
                var themeUpdates = _services.GetRequiredService<ThemePackUpdateService>();
                var themeUpdatesToken = _themeUpdatesCts.Token;
                _ = UiAsync.RunGuardedAsync(
                    async () =>
                    {
                        _ = await themeUpdates.CheckOnStartupAsync(themeUpdatesToken);
                    },
                    static exception => HostLog.Warning(exception, "Не удалось проверить обновления пакетов тем"));
            }

            if (_previousRunState.WasUnclean && !IsHeadless)
            {
                _services.GetRequiredService<ShellViewModel>().ReportUncleanPreviousRun();
            }

            MaybeLaunchOnboardingWizard();
        }
        catch (Exception ex)
        {
            HostLog.Fatal(ex, "{App} не смог запуститься", AppInfo.Name);
            _exitReason = AppExitReason.Failed;
            _runStateStore?.MarkFailure(RunStateStatus.StartupFailed, ex);
            Interlocked.Exchange(ref _fatalErrorHandled, 1);

            if (!IsHeadless)
            {
                _startupErrorDetails = ex.ToString();
            }

            Shutdown(1);
        }
    }

    private static async ValueTask<SplashSpan?> BeginStartupPhaseAsync(StartupSplash? splash, string name)
    {
        var span = splash?.StartSpan(name);

        if (span is not null)
        {
            await Dispatcher.Yield(DispatcherPriority.Loaded);
        }

        return span;
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _memoryWatchdog?.Dispose();
        _themeUpdatesCts?.Cancel();
        _themeUpdatesCts?.Dispose();
        _themeUpdatesCts = null;

        var stoppedCleanly = true;

        if (_services is not null)
        {
            stoppedCleanly = false;
            try
            {
                stoppedCleanly = Task.Run(StopAllComponentsAsync).GetAwaiter().GetResult();
            }
            catch (Exception exception)
            {
                HostLog.Error(exception, "Ошибка остановки сервисов при завершении работы");
            }

            if (_exitReason is AppExitReason.None)
            {
                _exitReason = !stoppedCleanly
                    ? AppExitReason.Failed
                    : IsRestartRequested ? AppExitReason.Restart : AppExitReason.Normal;
            }

            if (stoppedCleanly)
            {
                var updateApplied = !IsHeadless && !IsFatalShutdown && ApplyPendingUpdate();

                if (updateApplied)
                {
                    _exitReason = AppExitReason.UpdatePending;
                }

                if (AppRestart.ShouldLaunch(IsRestartRequested, updateApplied, IsFatalShutdown))
                {
                    LaunchReplacement();
                }
            }
            else if (!IsFatalShutdown)
            {
                _runStateStore?.MarkFailure(RunStateStatus.StopFailed);
            }
        }

        if (stoppedCleanly && !IsFatalShutdown && _exitReason is not (AppExitReason.AlreadyRunning or AppExitReason.Failed))
        {
            _runStateStore?.ClearCleanExit();
        }

        TaskScheduler.UnobservedTaskException -= OnUnobservedTaskException;

        if (_bindingErrors is { } bindingErrors)
        {
            bindingErrors.Captured -= OnBindingErrorCaptured;

            if (bindingErrors.Count > 0)
            {
                HostLog.Warning("Ошибок привязки данных за прогон: {BindingErrorCount}", bindingErrors.Count);
            }
        }

        if (IsFatalShutdown && !IsHeadless)
        {
            ShowFatalErrorNotice();
        }

        HostLog.Information("Завершение работы приложения ({Reason}), код выхода {ExitCode}", _exitReason, e.ApplicationExitCode);
        _bindingErrors?.Dispose();
        _logging?.Dispose();

        _singleInstanceMutex?.Dispose();

        base.OnExit(e);
    }

    private static void ApplyUiCulture()
    {
        var language = XmlLanguage.GetLanguage(UiCulture.Russian.IetfLanguageTag);

        FrameworkElement.LanguageProperty.OverrideMetadata(typeof(FrameworkElement), new FrameworkPropertyMetadata(language));
    }

    private static void OnBindingErrorCaptured(object? sender, BindingErrorRecord record)
    {
        HostLog.Error("Ошибка привязки данных: {BindingError}", record.Message);
    }

    private void RunGallery(GalleryArguments arguments, IServiceProvider services)
    {
        FontScaleManager.Apply(arguments.FontScale);

        _ = Dispatcher.InvokeAsync(async () =>
        {
            var code = 1;

            try
            {
                code = await GalleryRunner.RunAsync(GalleryHost.Create(services, arguments), arguments, new GalleryJournal());
            }
            catch (Exception exception)
            {
                HostLog.Error(exception, "Съёмка галереи упала");
            }

            Shutdown(code);
        });
    }

    private static ThemePackLoader CreateThemePackLoader()
    {
        return new(
            AppPaths.Combine("themes"),
            static (path, error) => HostLog.Warning(error, "Пакет темы отклонён: {Path}", path));
    }

    private static void MigrateLegacySettingsLayout()
    {
        using var loggerFactory = new SerilogLoggerFactory(Log.Logger);
        var logger = loggerFactory.CreateLogger(nameof(LegacySettingsLayoutMigrator));

        try
        {
            LegacySettingsLayoutMigrator.Run(AppPaths.BaseDirectory, AppPaths.SettingsDirectory, logger);
        }
        catch (Exception exception)
        {
            HostLog.Error(exception, "Ошибка пред-миграции layout-а настроек");
        }
    }

    private static void ConfigureServices(
        IServiceCollection services,
        UiLogSink uiLogSink,
        ISettingsStore uiSettings,
        KeepShellLoggingOptions loggingOptions,
        BindingErrorSink? bindingErrors,
        string? mcpDiscoveryFile)
    {
        var uiSettingsGuard = new UiSettingsWriteGuard(uiSettings);
        services.AddSingleton(uiSettingsGuard);
        services.AddSingleton<ISettingsStore>(uiSettingsGuard);

        if (bindingErrors is not null)
        {
            services.AddSingleton(bindingErrors);
        }

        services
            .AddCoreInfrastructure(uiLogSink, disposeSerilog: false, registerLogging: false)
            .AddStatistics()
            .AddSettingsStores()
            .AddDebugChannel(DebugChannel)
            .AddTwitchClients()
            .AddYouTubeClients()
            .AddGameBoxArt(cacheOnly: IsGallery)
            .AddUserAvatars(cacheOnly: IsGallery)
            .AddChatPipeline()
            .AddStreamMonitoring()
            .AddBroadcasting()
            .AddPolls()
            .AddHttpServer()
            .AddObsIntegration()
            .AddSelfUpdate()
            .AddDiagnostics();

        services.AddSingleton<BotConnectionManager>();
        services.AddSingleton<IBotConnectionController>(provider => provider.GetRequiredService<BotConnectionManager>());

        services.AddKeepShell();

        services.AddKeepShellDiagnostics(new DiagnosticsOptions
        {
            AppName = AppInfo.Name,
            AppVersion = AppInfo.Version,
            DataDirectory = AppPaths.BaseDirectory,
            PortableStorage = AppPaths.IsPortable,
            BundleLogFiles = 3,
            BundleLogTailLines = 4000,
            BundleHitchRows = 300,
        });

        services.AddSingleton(static provider => new DiagnosticsSnapshotPublisher(
            provider.GetRequiredService<IUiDispatcher>(),
            provider.GetRequiredService<DiagnosticsSnapshotSource>().Capture,
            provider.GetRequiredService<ILogger<DiagnosticsSnapshotPublisher>>()));

        services.AddSingleton<IDiagnosticsBundleSource, BotDiagnosticsBundleSource>();
        services.AddSingleton<ISecretValueProvider>(static provider =>
            new DelegateSecretValueProvider("настройки MCP", () =>
            {
                var preferences = provider.GetRequiredService<McpPreferences>();
                return [preferences.Token, preferences.EffectiveToken];
            }));
        services.AddSingleton<IDiagnosticsSecretSource, BotDiagnosticsSecretSource>();

        services.AddSingleton<IDiagnosticsCard, MemoryDiagnosticsCardViewModel>();
        services.AddSingleton<IDiagnosticsCard, EventSubDiagnosticsCardViewModel>();
        services.AddSingleton<IDiagnosticsCard, ChatDiagnosticsCardViewModel>();
        services.AddSingleton<IDiagnosticsCard, ObsDiagnosticsCardViewModel>();
        services.AddSingleton<IDiagnosticsCard, SseDiagnosticsCardViewModel>();
        services.AddSingleton<IDiagnosticsCard, QueueDiagnosticsCardViewModel>();
        services.AddSingleton<IDiagnosticsCard, EventBusDiagnosticsCardViewModel>();

        services.AddSingleton<McpConnectNotificationGate>();
        services.AddSingleton<IMcpConnectNotificationGate>(static provider => provider.GetRequiredService<McpConnectNotificationGate>());
        services.AddSingleton<BotAutomation>();
        services.AddKeepShellMcp<ShellViewModel>(new McpServerOptions
        {
            ServerName = "poproshaykabot",
            AppName = AppInfo.Name,
            AppVersion = AppInfo.Version,
            DefaultPort = 7656,
            ShotsDirectory = AppPaths.Combine("shots"),
            DiskLogDirectory = loggingOptions.LogsDirectory,
            DiskLogGlobs = [AppInfo.LogFileGlob],
            DiscoveryFilePath = mcpDiscoveryFile,
            ToolTypes = [typeof(PoproshaykaBotTools)],
            ConfigureTools = static (provider, tools) => tools.AddSingleton(provider.GetRequiredService<BotAutomation>()),
        });

        services.AddSingleton(static provider => new ErrorReportOptions
        {
            IssueRepo = () => provider.GetRequiredService<IUpdateRepositoryProvider>().Slug,
            LogFileGlobs = [AppInfo.LogFileGlob],
            SessionStartMarker = AppInfo.SessionStartMarker,
        });
        services.AddSingleton<ErrorReportService>();

        services.AddSingleton<DashboardTileViewModel, StreamInfoTileViewModel>();
        services.AddSingleton<DashboardTileViewModel, StreamPreviewTileViewModel>();
        services.AddSingleton<DashboardTileViewModel, BroadcastStatusTileViewModel>();
        services.AddSingleton<DashboardTileViewModel, BroadcastProfilesTileViewModel>();
        services.AddSingleton<DashboardTileViewModel, PollsTileViewModel>();
        services.AddSingleton<DashboardTileViewModel, ObsInfoTileViewModel>();
        services.AddSingleton<DashboardTileViewModel, ChatDisplayTileViewModel>();
        services.AddSingleton<DashboardTileViewModel, ChatOverlayPreviewTileViewModel>();
        services.AddSingleton<DashboardTileViewModel, MultiplatformChatTileViewModel>();
        services.AddSingleton<MultistreamTileViewModel>();
        services.AddSingleton<DashboardTileViewModel>(static provider => provider.GetRequiredService<MultistreamTileViewModel>());

        services.AddSingleton<StatisticsPreload>(static provider => new(
            provider.GetRequiredService<UserStatisticsLoader>(),
            provider.GetRequiredService<IUiDispatcher>().Invoke));
        services.AddSingleton<DashboardViewModel>();
        services.AddSingleton<IMultistreamPlatformControls, YouTubeMultistreamPlatformControls>();
        services.AddSingleton<MultistreamPageViewModel>();
        services.AddSingleton<LogsViewModel>();
        services.AddSingleton<UserStatisticsPageViewModel>();
        services.AddSingleton<GameBoxArtProvider>();
        services.AddSingleton<UserAvatarProvider>();
        services.AddSingleton<ChatMessageImageProvider>();
        services.AddSingleton<StreamHistoryPageViewModel>();
        services.AddSingleton<CommandsPageViewModel>();
        services.TryAddSingleton(static _ => CreateThemePackLoader());
        services.AddSingleton<ThemeSelectionViewModel>();
        services.AddSingleton(static _ => new ThemePackCatalog(
            new Uri("https://github.com/TheVSAKeeper/KeepShell.Themes/releases/latest/download/themes.json")));
        services.AddSingleton<ThemePackUpdateService>();
        services.AddSingleton(static provider => new ThemePacksViewModel(
            provider.GetRequiredService<ThemePackLoader>(),
            provider.GetRequiredService<ThemeSelectionViewModel>(),
            provider.GetRequiredService<IFilePicker>(),
            provider.GetRequiredService<ILogger<ThemePacksViewModel>>(),
            provider.GetRequiredService<ThemePackCatalog>(),
            provider.GetRequiredService<IThemeCatalogWindowService>(),
            provider.GetRequiredService<IDialogService>(),
            provider.GetRequiredService<IClipboardService>(),
            provider.GetRequiredService<ThemePackUpdateService>(),
            restartApp: () =>
            {
                if (IsHeadless || !TryRequestRestart())
                {
                    return Task.FromResult(false);
                }

                provider.GetRequiredService<IMainWindowController>().RequestClose();
                return Task.FromResult(true);
            }));
        services.AddSingleton<ShellPreferences>();
        services.AddSingleton<UpdateBannerViewModel>();
        services.AddSingleton<OnboardingBannerViewModel>();
        services.AddSingleton<ChannelSwitchPromptBannerViewModel>();
        services.AddSingleton<DebugBannerViewModel>();
        services.AddSingleton<IUnsavedChangesPrompt, UnsavedChangesPrompt>();
        services.AddSingleton<IOnboardingWizardLauncher, OnboardingWizardLauncher>();
        services.AddSingleton<IEmbeddedTwitchAuthDialog, EmbeddedTwitchAuthDialog>();
        services.AddSingleton<IEmbeddedYouTubeAuthDialog, EmbeddedYouTubeAuthDialog>();
        services.AddSingleton<IPlatformWebChatLoginDialog, EmbeddedPlatformChatLoginDialog>();
        services.AddSingleton<IPlatformAccountLogin, TwitchAccountLogin>();
        services.AddSingleton<IPlatformAccountLogin, YouTubeAccountLogin>();
        services.AddSingleton<ILegacyImportDialog, LegacyImportDialog>();
        services.AddSingleton<StreamMonitoringViewModel>();
        services.AddSingleton<IMainWindowController, MainWindowController>();
        services.AddSingleton<ShellViewModel>();
        services.AddSingleton<MainWindow>();

        services.AddTransient<GameAutocompleteViewModel>();
        services.AddTransient<BroadcastProfileEditDialogViewModel>();
        services.AddTransient<PollProfileEditDialogViewModel>();
        services.AddTransient<PollFromProfileDialogViewModel>();
        services.AddTransient<EmbeddedTwitchAuthDialogViewModel>();
        services.AddTransient<EmbeddedYouTubeAuthDialogViewModel>();
        services.AddTransient<EmbeddedPlatformChatLoginDialogViewModel>();
        services.AddTransient<MultistreamPlatformSetupDialogViewModel>();
        services.AddTransient<MultistreamChannelWizardDialogViewModel>();

        services.AddTransient<BasicSettingsSectionViewModel>();
        services.AddTransient<RateLimitingSettingsViewModel>();
        services.AddTransient<MessagesSettingsSectionViewModel>();
        services.AddTransient<HttpServerSectionViewModel>();
        services.AddTransient<OAuthSettingsViewModel>();
        services.AddTransient<YouTubeSettingsSectionViewModel>();
        services.AddTransient<ChatSettingsSectionViewModel>();
        services.AddSingleton<ObsChatSettingsSectionViewModel>();
        services.AddSingleton<ObsIntegrationSectionViewModel>();
        services.AddTransient<AutoBroadcastSettingsViewModel>();
        services.AddTransient<BotLifecycleAutomationSectionViewModel>();
        services.AddTransient<MiscSettingsSectionViewModel>();
        services.AddSingleton<PollsSettingsSectionViewModel>();
        services.AddSingleton<UpdateSettingsSectionViewModel>();
        services.AddTransient<DashboardLayoutSectionViewModel>();
        services.AddTransient<RanksSettingsSectionViewModel>();
        services.AddTransient<ChannelCatalogSettingsSectionViewModel>();
        services.AddTransient<PlatformAccountsSectionViewModel>();
        services.AddTransient<ChannelGroupsSettingsSectionViewModel>();
        services.AddSingleton(provider => new BroadcastProfilesSettingsSectionViewModel(
            provider.GetServices<DashboardTileViewModel>().OfType<BroadcastProfilesTileViewModel>().Single()));
        services.AddTransient<DebugChannelSectionViewModel>();
        services.AddSingleton<McpSettingsSectionViewModel>();
        services.AddTransient<AppearanceSettingsSectionViewModel>();

        services.AddTransient<ISettingsSectionContributor, ViewModels.Settings.Contributors.BasicSettingsSectionContributor>();
        services.AddTransient<ISettingsSectionContributor, ViewModels.Settings.Contributors.TwitchConnectionSettingsContributor>();
        services.AddTransient<ISettingsSectionContributor, ViewModels.Settings.Contributors.YouTubeConnectionSettingsContributor>();
        services.AddTransient<ISettingsSectionContributor, ViewModels.Settings.Contributors.ChatSettingsSectionContributor>();
        services.AddTransient<ISettingsSectionContributor, ViewModels.Settings.Contributors.ObsSettingsSectionContributor>();
        services.AddTransient<ISettingsSectionContributor, ViewModels.Settings.Contributors.StreamSettingsSectionContributor>();
        services.AddTransient<ISettingsSectionContributor, ViewModels.Settings.Contributors.RanksSettingsSectionContributor>();
        services.AddTransient<ISettingsSectionContributor, ViewModels.Settings.Contributors.ChannelsSettingsSectionContributor>();
        services.AddTransient<ISettingsSectionContributor, ViewModels.Settings.Contributors.DashboardSettingsSectionContributor>();
        services.AddTransient<ISettingsSectionContributor, ViewModels.Settings.Contributors.UpdateSettingsSectionContributor>();
        services.AddTransient<ISettingsSectionContributor, ViewModels.Settings.Contributors.DebugChannelSettingsSectionContributor>();
        services.AddTransient<ISettingsSectionContributor, ViewModels.Settings.Contributors.McpSettingsSectionContributor>();
        services.AddTransient<ISettingsSectionContributor, ViewModels.Settings.Contributors.AppearanceSettingsSectionContributor>();
        services.AddTransient<ISettingsSectionContributor, ViewModels.Settings.Contributors.MiscSettingsSectionContributor>();

        services.AddSingleton<SettingsPageViewModel>();

        services.AddTransient<IOnboardingPageViewModel, WelcomePageViewModel>();
        services.AddTransient<IOnboardingPageViewModel, CredentialsPageViewModel>();
        services.AddTransient<IOnboardingPageViewModel, HttpServerCheckPageViewModel>();
        services.AddTransient<IOnboardingPageViewModel, BotAuthorizationPageViewModel>();
        services.AddTransient<IOnboardingPageViewModel, BroadcasterAuthorizationPageViewModel>();
        services.AddTransient<IOnboardingPageViewModel, YouTubeOnboardingPageViewModel>();
        services.AddTransient<IOnboardingPageViewModel, BotConnectionPageViewModel>();
        services.AddTransient<IOnboardingPageViewModel, HealthCheckPageViewModel>();
        services.AddTransient<IOnboardingPageViewModel, CompletionPageViewModel>();

        services.AddTransient<OnboardingWizardViewModel>();
        services.AddTransient<OnboardingWizardWindow>();
    }

    internal async Task StopMcpBeforeExitAsync()
    {
        if (IsGallery || _services is null)
        {
            return;
        }

        if (_services.GetService<McpServerHost>() is not { } mcpHost)
        {
            return;
        }

        using var timeout = new CancellationTokenSource(ShutdownDeadlines.SoftDeadline);

        try
        {
            await mcpHost.StopAsync(timeout.Token);
        }
        catch (OperationCanceledException exception)
        {
            HostLog.Warning(exception, "Остановка MCP-сервера перед выходом прервана по таймауту");
        }
        catch (Exception exception)
        {
            HostLog.Error(exception, "Ошибка остановки MCP-сервера перед выходом");
        }
    }

    private async Task<bool> StopAllComponentsAsync()
    {
        using var loggerFactory = new SerilogLoggerFactory(Log.Logger);
        var enforceShutdownBounds = !IsHeadless || IsGallery;
        using var shutdownWatchdog = enforceShutdownBounds
            ? new ShutdownWatchdog(loggerFactory.CreateLogger(nameof(ShutdownWatchdog)),
                static () =>
                {
                    _runStateStore?.MarkFailure(RunStateStatus.ShutdownWatchdog);
                    Log.CloseAndFlush();
                },
                ShutdownDeadlines.HardDeadline)
            : null;

        using var shutdownTimeout = enforceShutdownBounds
            ? new CancellationTokenSource(ShutdownDeadlines.SoftDeadline)
            : null;
        var shutdownToken = shutdownTimeout?.Token ?? CancellationToken.None;

        var mcpStoppedCleanly = true;

        if (!IsGallery)
        {
            mcpStoppedCleanly = await StopMcpServerAsync(shutdownToken);
        }

        _services!.GetService<ISettingsStore>()?.Close();

        if (_kestrelServer is not null)
        {
            StopHttpServer(_kestrelServer);
        }

        if (_startedStreamMonitoringHosts.Count > 0)
        {
            StopStreamMonitoring(_startedStreamMonitoringHosts, shutdownToken);
            _startedStreamMonitoringHosts.Clear();
        }

        if (_appLifetime is not null)
        {
            await StopAppLifetimeAsync(_appLifetime, shutdownToken);
        }

        _services!.GetService<StatisticsPreload>()?.Stop(shutdownToken);

        await _services!.DisposeAsync();

        return mcpStoppedCleanly && !_mcpStopFailed;
    }

    private async Task<bool> StopMcpServerAsync(CancellationToken cancellationToken)
    {
        if (_services?.GetService<McpServerHost>() is not { } mcpHost)
        {
            return true;
        }

        try
        {
            await mcpHost.StopAsync(cancellationToken);
            return true;
        }
        catch (OperationCanceledException exception)
        {
            _mcpStopFailed = true;
            HostLog.Warning(exception, "Остановка MCP-сервера прервана по таймауту завершения");
            return false;
        }
        catch (Exception exception)
        {
            _mcpStopFailed = true;
            HostLog.Error(exception, "Ошибка остановки MCP-сервера");
            return false;
        }
    }

    internal sealed record McpStartupOptions(
        bool ForceEnable,
        int? Port,
        string? Token,
        bool? AllowMutations,
        string? DiscoveryFilePath)
    {
        public static readonly McpStartupOptions Empty = new(false, null, null, null, null);

        public bool HasOverrides => ForceEnable || Port.HasValue || Token is not null || AllowMutations.HasValue;
    }

    internal static McpStartupOptions ParseMcpStartupOptions(
        IReadOnlyList<string> args,
        Func<string, string?>? readEnvironment = null)
    {
        readEnvironment ??= Environment.GetEnvironmentVariable;

        var forceEnable = ParseEnvFlag(readEnvironment("POPROSHAYKA_MCP_ENABLE")) == true;
        var port = ParseMcpPort(readEnvironment("POPROSHAYKA_MCP_PORT"));
        var token = NormalizeOptionValue(readEnvironment("POPROSHAYKA_MCP_TOKEN"));
        var allowMutations = ParseEnvFlag(readEnvironment("POPROSHAYKA_MCP_ALLOW_MUTATIONS"));
        var discoveryFile = NormalizeOptionValue(readEnvironment("POPROSHAYKA_MCP_DISCOVERY_FILE"));

        var index = 0;

        while (index < args.Count)
        {
            var arg = args[index];

            if (string.Equals(arg, "--enable-mcp", StringComparison.OrdinalIgnoreCase))
            {
                forceEnable = true;
                index++;
                continue;
            }

            if (string.Equals(arg, "--mcp-allow-mutations", StringComparison.OrdinalIgnoreCase))
            {
                allowMutations = true;
                index++;
                continue;
            }

            if (TryReadInlineOption(arg, "--mcp-allow-mutations=", out var allowMutationsValue))
            {
                allowMutations = ParseEnvFlag(allowMutationsValue)
                    ?? throw new ArgumentException(
                        $"Недопустимое значение параметра --mcp-allow-mutations: «{allowMutationsValue}». Ожидается true/false или 1/0.");

                index++;
                continue;
            }

            if (TryReadOptionValue(args, ref index, "--mcp-port", out var portValue))
            {
                port = ParseMcpPort(portValue)
                    ?? throw new ArgumentException(
                        $"Недопустимое значение параметра --mcp-port: «{portValue}». Ожидается 0 или целое число от {McpPreferences.PortMin} до {McpPreferences.PortMax}.");

                index++;
                continue;
            }

            if (TryReadOptionValue(args, ref index, "--mcp-token", out var tokenValue))
            {
                token = NormalizeOptionValue(tokenValue)
                    ?? throw new ArgumentException("Параметр --mcp-token требует непустое значение.");

                index++;
                continue;
            }

            if (TryReadOptionValue(args, ref index, "--mcp-discovery-file", out var discoveryValue))
            {
                discoveryFile = NormalizeOptionValue(discoveryValue)
                    ?? throw new ArgumentException("Параметр --mcp-discovery-file требует путь к файлу.");

                index++;
                continue;
            }

            index++;
        }

        return new(forceEnable, port, token, allowMutations, discoveryFile);
    }

    private static bool? ParseEnvFlag(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var trimmed = value.Trim();

        if (string.Equals(trimmed, "1", StringComparison.Ordinal)
            || string.Equals(trimmed, "true", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (string.Equals(trimmed, "0", StringComparison.Ordinal)
            || string.Equals(trimmed, "false", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return null;
    }

    private static int? ParseMcpPort(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        if (int.TryParse(value.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed)
            && (parsed == 0 || (parsed >= McpPreferences.PortMin && parsed <= McpPreferences.PortMax)))
        {
            return parsed;
        }

        return null;
    }

    private static string? NormalizeOptionValue(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }

    private static bool TryReadInlineOption(string arg, string prefix, out string? value)
    {
        if (arg.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
        {
            value = arg[prefix.Length..];
            return true;
        }

        value = null;
        return false;
    }

    private static bool TryReadOptionValue(IReadOnlyList<string> args, ref int index, string name, out string? value)
    {
        var arg = args[index];

        if (string.Equals(arg, name, StringComparison.OrdinalIgnoreCase))
        {
            if (index + 1 >= args.Count || args[index + 1].StartsWith("--", StringComparison.Ordinal))
            {
                throw new ArgumentException($"Параметр {name} требует значение.");
            }

            value = args[++index];
            return true;
        }

        var prefix = name + "=";

        if (arg.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
        {
            value = arg[prefix.Length..];
            return true;
        }

        value = null;
        return false;
    }

    public static TimeSpan ComputeStartupElapsed(long startTimestamp, TimeSpan excludedWait)
    {
        var elapsed = Stopwatch.GetElapsedTime(startTimestamp) - excludedWait;
        return elapsed > TimeSpan.Zero ? elapsed : TimeSpan.Zero;
    }

    private static PreviousRunState BeginRunStateSession(string version, string? build)
    {
        var store = new RunStateStore();
        _runStateStore = store;
        return store.BeginSession(version, build);
    }

    private void AttachFatalExceptionTrap()
    {
        DispatcherUnhandledException += OnDispatcherUnhandledException;
        TaskScheduler.UnobservedTaskException += OnUnobservedTaskException;

        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
        {
            var exception = args.ExceptionObject as Exception;
            _runStateStore?.MarkFailure(RunStateStatus.FatalShutdown, exception);

            if (exception is not null)
            {
                _unhandledExceptionJournal.Record("AppDomain.UnhandledException", exception, handled: false);
                HostLog.Fatal(exception, "Приложение завершило работу из-за непредвиденной ошибки");
            }

            Log.CloseAndFlush();
        };
    }

    private void OnUnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs args)
    {
        args.SetObserved();

        var recorded = UnobservedTaskExceptions.Record(args.Exception);
        if (recorded.IsCancellationOnly)
        {
            HostLog.Debug(
                "Отменённая фоновая задача завершилась без наблюдателя (вложенных: {InnerCount}, всего отмен: {CanceledCount})",
                recorded.InnerCount,
                recorded.TotalCount);
            return;
        }

        _unhandledExceptionJournal.Record("TaskScheduler.UnobservedTaskException", args.Exception, handled: true);

        HostLog.Error(
            "Необработанное исключение фоновой задачи: {ExceptionType} @ {TopFrame} (вложенных: {InnerCount}, всего сбоев: {FailureCount})",
            recorded.Entry?.ExceptionType ?? args.Exception.GetType().FullName,
            recorded.Entry?.TopFrame ?? "–",
            recorded.InnerCount,
            recorded.TotalCount);
    }

    private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs args)
    {
        args.Handled = true;

        if (Interlocked.Exchange(ref _fatalErrorHandled, 1) == 1)
        {
            return;
        }

        _unhandledExceptionJournal.Record("Dispatcher.UnhandledException", args.Exception, handled: true);

        _exitReason = AppExitReason.Failed;
        _runStateStore?.MarkFailure(RunStateStatus.FatalShutdown, args.Exception);
        HostLog.Fatal(args.Exception, "Необработанное исключение в UI-потоке");

        // TODO: после фатальной ошибки хост только закрывается – передачу мьютекса новому процессу
        //  уже умеет LaunchReplacement (--restart-after), но падение на старте зациклило бы перезапуск –
        //  заводить вместе со счётчиком падений подряд
        try
        {
            Shutdown(1);
        }
        catch (InvalidOperationException exception)
        {
            HostLog.Error(exception, "Не удалось штатно завершить работу после фатальной ошибки");
        }
    }

    private void ShowFatalErrorNotice()
    {
        try
        {
            var caption = _startupErrorDetails is null ? $"{AppInfo.Name} – критическая ошибка" : $"{AppInfo.Name} – ошибка запуска";
            var message = _startupErrorDetails
                ?? $"Произошла непредвиденная ошибка.\n\nПодробности – в журнале: {_loggingOptions?.LogsDirectory ?? AppPaths.Combine("logs")}";
            MessageBox.Show(message, caption, MessageBoxButton.OK, MessageBoxImage.Error);
        }
        catch (Exception notificationFailure)
        {
            HostLog.Error(notificationFailure, "Не удалось показать системное сообщение о фатальной ошибке");
        }
    }
}
