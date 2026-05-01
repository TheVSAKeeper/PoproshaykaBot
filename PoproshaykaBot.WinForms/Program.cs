using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using PoproshaykaBot.WinForms.Auth;
using PoproshaykaBot.WinForms.Broadcast;
using PoproshaykaBot.WinForms.Broadcast.Profiles;
using PoproshaykaBot.WinForms.Chat;
using PoproshaykaBot.WinForms.Chat.Commands;
using PoproshaykaBot.WinForms.Infrastructure;
using PoproshaykaBot.WinForms.Infrastructure.Di;
using PoproshaykaBot.WinForms.Infrastructure.Events;
using PoproshaykaBot.WinForms.Infrastructure.Hosting;
using PoproshaykaBot.WinForms.Infrastructure.Hosting.Components;
using PoproshaykaBot.WinForms.Polls;
using PoproshaykaBot.WinForms.Server;
using PoproshaykaBot.WinForms.Settings;
using PoproshaykaBot.WinForms.Statistics;
using PoproshaykaBot.WinForms.Streaming;
using PoproshaykaBot.WinForms.Tiles;
using PoproshaykaBot.WinForms.Twitch;
using PoproshaykaBot.WinForms.Twitch.Chat;
using PoproshaykaBot.WinForms.Twitch.EventSub;
using PoproshaykaBot.WinForms.Twitch.Helix;
using PoproshaykaBot.WinForms.Users;
using Serilog;
using System.Diagnostics;
using Timer = System.Windows.Forms.Timer;

namespace PoproshaykaBot.WinForms;

