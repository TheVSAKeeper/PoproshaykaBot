using PoproshaykaBot.Core.Infrastructure.Hosting;
using PoproshaykaBot.Core.Infrastructure.Runtime;
using PoproshaykaBot.Core.Server;
using PoproshaykaBot.Core.Settings;
using PoproshaykaBot.Core.Update;
using PoproshaykaBot.Wpf.Bootstrap;
using Serilog;
using Serilog.Extensions.Logging;
using System.Diagnostics;
using System.Windows;

namespace PoproshaykaBot.Wpf;

public partial class App
{
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
            HostLog.Error(exception, "Ошибка очистки после обновления");
        }
    }

    private static bool ApplyPendingUpdate()
    {
        var executablePath = Environment.ProcessPath;

        if (string.IsNullOrEmpty(executablePath))
        {
            return false;
        }

        using var loggerFactory = new SerilogLoggerFactory(Log.Logger);
        var logger = loggerFactory.CreateLogger(nameof(UpdateApplier));

        try
        {
            return UpdateApplier.TryApplyPending(UpdatePaths.StagingDirectory(executablePath), executablePath, logger);
        }
        catch (Exception exception)
        {
            HostLog.Error(exception, "Ошибка применения запланированного обновления");
            return false;
        }
    }

    internal static bool TryRequestRestart()
    {
        return Interlocked.CompareExchange(ref _restartRequested, 1, 0) == 0;
    }

    internal static void CancelRestart()
    {
        Interlocked.Exchange(ref _restartRequested, 0);
    }

    internal static bool TryRestartForced()
    {
        if (!LaunchReplacement())
        {
            return false;
        }

        _runStateStore?.ClearCleanExit();
        HostLog.Information("Завершение работы приложения ({Reason}), код выхода {ExitCode}", AppExitReason.ForcedRestart, -1);
        Log.CloseAndFlush();
        Process.GetCurrentProcess().Kill();
        return true;
    }

    private static bool LaunchReplacement()
    {
        var executablePath = Environment.ProcessPath;

        if (string.IsNullOrEmpty(executablePath))
        {
            HostLog.Error("Перезапуск невозможен: путь к исполняемому файлу приложения неизвестен");
            return false;
        }

        var arguments = AppRestart.BuildArguments(StartupArguments, Environment.ProcessId);

        if (!AppRestart.TryLaunch(executablePath, arguments, false, out var failure))
        {
            HostLog.Error(failure, "Не удалось запустить новый экземпляр приложения для перезапуска");
            return false;
        }

        HostLog.Information("Запущен новый экземпляр приложения, он дождётся завершения текущего");
        return true;
    }

    private static void ReportRestartHandoff(int previousProcessId, bool exited)
    {
        if (exited)
        {
            HostLog.Information("Перезапуск: предыдущий экземпляр (PID {ProcessId}) завершился", previousProcessId);
            return;
        }

        HostLog.Warning("Перезапуск: предыдущий экземпляр (PID {ProcessId}) не завершился за {Seconds} с, запуск идёт как обычно",
            previousProcessId,
            (int)AppRestart.HandoffTimeout.TotalSeconds);
    }

    private static void ReportEnvironment(IReadOnlyList<string> arguments, PreviousRunState previousRun)
    {
        using var loggerFactory = new SerilogLoggerFactory(Log.Logger);
        StartupReport.LogEnvironment(loggerFactory.CreateLogger(nameof(StartupReport)), arguments, AppBuildBadge.StartupLine, previousRun);
    }

    private static void ReportConfiguration(IServiceProvider serviceProvider)
    {
        using var loggerFactory = new SerilogLoggerFactory(Log.Logger);
        StartupReport.LogConfiguration(loggerFactory.CreateLogger(nameof(StartupReport)), serviceProvider);
    }

    internal static async Task StartStreamMonitoringAsync(
        IEnumerable<StreamMonitoringHost> hosts, ICollection<StreamMonitoringHost> started)
    {
        foreach (var host in hosts)
        {
            try
            {
                await host.StartAsync(CancellationToken.None);
                started.Add(host);
                HostLog.Information("Стрим-мониторинг площадки {Platform} запущен независимо от подключения бота", host.Platform);
            }
            catch (Exception ex)
            {
                HostLog.Error(ex, "Ошибка запуска стрим-мониторинга площадки {Platform}", host.Platform);

                if (host.IsRunning)
                {
                    using var rollbackCts = new CancellationTokenSource(TimeSpan.FromSeconds(6));
                    await StopStreamMonitoringHostAsync(host, rollbackCts.Token);
                }
            }
        }
    }

    private static void StopStreamMonitoring(IReadOnlyList<StreamMonitoringHost> hosts, CancellationToken cancellationToken)
    {
        for (var i = hosts.Count - 1; i >= 0; i--)
        {
            StopStreamMonitoringHostAsync(hosts[i], cancellationToken).GetAwaiter().GetResult();
        }
    }

    private static async Task StopStreamMonitoringHostAsync(StreamMonitoringHost host, CancellationToken cancellationToken)
    {
        try
        {
            await host.StopAsync(cancellationToken);
        }
        catch (OperationCanceledException exception)
        {
            HostLog.Warning(exception, "Остановка стрим-мониторинга площадки {Platform} прервана по таймауту завершения", host.Platform);
        }
        catch (Exception ex)
        {
            HostLog.Error(ex, "Ошибка остановки стрим-мониторинга площадки {Platform}", host.Platform);
        }
    }

    internal static async Task StartAppLifetimeAsync(AppLifetime appLifetime)
    {
        try
        {
            await appLifetime.StartAsync(CancellationToken.None);
        }
        catch (Exception ex)
        {
            HostLog.Error(ex, "Ошибка запуска фоновых компонентов приложения");
            using var rollbackCts = new CancellationTokenSource(TimeSpan.FromSeconds(6));
            await StopAppLifetimeAsync(appLifetime, rollbackCts.Token);
        }
    }

    private static async Task<bool> StartHttpServerIfNeededAsync(SettingsManager settingsManager, KestrelHttpServer kestrelServer)
    {
        using var loggerFactory = new SerilogLoggerFactory(Log.Logger);
        var reconcile = HttpServerPortReconciler.Reconcile(settingsManager, loggerFactory.CreateLogger(nameof(HttpServerPortReconciler)));

        if (reconcile.Notice is { } notice)
        {
            StyledMessageBox.Show(notice.Message, notice.Title, MessageBoxButton.OK, ToMessageBoxImage(notice.Severity));
        }

        if (!reconcile.IsResolved)
        {
            HostLog.Warning("Не удалось разрешить конфликт портов. HTTP сервер не запущен");
            StyledMessageBox.Show("Не удалось разрешить конфликт портов. HTTP сервер не запущен.", "Ошибка", MessageBoxButton.OK, MessageBoxImage.Error);
            return false;
        }

        try
        {
            await kestrelServer.StartAsync();
            HostLog.Information("HTTP сервер успешно запущен");
            return true;
        }
        catch (Exception ex)
        {
            HostLog.Error(ex, "Ошибка запуска HTTP сервера");
            StyledMessageBox.Show($"Ошибка запуска HTTP сервера: {ex.Message}", "Ошибка", MessageBoxButton.OK, MessageBoxImage.Error);
            return false;
        }
    }

    private static void StopHttpServer(KestrelHttpServer kestrelServer)
    {
        try
        {
            if (kestrelServer.IsRunning)
            {
                kestrelServer.StopAsync().GetAwaiter().GetResult();
            }
        }
        catch (Exception ex)
        {
            HostLog.Error(ex, "Ошибка остановки HTTP сервера");
        }
    }

    private static async Task StopAppLifetimeAsync(AppLifetime appLifetime, CancellationToken cancellationToken)
    {
        try
        {
            await appLifetime.StopAsync(cancellationToken);
        }
        catch (OperationCanceledException exception)
        {
            HostLog.Warning(exception, "Остановка AppLifetime прервана по таймауту завершения");
        }
        catch (Exception ex)
        {
            HostLog.Error(ex, "Ошибка остановки AppLifetime");
        }
    }

    private static MessageBoxImage ToMessageBoxImage(PortReconcileSeverity severity)
    {
        return severity switch
        {
            PortReconcileSeverity.Information => MessageBoxImage.Information,
            PortReconcileSeverity.Warning => MessageBoxImage.Warning,
            PortReconcileSeverity.Error => MessageBoxImage.Error,
            _ => MessageBoxImage.None,
        };
    }
}
