using Microsoft.Extensions.DependencyInjection;
using PoproshaykaBot.Core.Broadcast;
using PoproshaykaBot.Core.Chat;
using PoproshaykaBot.Core.Infrastructure;
using PoproshaykaBot.Core.Infrastructure.Di;
using PoproshaykaBot.Core.Infrastructure.Events;
using PoproshaykaBot.Core.Infrastructure.Hosting;
using PoproshaykaBot.Core.Infrastructure.Logging;
using PoproshaykaBot.Core.Obs;
using PoproshaykaBot.Core.Polls;
using PoproshaykaBot.Core.Server;
using PoproshaykaBot.Core.Settings;
using PoproshaykaBot.Core.Settings.Migrations;
using PoproshaykaBot.Core.Statistics;
using PoproshaykaBot.Core.Streaming;
using PoproshaykaBot.Core.Twitch;
using PoproshaykaBot.Core.Update;
using PoproshaykaBot.Wpf.Bootstrap;
using PoproshaykaBot.Wpf.ViewModels;
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
using System.Windows;

namespace PoproshaykaBot.Wpf;

public partial class App : Application
{
    private const string OutputTemplate = "[{Timestamp:HH:mm:ss.fff} {Level:u3}] [{SourceContext}] {Message:lj}{NewLine}{Exception}";

    private ServiceProvider? _services;
    private KeepShellLogging? _logging;
    private AppLifetime? _appLifetime;
    private StreamMonitoringHost? _streamMonitoringHost;
    private Timer? _memoryWatchdog;
    private Mutex? _singleInstanceMutex;

    private bool _isUiSmoke;
    private bool _isFinalizeUpdate;
    private bool _appLifetimeStarted;
    private bool _streamMonitoringStarted;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        _isUiSmoke = e.Args.Any(arg => string.Equals(arg, "--ui-smoke", StringComparison.OrdinalIgnoreCase));
        _isFinalizeUpdate = e.Args.Any(arg => string.Equals(arg, UpdateApplier.FinalizeArgument, StringComparison.OrdinalIgnoreCase));

        var coreUiLogSink = new UiLogSink();
        SelfLog.Enable(message => Debug.WriteLine($"[Serilog] {message}"));

        _logging = KeepShellLogging.Bootstrap(new()
        {
            LogsDirectory = AppPaths.Combine("logs"),
            FileNamePrefix = AppInfo.LogFilePrefix,
            OutputTemplate = OutputTemplate,
            MinimumLevel = LogEventLevel.Debug,
            MinimumLevelOverrides = new Dictionary<string, LogEventLevel>
            {
                ["Microsoft"] = LogEventLevel.Information,
                ["Microsoft.AspNetCore"] = LogEventLevel.Warning,
                ["System.Net.Http.HttpClient"] = LogEventLevel.Warning,
            },
            WriteToDebug = true,
            Configure = configuration => configuration
                .WriteTo.Console(outputTemplate: OutputTemplate)
                .WriteTo.Sink(coreUiLogSink, LogEventLevel.Information),
        });

        Log.Information(AppInfo.SessionStartMarker + "...");

        AttachFatalExceptionTrap();

        StyledMessageBox.DefaultTitle = AppInfo.Name;
        ShutdownMode = ShutdownMode.OnExplicitShutdown;

