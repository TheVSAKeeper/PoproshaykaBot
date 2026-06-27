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
using PoproshaykaBot.Wpf.ViewModels;
using PoproshaykaBot.Wpf.Views;
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

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        var uiLogSink = new UiLogSink();
        SelfLog.Enable(message => Debug.WriteLine($"[Serilog] {message}"));
        Log.Logger = BuildLogger(uiLogSink);
        Log.Information("Запуск приложения (WPF)...");
        Log.Information("Режим хранения данных: {Mode}, базовая директория: {BaseDirectory}", ResolveStorageMode(), AppPaths.BaseDirectory);

        MigrateLegacySettingsLayout();

        StyledMessageBox.DefaultTitle = "PoproshaykaBot";

        ThemeManager.Register(ThemeManager.DefaultLight);
        ThemeManager.Register(ThemeManager.DefaultDark);
        ThemeManager.Apply("light");
        FontScaleManager.Initialize(FontScaleManager.DefaultScale);
        ViewLocator.InstallIntoApplication();

        var services = new ServiceCollection();
        ConfigureServices(services, uiLogSink);
        _services = services.BuildServiceProvider();

        _services.ActivateEventSubscribers(typeof(InfrastructureServiceCollectionExtensions).Assembly);

        var window = _services.GetRequiredService<MainWindow>();
        MainWindow = window;
        window.Show();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _services?.DisposeAsync().AsTask().GetAwaiter().GetResult();
        Log.Information("Завершение работы приложения");
        Log.CloseAndFlush();
        base.OnExit(e);
    }

    private static Serilog.Core.Logger BuildLogger(UiLogSink uiLogSink)
    {
        return new LoggerConfiguration()
            .MinimumLevel.Debug()
            .MinimumLevel.Override("Microsoft", LogEventLevel.Information)
            .MinimumLevel.Override("Microsoft.AspNetCore", LogEventLevel.Warning)
            .MinimumLevel.Override("System.Net.Http.HttpClient", LogEventLevel.Warning)
            .WriteTo.Console(outputTemplate: OutputTemplate)
            .WriteTo.Debug(outputTemplate: OutputTemplate)
            .WriteTo.File(AppPaths.Combine("logs", "bot_log_.txt"),
                rollingInterval: RollingInterval.Day,
                fileSizeLimitBytes: 50L * 1024 * 1024,
                rollOnFileSizeLimit: true,
                retainedFileCountLimit: 31,
                outputTemplate: OutputTemplate)
            .WriteTo.Sink(uiLogSink, LogEventLevel.Information)
            .CreateLogger();
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

    private static void ConfigureServices(IServiceCollection services, UiLogSink uiLogSink)
    {
        services
            .AddCoreInfrastructure(uiLogSink)
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
        services.AddSingleton<SpikePageViewModel>();
        services.AddSingleton<ShellViewModel>();
        services.AddSingleton<MainWindow>();
    }
}
