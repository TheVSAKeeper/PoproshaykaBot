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
using PoproshaykaBot.WinForms.Infrastructure.Di;
using Serilog;
using Serilog.Debugging;
using Serilog.Events;
using Serilog.Extensions.Logging;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using Timer = System.Threading.Timer;

namespace PoproshaykaBot.WinForms;

public static class Program
{
    private const int ShutdownSoftDeadlineSeconds = 8;

    private const int ShutdownHardDeadlineSeconds = 12;

    private static int _memoryWatchdogBusy;

    [STAThread]
    private static void Main(string[] args)
    {
        var isUiSmoke = args.Any(arg => string.Equals(arg, "--ui-smoke", StringComparison.OrdinalIgnoreCase));
        var isFinalizeUpdate = args.Any(arg => string.Equals(arg, UpdateApplier.FinalizeArgument, StringComparison.OrdinalIgnoreCase));

        const string OutputTemplate = "[{Timestamp:HH:mm:ss.fff} {Level:u3}] [{SourceContext}] {Message:lj}{NewLine}{Exception}";

        var uiLogSink = new UiLogSink();

        SelfLog.Enable(message => Debug.WriteLine($"[Serilog] {message}"));

        Log.Logger = new LoggerConfiguration()
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

        Mutex? singleInstanceMutex = null;

        try
        {
            Log.Information("Запуск приложения...");

            singleInstanceMutex = AcquireSingleInstanceLock(isFinalizeUpdate);

            if (singleInstanceMutex is null)
            {
                Log.Information("Обнаружен уже запущенный экземпляр приложения. Завершение работы");

                if (!isUiSmoke)
                {
                    MessageBox.Show("PoproshaykaBot уже запущен.\n\nОдновременно может работать только один экземпляр приложения.",
                        "Приложение уже запущено",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Information);
                }

                return;
            }

            Log.Information("Режим хранения данных: {Mode}, базовая директория: {BaseDirectory}",
                ResolveStorageMode(),
                AppPaths.BaseDirectory);

            if (isFinalizeUpdate)
            {
                FinalizeUpdate();
            }

            Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
            ApplicationConfiguration.Initialize();

            using var memoryWatchdog = isUiSmoke ? null : CreateMemoryWatchdog();

            RunApp(isUiSmoke, uiLogSink);
        }
        catch (Exception ex)
        {
            Log.Fatal(ex, "Приложение завершило работу из-за непредвиденной ошибки");
        }
        finally
        {
            Log.Information("Завершение работы приложения");
            Log.CloseAndFlush();
            singleInstanceMutex?.Dispose();
        }
    }

    private static void RunApp(bool isUiSmoke, UiLogSink uiLogSink)
    {
        MigrateLegacySettingsLayout();

        var services = new ServiceCollection();
        ConfigureServices(services, uiLogSink);

        var serviceProvider = services.BuildServiceProvider();
        var appLifetime = serviceProvider.GetRequiredService<AppLifetime>();
        var streamMonitoringHost = serviceProvider.GetRequiredService<StreamMonitoringHost>();
        var appLifetimeStarted = false;
        var streamMonitoringStarted = false;
        try
        {
            serviceProvider.ActivateEventSubscribers(typeof(InfrastructureServiceCollectionExtensions).Assembly);

            var settingsManager = serviceProvider.GetRequiredService<SettingsManager>();
            appLifetimeStarted = StartHttpServerIfNeeded(isUiSmoke, settingsManager, appLifetime);

            if (!isUiSmoke)
            {
                streamMonitoringStarted = StartStreamMonitoring(streamMonitoringHost);
            }

            var mainForm = serviceProvider.GetRequiredService<MainForm>();
            Application.Run(mainForm);
        }
        finally
        {
            StopAllComponents(serviceProvider, appLifetime, streamMonitoringHost, appLifetimeStarted, streamMonitoringStarted, isUiSmoke);

            if (!isUiSmoke)
            {
                ApplyPendingUpdate();
            }
        }
    }

    private static void StopAllComponents(
        ServiceProvider serviceProvider,
        AppLifetime appLifetime,
        StreamMonitoringHost streamMonitoringHost,
        bool appLifetimeStarted,
        bool streamMonitoringStarted,
        bool isUiSmoke)
    {
        using var shutdownWatchdog = isUiSmoke ? null : CreateShutdownWatchdog();

        using var shutdownTimeout = isUiSmoke ? null : new CancellationTokenSource(TimeSpan.FromSeconds(ShutdownSoftDeadlineSeconds));
        var shutdownToken = shutdownTimeout?.Token ?? CancellationToken.None;

        if (streamMonitoringStarted)
        {
            StopStreamMonitoring(streamMonitoringHost, shutdownToken);
        }

        if (appLifetimeStarted)
        {
            StopAppLifetime(appLifetime, shutdownToken);
        }

        serviceProvider.DisposeAsync().AsTask().GetAwaiter().GetResult();
    }