        try
        {
            var uiSettingsPath = AppPaths.SettingsFile("ui-preferences.toml");
            ISettingsStore uiSettings = new SettingsStore(uiSettingsPath);

            AppThemes.Register();
            var themeKey = uiSettings.GetStringValue(SettingsKeys.Theme);
            ThemeManager.Apply(string.IsNullOrWhiteSpace(themeKey) ? AppThemes.LightKey : themeKey);
            FontScaleManager.Initialize(uiSettings.GetDouble(SettingsKeys.FontScale, FontScaleManager.DefaultScale));

            _singleInstanceMutex = AcquireSingleInstanceLock(_isFinalizeUpdate);

            if (_singleInstanceMutex is null)
            {
                Log.Information("Обнаружен уже запущенный экземпляр приложения. Завершение работы");

                if (!_isUiSmoke)
                {
                    StyledMessageBox.Show("PoproshaykaBot уже запущен.\n\nОдновременно может работать только один экземпляр приложения.",
                        "Приложение уже запущено",
                        MessageBoxButton.OK,
                        MessageBoxImage.Information);
                }

                Shutdown();
                return;
            }

            Log.Information("Режим хранения данных: {Mode}, базовая директория: {BaseDirectory}", ResolveStorageMode(), AppPaths.BaseDirectory);

            if (_isFinalizeUpdate)
            {
                FinalizeUpdate();
            }

            MigrateLegacySettingsLayout();

            ViewLocator.InstallIntoApplication();

            var services = new ServiceCollection();
            ConfigureServices(services, coreUiLogSink, uiSettings);
            _services = services.BuildServiceProvider();

            _services.ActivateEventSubscribers(typeof(InfrastructureServiceCollectionExtensions).Assembly);

            _appLifetime = _services.GetRequiredService<AppLifetime>();
            _streamMonitoringHost = _services.GetRequiredService<StreamMonitoringHost>();

            if (_isUiSmoke)
            {
                Log.Information("Запуск в режиме UI smoke-теста. HTTP сервер и сетевые подсистемы отключены");
            }
            else
            {
                var settingsManager = _services.GetRequiredService<SettingsManager>();
                _appLifetimeStarted = StartHttpServerIfNeeded(settingsManager, _appLifetime);
                _streamMonitoringStarted = StartStreamMonitoring(_streamMonitoringHost);
                _memoryWatchdog = CreateMemoryWatchdog();
            }

            var window = _services.GetRequiredService<MainWindow>();
            MainWindow = window;
            window.Show();

            ShutdownMode = ShutdownMode.OnMainWindowClose;

            MaybeLaunchOnboardingWizard();
        }
        catch (Exception ex)
        {
            Log.Fatal(ex, "{App} не смог запуститься", AppInfo.Name);
            StyledMessageBox.Show(ex.ToString(), $"{AppInfo.Name} – ошибка запуска", MessageBoxButton.OK, MessageBoxImage.Error);
            Shutdown(1);
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _services?.GetService<ISettingsStore>()?.Flush();

        if (_services is not null)
        {
            Task.Run(StopAllComponents).GetAwaiter().GetResult();
        }

        if (!_isUiSmoke)
        {
            ApplyPendingUpdate();
        }

        Log.Information("Завершение работы приложения");
        _logging?.Dispose();

        _memoryWatchdog?.Dispose();
        _singleInstanceMutex?.Dispose();

        base.OnExit(e);
    }