public static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        var isUiSmoke = args.Any(arg => string.Equals(arg, "--ui-smoke", StringComparison.OrdinalIgnoreCase));

        const string OutputTemplate = "[{Timestamp:HH:mm:ss.fff} {Level:u3}] [{SourceContext}] {Message:lj}{NewLine}{Exception}";

        Log.Logger = new LoggerConfiguration()
            .MinimumLevel.Debug()
            .WriteTo.Console(outputTemplate: OutputTemplate)
            .WriteTo.Debug(outputTemplate: OutputTemplate)
            .WriteTo.File(AppPaths.Combine("logs", "bot_log_.txt"), rollingInterval: RollingInterval.Day, outputTemplate: OutputTemplate)
            .CreateLogger();

        try
        {
            Log.Information("Запуск приложения...");
            Log.Information("Режим хранения данных: {Mode}, базовая директория: {BaseDirectory}",
                AppPaths.IsPortable ? "portable" : "AppData",
                AppPaths.BaseDirectory);

            Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
            ApplicationConfiguration.Initialize();

            var memoryCheckTimer = CreateMemoryWatchdogTimer();

            if (!isUiSmoke)
            {
                memoryCheckTimer.Start();
            }

            RunApp(isUiSmoke);
        }
        catch (Exception ex)
        {
            Log.Fatal(ex, "Приложение завершило работу из-за непредвиденной ошибки");
        }
        finally
        {
            Log.Information("Завершение работы приложения");
            Log.CloseAndFlush();
        }
    }

    private static void RunApp(bool isUiSmoke)
    {
        var services = new ServiceCollection();
        ConfigureServices(services);

        var serviceProvider = services.BuildServiceProvider();
        var appLifetime = serviceProvider.GetRequiredService<AppLifetime>();
        var appLifetimeStarted = false;
        try
        {
            serviceProvider.ActivateEventSubscribers(typeof(Program).Assembly);

            var settingsManager = serviceProvider.GetRequiredService<SettingsManager>();
            appLifetimeStarted = StartHttpServerIfNeeded(isUiSmoke, settingsManager, appLifetime);

            var mainForm = serviceProvider.GetRequiredService<MainForm>();
            Application.Run(mainForm);
        }
        finally
        {
            if (appLifetimeStarted)
            {
                StopAppLifetime(appLifetime);
            }

            serviceProvider.DisposeAsync().AsTask().GetAwaiter().GetResult();
        }
    }

    private static bool StartHttpServerIfNeeded(bool isUiSmoke, SettingsManager settingsManager, AppLifetime appLifetime)
    {
        if (isUiSmoke)
        {
            Log.Information("Запуск в режиме UI smoke-теста. HTTP сервер и сетевые подсистемы отключены");
            return false;
        }

        if (!ValidateAndResolvePortConflict(settingsManager))
        {
            Log.Warning("Не удалось разрешить конфликт портов. HTTP сервер не запущен");
            MessageBox.Show("Не удалось разрешить конфликт портов. HTTP сервер не запущен.", "Ошибка", MessageBoxButtons.OK, MessageBoxIcon.Error);
            return false;
        }

        try
        {
            appLifetime.StartAsync(CancellationToken.None).GetAwaiter().GetResult();
            Log.Information("HTTP сервер успешно запущен");
            return true;
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Ошибка запуска HTTP сервера");
            MessageBox.Show($"Ошибка запуска HTTP сервера: {ex.Message}", "Ошибка", MessageBoxButtons.OK, MessageBoxIcon.Error);
            return false;
        }
    }

    private static void StopAppLifetime(AppLifetime appLifetime)
    {
        try
        {
            appLifetime.StopAsync(CancellationToken.None).GetAwaiter().GetResult();
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Ошибка остановки AppLifetime");
        }
    }

    private static Timer CreateMemoryWatchdogTimer()
    {
        const long ThresholdMb = 1024;
        const long MemoryThreshold = 1024 * 1024 * ThresholdMb;
        const int CheckIntervalMs = 1000;
        const int KillDelayMs = 1000;

        var memoryCheckTimer = new Timer { Interval = CheckIntervalMs };
        memoryCheckTimer.Tick += (_, _) =>
        {
            var currentProcess = Process.GetCurrentProcess();
            var memoryBytes = currentProcess.PrivateMemorySize64;

            if (memoryBytes <= MemoryThreshold)
            {
                return;
            }

            Log.Warning("Обнаружено аномальное потребление памяти: {MemoryBytes} байт", memoryBytes);
            memoryCheckTimer.Stop();

            Task.Run(async () =>
            {
                await Task.Delay(KillDelayMs);
                Log.Fatal("Принудительное завершение процесса из-за утечки памяти");
                await Log.CloseAndFlushAsync();
                currentProcess.Kill();
            });

            var memoryMb = memoryBytes / (1024.0 * 1024.0);
            var killDelaySeconds = KillDelayMs / 1000;
            var secondsWord = GetRussianSecondsWord(killDelaySeconds);
            var message =
                $"""
                 Внимание! Обнаружено аномальное потребление ресурсов.

                 Текущее использование: {memoryMb:F2} MB
                 Установленный лимит: {ThresholdMb:F2} MB

                 У вас есть {killDelaySeconds} {secondsWord}, чтобы прочитать это сообщение, затем процесс будет убит принудительно.
                 """;

            MessageBox.Show(message, "Критическая нагрузка на память", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            Environment.FailFast("Пользователь подтвердил закрытие при утечке памяти.");
        };

        return memoryCheckTimer;
    }

    private static void ConfigureServices(IServiceCollection services)
    {
        services.AddLogging(builder =>
        {
            builder.ClearProviders();
            builder.AddSerilog(dispose: true);
        });

        services.AddHttpClient();

        services.AddSingleton(TimeProvider.System);
        services.AddSingleton<InMemoryEventBus>();
        services.AddSingleton<IEventBus>(sp => sp.GetRequiredService<InMemoryEventBus>());
        services.AddSingleton<UiEventDispatcher>();
        services.AddSingleton<AppHost>();

        services.AddSingleton<IControlFactory, ControlFactory>();
        services.AddSingleton<IFormFactory, FormFactory>();

        services.AddSingleton<IHostedComponent, StatisticsHostedComponent>();
        services.AddSingleton<IHostedComponent, ChatDecorationsHostedComponent>();
        services.AddSingleton<IHostedComponent, BroadcastSchedulerHostedComponent>();

        services.AddEventSubscribers(typeof(Program).Assembly);

        services.AddSingleton<SettingsManager>();

        services.AddTransient<BotTwitchAuthHandler>();
        services.AddTransient<BroadcasterTwitchAuthHandler>();

        services.AddHttpClient(TwitchEndpoints.HelixBotClient, client =>
            {
                client.BaseAddress = new(TwitchEndpoints.HelixBaseUrl);
            })
            .AddHttpMessageHandler<BotTwitchAuthHandler>();

        services.AddHttpClient(TwitchEndpoints.HelixBroadcasterClient, client =>
            {
                client.BaseAddress = new(TwitchEndpoints.HelixBaseUrl);
            })
            .AddHttpMessageHandler<BroadcasterTwitchAuthHandler>();

        services.AddKeyedSingleton<ITwitchHelixClient, BotHelixClient>(TwitchEndpoints.HelixBotClient);
        services.AddKeyedSingleton<ITwitchHelixClient, BroadcasterHelixClient>(TwitchEndpoints.HelixBroadcasterClient);

        services.AddSingleton<EventSubChatMessageMapper>();
        services.AddSingleton<ChatIngestionService>();
        services.AddSingleton<IHostedComponent>(sp => sp.GetRequiredService<ChatIngestionService>());
        services.AddSingleton<ChatSender>();
        services.AddSingleton<IHostedComponent>(sp => sp.GetRequiredService<ChatSender>());

        services.AddSingleton<StatisticsCollector>();
        services.AddSingleton<TwitchOAuthService>();
        services.AddSingleton<ChatHistoryManager>();
        services.AddSingleton<SseService>();

        services.AddSingleton<KestrelHttpServer>();
        services.AddSingleton<AppLifetime>();
        services.AddSingleton<IAppLifetimeComponent, KestrelHttpServerLifetimeAdapter>();

        services.AddSingleton<ITwitchEventSubClient, TwitchEventSubClient>();
        services.AddSingleton<EventSubConnectionHost>();
        services.AddSingleton<IHostedComponent>(sp => sp.GetRequiredService<EventSubConnectionHost>());
        services.AddSingleton<StreamStatusManager>();
        services.AddSingleton<IStreamStatus>(sp => sp.GetRequiredService<StreamStatusManager>());
        services.AddSingleton<IHostedComponent>(sp => sp.GetRequiredService<StreamStatusManager>());
        services.AddSingleton<StreamStatusWatchdog>();
        services.AddSingleton<IHostedComponent>(sp => sp.GetRequiredService<StreamStatusWatchdog>());
        services.AddSingleton<ChatDecorationsProvider>();
        services.AddSingleton<UserRankService>();
        services.AddSingleton<UserMessagesManagementService>();

        services.AddSingleton<TwitchChatMessenger>();
        services.AddSingleton<IChatMessenger>(sp => sp.GetRequiredService<TwitchChatMessenger>());
        services.AddSingleton<BroadcastScheduler>();

        services.AddSingleton<ITwitchChannelsApi, TwitchChannelsApiAdapter>();
        services.AddSingleton<ITwitchSearchApi, TwitchSearchApiAdapter>();
        services.AddSingleton<IBroadcasterIdProvider, BroadcasterIdProvider>();
        services.AddSingleton<IBotUserIdProvider, BotUserIdProvider>();
        services.AddSingleton<ChannelUpdateSubscriber>();
        services.AddSingleton<IHostedComponent>(sp => sp.GetRequiredService<ChannelUpdateSubscriber>());
        services.AddSingleton<IChannelUpdateConfirmation>(sp => sp.GetRequiredService<ChannelUpdateConfirmationService>());
        services.AddSingleton<ChannelInformationApplier>();
        services.AddSingleton<IChannelInformationApplier>(sp => sp.GetRequiredService<ChannelInformationApplier>());
        services.AddSingleton<GameCategoryResolver>();
        services.AddSingleton<IGameCategoryResolver>(sp => sp.GetRequiredService<GameCategoryResolver>());
        services.AddSingleton<BroadcastProfilesManager>();

        services.AddSingleton<PollProfilesManager>();
        services.AddSingleton<PollSnapshotStore>();
        services.AddSingleton<PollsAvailabilityService>();
        services.AddSingleton<PollController>();
        services.AddSingleton<IPollController>(sp => sp.GetRequiredService<PollController>());
        services.AddSingleton<PollEventSubscriber>();
        services.AddSingleton<IHostedComponent>(sp => sp.GetRequiredService<PollEventSubscriber>());
        services.AddSingleton<PollHistoryStore>();
        services.AddSingleton<IHostedComponent>(sp => sp.GetRequiredService<PollHistoryStore>());

        services.AddSingleton<AudienceTracker>();

        services.AddSingleton<IChatCommand, HelloCommand>();
        services.AddSingleton<IChatCommand, DonateCommand>();
        services.AddSingleton<IChatCommand, HowManyMessagesCommand>();
        services.AddSingleton<IChatCommand, BotStatsCommand>();
        services.AddSingleton<IChatCommand, TopUsersCommand>();
        services.AddSingleton<IChatCommand, MyProfileCommand>();
        services.AddSingleton<IChatCommand, ActiveUsersCommand>();
        services.AddSingleton<IChatCommand, ByeCommand>();
        services.AddSingleton<IChatCommand, StreamInfoCommand>();
        services.AddSingleton<IChatCommand, TrumpCommand>();
        services.AddSingleton<IChatCommand, RanksCommand>();
        services.AddSingleton<IChatCommand, RankCommand>();
        services.AddSingleton<IChatCommand, ProfileCommand>();
        services.AddSingleton<IChatCommand, TitleCommand>();
        services.AddSingleton<IChatCommand, GameCommand>();

        services.AddSingleton<ChatCommandProcessor>(sp =>
        {
            var commands = sp.GetServices<IChatCommand>().ToList();
            var processor = new ChatCommandProcessor(commands);
            processor.Register(new HelpCommand(processor.GetAllCommands));
            return processor;
        });

        services.AddSingleton<TwitchChatHandler>();
        services.AddSingleton<IChannelProvider>(sp => sp.GetRequiredService<TwitchChatHandler>());

        services.AddSingleton<DashboardTileType, StreamInfoTileType>();
        services.AddSingleton<DashboardTileType, BroadcastStatusTileType>();
        services.AddSingleton<DashboardTileType, LogsTileType>();
        services.AddSingleton<DashboardTileType, TwitchChatTileType>();
        services.AddSingleton<DashboardTileType, ChatOverlayPreviewTileType>();
        services.AddSingleton<DashboardTileType, BroadcastProfilesTileType>();
        services.AddSingleton<DashboardTileType, PollsControlTileType>();
        services.AddSingleton<IDashboardTileCatalog, DashboardTileCatalog>();

        services.AddSingleton<BotConnectionManager>();
        services.AddSingleton<MainForm>();

        services.AddTransient<SettingsForm>();
        services.AddTransient<UserStatisticsForm>();
        services.AddTransient<BroadcastProfileEditDialog>();
        services.AddTransient<PollFromProfileDialog>();
        services.AddTransient<PollProfileEditDialog>();
    }

    private static string GetRussianSecondsWord(int seconds)
    {
        var mod100 = seconds % 100;
        if (mod100 is >= 11 and <= 14)
        {
            return "секунд";
        }

        return (seconds % 10) switch
        {
            1 => "секунда",
            2 or 3 or 4 => "секунды",
            _ => "секунд",
        };
    }

    private static bool ValidateAndResolvePortConflict(SettingsManager settingsManager)
    {
        var settings = settingsManager.Current;
        var redirectUri = settings.Twitch.RedirectUri;
        var serverPort = settings.Twitch.HttpServerPort;

        if (!Uri.TryCreate(redirectUri, UriKind.Absolute, out var uri))
        {
            Log.Error("Некорректный RedirectUri: {RedirectUri}", redirectUri);
            MessageBox.Show($"Некорректный RedirectUri: {redirectUri}\n\nПожалуйста, исправьте URI в настройках OAuth.",
                "Ошибка конфигурации",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);

            return false;
        }

        int redirectPort;
        if (uri.Port == -1)
        {
            redirectPort = uri.Scheme == "https" ? 443 : 80;
        }
        else
        {
            redirectPort = uri.Port;
        }

        if (redirectPort == serverPort)
        {
            return true;
        }

        Log.Information("Конфликт портов. Обновление порта с {OldPort} на {NewPort}", serverPort, redirectPort);
        settings.Twitch.HttpServerPort = redirectPort;
        settingsManager.SaveSettings(settings);

        var message = $"""
                       Обнаружен конфликт портов:

                       • RedirectUri использует порт: {redirectPort}
                       • HTTP сервер был настроен на порт: {serverPort}

                       Для корректной работы OAuth порт HTTP сервера был автоматически обновлен до {redirectPort}.

                       Если вы хотите использовать другой порт, пожалуйста, измените его вручную в настройках HTTP сервера и RedirectUri.
                       """;

        MessageBox.Show(message,
            "Порт обновлен",
            MessageBoxButtons.OK,
            MessageBoxIcon.Information);

        return true;
    }
}