    private static void FinalizeUpdate()
    {
        using var loggerFactory = new SerilogLoggerFactory(Log.Logger);
        var logger = loggerFactory.CreateLogger(nameof(UpdateFinalizer));

        try
        {
            var executablePath = Environment.ProcessPath;

            if (string.IsNullOrEmpty(executablePath))
            {
                return;
            }

            UpdateFinalizer.Run(UpdatePaths.StagingDirectory(executablePath), executablePath, logger);
        }
        catch (Exception exception)
        {
            Log.Error(exception, "Ошибка очистки после обновления");
        }
    }

    private static void ApplyPendingUpdate()
    {
        var executablePath = Environment.ProcessPath;

        if (string.IsNullOrEmpty(executablePath))
        {
            return;
        }

        using var loggerFactory = new SerilogLoggerFactory(Log.Logger);
        var logger = loggerFactory.CreateLogger(nameof(UpdateApplier));

        try
        {
            UpdateApplier.TryApplyPending(UpdatePaths.StagingDirectory(executablePath), executablePath, logger);
        }
        catch (Exception exception)
        {
            Log.Error(exception, "Ошибка применения запланированного обновления");
        }
    }

    private static void MigrateLegacySettingsLayout()
    {
        var loggerFactory = new SerilogLoggerFactory(Log.Logger);
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

    private static bool StartStreamMonitoring(StreamMonitoringHost host)
    {
        try
        {
            host.StartAsync(CancellationToken.None).GetAwaiter().GetResult();
            Log.Information("Стрим-мониторинг запущен независимо от подключения бота");
            return true;
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Ошибка запуска стрим-мониторинга");
            return false;
        }
    }

    private static void StopStreamMonitoring(StreamMonitoringHost host, CancellationToken cancellationToken)
    {
        try
        {
            host.StopAsync(cancellationToken).GetAwaiter().GetResult();
        }
        catch (OperationCanceledException)
        {
            Log.Warning("Остановка стрим-мониторинга прервана по таймауту завершения");
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Ошибка остановки стрим-мониторинга");
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

    private static void StopAppLifetime(AppLifetime appLifetime, CancellationToken cancellationToken)
    {
        try
        {
            appLifetime.StopAsync(cancellationToken).GetAwaiter().GetResult();
        }
        catch (OperationCanceledException)
        {
            Log.Warning("Остановка AppLifetime прервана по таймауту завершения");
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Ошибка остановки AppLifetime");
        }
    }

    private static string ResolveStorageMode()
    {
        if (AppPaths.IsBaseDirectoryOverridden)
        {
            return "override";
        }

        return AppPaths.IsPortable ? "portable" : "AppData";
    }

    private static Mutex? AcquireSingleInstanceLock(bool isFinalizeUpdate)
    {
        var mutex = new Mutex(true, BuildSingleInstanceMutexName(), out var createdNew);

        if (createdNew || isFinalizeUpdate)
        {
            return mutex;
        }

        mutex.Dispose();
        return null;
    }

    private static string BuildSingleInstanceMutexName()
    {
        var key = AppPaths.BaseDirectory.ToLowerInvariant();
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(key)));
        return $"Local\\PoproshaykaBot-{hash[..16]}";
    }

    private static Timer CreateMemoryWatchdog()
    {
        const long BytesPerMb = 1024 * 1024;
        const long SelfThresholdMb = 1024;
        const long TotalThresholdMb = 2048;
        const int LogEveryNthCheck = 30;
        var checkInterval = TimeSpan.FromSeconds(2);

        var checkCount = 0;

        return new(_ =>
            {
                if (Interlocked.Exchange(ref _memoryWatchdogBusy, 1) == 1)
                {
                    return;
                }

                try
                {
                    var selfBytes = SelfMemoryBytes();
                    var childBytes = ChildProcessMemoryBytes(out var childCount);
                    var totalBytes = selfBytes + childBytes;

                    if (++checkCount % LogEveryNthCheck == 1)
                    {
                        Log.Information("Память: процесс {SelfMb} МБ, дочерние {ChildMb} МБ ({ChildCount} проц.), всего {TotalMb} МБ",
                            selfBytes / BytesPerMb, childBytes / BytesPerMb, childCount, totalBytes / BytesPerMb);
                    }

                    if (selfBytes <= SelfThresholdMb * BytesPerMb && totalBytes <= TotalThresholdMb * BytesPerMb)
                    {
                        return;
                    }

                    Log.Fatal("Аномальное потребление памяти: процесс {SelfMb} МБ, дочерние {ChildMb} МБ ({ChildCount} проц.), всего {TotalMb} МБ — принудительное завершение процесса",
                        selfBytes / BytesPerMb, childBytes / BytesPerMb, childCount, totalBytes / BytesPerMb);

                    Log.CloseAndFlush();
                    Process.GetCurrentProcess().Kill();
                }
                finally
                {
                    Interlocked.Exchange(ref _memoryWatchdogBusy, 0);
                }
            },
            null,
            checkInterval,
            checkInterval);
    }

    private static long SelfMemoryBytes()
    {
        using var process = Process.GetCurrentProcess();
        return process.PrivateMemorySize64;
    }

    private static long ChildProcessMemoryBytes(out int count)
    {
        count = 0;
        var parentByPid = SnapshotParentMap();

        if (parentByPid.Count == 0)
        {
            return 0;
        }

        var childrenByParent = new Dictionary<int, List<int>>();
        foreach (var (pid, parentPid) in parentByPid)
        {
            if (!childrenByParent.TryGetValue(parentPid, out var siblings))
            {
                siblings = [];
                childrenByParent[parentPid] = siblings;
            }

            siblings.Add(pid);
        }

        long totalBytes = 0;
        var pending = new Queue<int>();
        var visited = new HashSet<int> { Environment.ProcessId };
        pending.Enqueue(Environment.ProcessId);

        while (pending.Count > 0)
        {
            if (!childrenByParent.TryGetValue(pending.Dequeue(), out var children))
            {
                continue;
            }

            foreach (var childPid in children)
            {
                if (!visited.Add(childPid))
                {
                    continue;
                }

                pending.Enqueue(childPid);

                try
                {
                    using var child = Process.GetProcessById(childPid);
                    totalBytes += child.PrivateMemorySize64;
                    count++;
                }
                catch (Exception exception)
                {
                    Log.Debug(exception, "Watchdog: процесс {Pid} недоступен для замера памяти", childPid);
                }
            }
        }

        return totalBytes;
    }

    private static Dictionary<int, int> SnapshotParentMap()
    {
        const uint Th32csSnapprocess = 0x00000002;
        var map = new Dictionary<int, int>();
        var snapshot = CreateToolhelp32Snapshot(Th32csSnapprocess, 0);

        if (snapshot == IntPtr.Zero || snapshot == new IntPtr(-1))
        {
            return map;
        }

        try
        {
            var entry = new ProcessEntry32 { dwSize = (uint)Marshal.SizeOf<ProcessEntry32>() };

            if (!Process32First(snapshot, ref entry))
            {
                return map;
            }

            do
            {
                map[(int)entry.th32ProcessID] = (int)entry.th32ParentProcessID;
            } while (Process32Next(snapshot, ref entry));
        }
        finally
        {
            CloseHandle(snapshot);
        }

        return map;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr CreateToolhelp32Snapshot(uint dwFlags, uint th32ProcessID);

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool Process32First(IntPtr hSnapshot, ref ProcessEntry32 lppe);

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool Process32Next(IntPtr hSnapshot, ref ProcessEntry32 lppe);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CloseHandle(IntPtr hObject);

    private static Timer CreateShutdownWatchdog()
    {
        return new(_ =>
            {
                Log.Fatal("Завершение работы превысило лимит {Seconds} c — принудительное завершение процесса", ShutdownHardDeadlineSeconds);
                Log.CloseAndFlush();
                Process.GetCurrentProcess().Kill();
            },
            null,
            TimeSpan.FromSeconds(ShutdownHardDeadlineSeconds),
            Timeout.InfiniteTimeSpan);
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
            .AddSelfUpdate()
            .AddDashboardTiles()
            .AddForms();
    }

    private static bool ValidateAndResolvePortConflict(SettingsManager settingsManager)
    {
        var settings = settingsManager.Current;
        var redirectUri = settings.Twitch.RedirectUri;
        var serverPort = settings.Twitch.HttpServerPort;

        if (!RedirectUriPortResolver.TryResolve(redirectUri, out var redirectPort))
        {
            Log.Error("Некорректный RedirectUri: {RedirectUri}", redirectUri);
            MessageBox.Show($"Некорректный RedirectUri: {redirectUri}\n\nПожалуйста, исправьте URI в настройках OAuth.",
                "Ошибка конфигурации",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);

            return false;
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

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct ProcessEntry32
    {
        public uint dwSize;
        public uint cntUsage;
        public uint th32ProcessID;
        public IntPtr th32DefaultHeapID;
        public uint th32ModuleID;
        public uint cntThreads;
        public uint th32ParentProcessID;
        public int pcPriClassBase;
        public uint dwFlags;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)]
        public string szExeFile;
    }
}
