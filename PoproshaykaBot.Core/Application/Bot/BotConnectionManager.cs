using PoproshaykaBot.Core.Domain.Models.Connection;

namespace PoproshaykaBot.Core.Application.Bot;

/// <summary>
/// Менеджер подключения бота с поддержкой async/await.
/// </summary>
public class BotConnectionManager : IDisposable
{
    private readonly Func<string, Bot> _botFactory;
    private readonly Func<Task<string?>> _tokenProvider;
    private CancellationTokenSource? _cancellationTokenSource;
    private Task? _connectionTask;
    private bool _disposed;

    public BotConnectionManager(Func<string, Bot> botFactory, Func<Task<string?>> tokenProvider)
    {
        _botFactory = botFactory ?? throw new ArgumentNullException(nameof(botFactory));
        _tokenProvider = tokenProvider ?? throw new ArgumentNullException(nameof(tokenProvider));
    }

    public event EventHandler<BotConnectionResult>? ConnectionCompleted;

    public event EventHandler<string>? ProgressChanged;

    public bool IsBusy => _connectionTask is { IsCompleted: false };

    public void StartConnection()
    {
        if (IsBusy)
        {
            throw new InvalidOperationException("Connection is already in progress");
        }

        _cancellationTokenSource = new();
        var progress = new Progress<string>(message => ProgressChanged?.Invoke(this, message));

        _connectionTask = Task.Run(async () => await ConnectAsync(progress, _cancellationTokenSource.Token));
    }

    public void CancelConnection()
    {
        _cancellationTokenSource?.Cancel();
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        CancelConnection();
        _cancellationTokenSource?.Dispose();
        _disposed = true;
    }

    private async Task ConnectAsync(IProgress<string> progress, CancellationToken cancellationToken)
    {
        BotConnectionResult result;

        try
        {
            progress.Report("Получение токена доступа...");
            var accessTokenTask = _tokenProvider();

            while (!accessTokenTask.IsCompleted)
            {
                cancellationToken.ThrowIfCancellationRequested();
                await Task.Delay(100, cancellationToken);
            }

            var accessToken = await accessTokenTask;

            if (string.IsNullOrWhiteSpace(accessToken))
            {
                throw new InvalidOperationException("Не удалось получить токен доступа. Проверьте настройки OAuth.");
            }

            progress.Report("Создание экземпляра бота...");
            var bot = _botFactory(accessToken);

            progress.Report("Инициализация подключения...");
            progress.Report("Подключение к серверу Twitch...");

            var connectionTask = Task.Run(async () =>
            {
                await bot.ConnectAsync(cancellationToken);
            }, cancellationToken);

            while (!connectionTask.IsCompleted)
            {
                cancellationToken.ThrowIfCancellationRequested();
                progress.Report("Ожидание подтверждения подключения...");
                await Task.Delay(500, cancellationToken);
            }

            if (connectionTask.IsFaulted)
            {
                throw connectionTask.Exception?.InnerException ?? new Exception("Неизвестная ошибка подключения");
            }

            progress.Report("Подключение установлено успешно");
            progress.Report("Инициализация завершена");

            result = BotConnectionResult.Success(bot);
        }
        catch (OperationCanceledException)
        {
            result = BotConnectionResult.Cancelled();
        }
        catch (Exception exception)
        {
            result = BotConnectionResult.Failed(exception);
        }

        ConnectionCompleted?.Invoke(this, result);
    }
}
