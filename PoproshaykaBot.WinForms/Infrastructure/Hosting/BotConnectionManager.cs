using Microsoft.Extensions.Logging;
using PoproshaykaBot.WinForms.Auth;
using PoproshaykaBot.WinForms.Chat;
using PoproshaykaBot.WinForms.Infrastructure.Events;
using PoproshaykaBot.WinForms.Infrastructure.Events.Lifecycle;
using PoproshaykaBot.WinForms.Infrastructure.Events.Logging;
using PoproshaykaBot.WinForms.Settings;

namespace PoproshaykaBot.WinForms.Infrastructure.Hosting;

public sealed class BotConnectionManager : IDisposable
{
    private readonly TwitchOAuthService _tokenService;
    private readonly SettingsManager _settingsManager;
    private readonly TwitchChatHandler _twitchChatHandler;
    private readonly AppHost _appHost;
    private readonly IEventBus _eventBus;
    private readonly ILogger<BotConnectionManager> _logger;

    private CancellationTokenSource? _cts;
    private Task? _connectionTask;
    private bool _disposed;

    public BotConnectionManager(
        TwitchOAuthService tokenService,
        SettingsManager settingsManager,
        TwitchChatHandler twitchChatHandler,
        AppHost appHost,
        IEventBus eventBus,
        ILogger<BotConnectionManager> logger)
    {
        _tokenService = tokenService;
        _settingsManager = settingsManager;
        _twitchChatHandler = twitchChatHandler;
        _appHost = appHost;
        _eventBus = eventBus;
        _logger = logger;

        _logger.LogDebug("Менеджер подключений бота инициализирован");
    }

    public bool IsBusy => _connectionTask is { IsCompleted: false };

    public void StartConnection()
    {
        _logger.LogDebug("Попытка запуска подключения");

        if (IsBusy)
        {
            _logger.LogWarning("Попытка запуска подключения отклонена: процесс уже выполняется");
            throw new InvalidOperationException("Connection is already in progress");
        }

        _logger.LogInformation("Начат процесс подключения бота");

        _cts?.Dispose();
        _cts = new();

        _connectionTask = ConnectAsync(_cts.Token);
    }

    public void CancelConnection()
    {
        if (_cts == null || _cts.IsCancellationRequested)
        {
            return;
        }

        _logger.LogInformation("Пользователь запросил отмену подключения");
        _cts.Cancel();
    }

    public async Task StopAsync()
    {
        _logger.LogDebug("Инициализация процесса остановки бота (StopAsync)");

        await _eventBus.PublishAsync(new BotLifecyclePhaseChanged(BotLifecyclePhase.Disconnecting));

        var progressReporter = new Progress<string>(ReportProgress);

        try
        {
            await _appHost.StopAsync(progressReporter, CancellationToken.None);
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "Произошла ошибка при остановке компонентов AppHost");
            ReportProgress($"Ошибка остановки компонентов: {exception.Message}");
        }

        _twitchChatHandler.Reset();
        _logger.LogInformation("Бот успешно остановлен");
        PublishPhase(BotLifecyclePhase.Disconnected);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _logger.LogDebug("Освобождение ресурсов BotConnectionManager (Dispose)");

        CancelConnection();

        _cts?.Dispose();
        _disposed = true;
    }

    private async Task ConnectAsync(CancellationToken ct)
    {
        PublishPhase(BotLifecyclePhase.Connecting);

        try
        {
            ReportProgress("Получение токена доступа...");
            _logger.LogDebug("Запрос токена доступа");

            var accessToken = await _tokenService.GetValidTokenOrRefreshAsync(TwitchOAuthRole.Bot, ct);

            ct.ThrowIfCancellationRequested();

            if (string.IsNullOrWhiteSpace(accessToken))
            {
                _logger.LogError("Не удалось получить токен доступа (токен пуст или null)");
                throw new InvalidOperationException("Токен бота отсутствует или недействителен. Авторизуйте бота в настройках Twitch (вкладка «Бот»).");
            }

            ReportProgress("Запуск компонентов бота...");
            var settings = _settingsManager.Current.Twitch;

            _logger.LogInformation("Запуск AppHost для канала {Channel}", settings.Channel);

            var progressReporter = new Progress<string>(ReportProgress);
            await _appHost.StartAsync(progressReporter, ct);

            _ = _eventBus.PublishAsync(new BotJoinedChannel(settings.Channel), ct);

            ReportProgress("Подключение установлено успешно");
            _logger.LogInformation("Процесс подключения бота успешно завершен");
            PublishPhase(BotLifecyclePhase.Connected);
        }
        catch (OperationCanceledException)
        {
            _logger.LogWarning("Процесс подключения бота был отменен");
            PublishPhase(BotLifecyclePhase.Cancelled);
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "Произошла ошибка в процессе подключения бота");
            ReportProgress($"Ошибка подключения: {exception.Message}");
            PublishPhase(BotLifecyclePhase.Failed, exception);
        }
    }

    private void ReportProgress(string message)
    {
        _ = _eventBus.PublishAsync(new BotConnectionStatusUpdated(message));
        _ = _eventBus.PublishAsync(new BotLogEntry(BotLogLevel.Information,
            nameof(BotConnectionManager),
            message));
    }

    private void PublishPhase(BotLifecyclePhase phase, Exception? exception = null)
    {
        _ = _eventBus.PublishAsync(new BotLifecyclePhaseChanged(phase, exception));
    }
}