    private static string ResolveStorageMode()
    {
        if (AppPaths.IsBaseDirectoryOverridden)
        {
            return "override";
        }

        return AppPaths.IsPortable ? "portable" : "AppData";
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
            Log.Error(exception, "Ошибка пред-миграции layout-а настроек");
        }
    }

    private static void ConfigureServices(IServiceCollection services, UiLogSink uiLogSink, ISettingsStore uiSettings)
    {
        services.AddSingleton(uiSettings);

        services
            .AddCoreInfrastructure(uiLogSink, disposeSerilog: false)
            .AddStatistics()
            .AddSettingsStores()
            .AddTwitchClients()
            .AddChatPipeline()
            .AddStreamMonitoring()
            .AddBroadcasting()
            .AddPolls()
            .AddHttpServer()
            .AddObsIntegration()
            .AddSelfUpdate();

        services.AddSingleton<BotConnectionManager>();

        services.AddKeepShell();

        services.AddSingleton(new ErrorReportOptions
        {
            IssueRepo = AppInfo.RepoSlug,
            LogFileGlobs = [AppInfo.LogFileGlob],
            SessionStartMarker = AppInfo.SessionStartMarker,
        });
        services.AddSingleton<ErrorReportService>();

        services.AddSingleton<DashboardTileViewModel, LogsTileViewModel>();
        services.AddSingleton<DashboardTileViewModel, StreamInfoTileViewModel>();
        services.AddSingleton<DashboardTileViewModel, BroadcastStatusTileViewModel>();
        services.AddSingleton<DashboardTileViewModel, BroadcastProfilesTileViewModel>();
        services.AddSingleton<DashboardTileViewModel, PollsTileViewModel>();
        services.AddSingleton<DashboardTileViewModel, ObsInfoTileViewModel>();
        services.AddSingleton<DashboardTileViewModel, ChatDisplayTileViewModel>();
        services.AddSingleton<DashboardTileViewModel, ChatOverlayPreviewTileViewModel>();

        services.AddSingleton<DashboardViewModel>();
        services.AddSingleton<UserStatisticsPageViewModel>();
        services.AddSingleton<StreamHistoryPageViewModel>();
        services.AddSingleton<ThemeViewModel>();
        services.AddSingleton<ShellPreferences>();
        services.AddSingleton<ShellViewModel>();
        services.AddSingleton<MainWindow>();

        services.AddTransient<GameAutocompleteViewModel>();
        services.AddTransient<BroadcastProfileEditDialogViewModel>();
        services.AddTransient<PollProfileEditDialogViewModel>();
        services.AddTransient<PollFromProfileDialogViewModel>();

        services.AddTransient<BasicSettingsSectionViewModel>();
        services.AddTransient<RateLimitingSettingsViewModel>();
        services.AddTransient<MessagesSettingsSectionViewModel>();
        services.AddTransient<HttpServerSectionViewModel>();
        services.AddTransient<OAuthSettingsViewModel>();
        services.AddSingleton<ObsChatSettingsSectionViewModel>();
        services.AddSingleton<ObsIntegrationSectionViewModel>();
        services.AddTransient<AutoBroadcastSettingsViewModel>();
        services.AddTransient<BotLifecycleAutomationSectionViewModel>();
        services.AddTransient<MiscSettingsSectionViewModel>();
        services.AddSingleton<PollsSettingsSectionViewModel>();
        services.AddSingleton<UpdateSettingsSectionViewModel>();
        services.AddTransient<DashboardLayoutSectionViewModel>();

        services.AddSingleton<SettingsPageViewModel>();

        services.AddTransient<IOnboardingPageViewModel, WelcomePageViewModel>();
        services.AddTransient<IOnboardingPageViewModel, CredentialsPageViewModel>();
        services.AddTransient<IOnboardingPageViewModel, HttpServerCheckPageViewModel>();
        services.AddTransient<IOnboardingPageViewModel, BotAuthorizationPageViewModel>();
        services.AddTransient<IOnboardingPageViewModel, BroadcasterAuthorizationPageViewModel>();
        services.AddTransient<IOnboardingPageViewModel, BotConnectionPageViewModel>();
        services.AddTransient<IOnboardingPageViewModel, HealthCheckPageViewModel>();
        services.AddTransient<IOnboardingPageViewModel, CompletionPageViewModel>();

        services.AddTransient<OnboardingWizardViewModel>();
        services.AddTransient<OnboardingWizardWindow>();
    }

    private void StopAllComponents()
    {
        using var shutdownWatchdog = _isUiSmoke ? null : CreateShutdownWatchdog();
        using var shutdownTimeout = _isUiSmoke ? null : new CancellationTokenSource(TimeSpan.FromSeconds(ShutdownSoftDeadlineSeconds));
        var shutdownToken = shutdownTimeout?.Token ?? CancellationToken.None;

        if (_streamMonitoringStarted && _streamMonitoringHost is not null)
        {
            StopStreamMonitoring(_streamMonitoringHost, shutdownToken);
        }

        if (_appLifetimeStarted && _appLifetime is not null)
        {
            StopAppLifetime(_appLifetime, shutdownToken);
        }

        _services!.DisposeAsync().AsTask().GetAwaiter().GetResult();
    }

    private void AttachFatalExceptionTrap()
    {
        DispatcherUnhandledException += (_, args) => Log.Fatal(args.Exception, "Необработанное исключение в UI-потоке");

        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
        {
            if (args.ExceptionObject is Exception exception)
            {
                Log.Fatal(exception, "Приложение завершило работу из-за непредвиденной ошибки");
            }

            Log.CloseAndFlush();
        };
    }
}
