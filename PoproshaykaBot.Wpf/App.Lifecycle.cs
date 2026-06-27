using PoproshaykaBot.Core.Infrastructure;
using PoproshaykaBot.Core.Infrastructure.Hosting;
using PoproshaykaBot.Core.Server;
using PoproshaykaBot.Core.Settings;
using PoproshaykaBot.Core.Update;
using Serilog;
using Serilog.Extensions.Logging;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Windows;
using Timer = System.Threading.Timer;

namespace PoproshaykaBot.Wpf;

public partial class App
{
    private const int ShutdownSoftDeadlineSeconds = 8;

    private const int ShutdownHardDeadlineSeconds = 12;

    private static int _memoryWatchdogBusy;

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

    private static bool StartHttpServerIfNeeded(SettingsManager settingsManager, AppLifetime appLifetime)
    {
        if (!ValidateAndResolvePortConflict(settingsManager))
        {
            Log.Warning("Не удалось разрешить конфликт портов. HTTP сервер не запущен");
            StyledMessageBox.Show("Не удалось разрешить конфликт портов. HTTP сервер не запущен.", "Ошибка", MessageBoxButton.OK, MessageBoxImage.Error);
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
            StyledMessageBox.Show($"Ошибка запуска HTTP сервера: {ex.Message}", "Ошибка", MessageBoxButton.OK, MessageBoxImage.Error);
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

    private static bool ValidateAndResolvePortConflict(SettingsManager settingsManager)
    {
        var settings = settingsManager.Current;
        var redirectUri = settings.Twitch.RedirectUri;
        var serverPort = settings.Twitch.HttpServerPort;

        if (!RedirectUriPortResolver.TryResolve(redirectUri, out var redirectPort))
        {
            Log.Error("Некорректный RedirectUri: {RedirectUri}", redirectUri);
            StyledMessageBox.Show($"Некорректный RedirectUri: {redirectUri}\n\nПожалуйста, исправьте URI в настройках OAuth.",
                "Ошибка конфигурации",
                MessageBoxButton.OK,
                MessageBoxImage.Error);

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

        StyledMessageBox.Show(message,
            "Порт обновлен",
            MessageBoxButton.OK,
            MessageBoxImage.Information);

        return true;
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
