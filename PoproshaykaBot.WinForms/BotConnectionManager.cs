using System.ComponentModel;

namespace PoproshaykaBot.WinForms;

public class BotConnectionManager : IDisposable
{
    private readonly BackgroundWorker _backgroundWorker;
    private readonly Func<string, Bot> _botFactory;
    private readonly Func<Task<string?>> _tokenProvider;
    private bool _disposed;

    public BotConnectionManager(Func<string, Bot> botFactory, Func<Task<string?>> tokenProvider)
    {
        _botFactory = botFactory;
        _tokenProvider = tokenProvider;

        _backgroundWorker = new()
        {
            WorkerReportsProgress = true,
            WorkerSupportsCancellation = true,
        };

        _backgroundWorker.DoWork += BackgroundWorker_DoWork;
        _backgroundWorker.ProgressChanged += BackgroundWorker_ProgressChanged;
        _backgroundWorker.RunWorkerCompleted += BackgroundWorker_RunWorkerCompleted;
    }

    public event EventHandler<BotConnectionResult>? ConnectionCompleted;

    public event EventHandler<string>? ProgressChanged;

    public bool IsBusy => _backgroundWorker.IsBusy;

    public void StartConnection()
    {
        if (_backgroundWorker.IsBusy)
        {
            throw new InvalidOperationException("Connection is already in progress");
        }

        _backgroundWorker.RunWorkerAsync();
    }

    public void CancelConnection()
    {
        if (_backgroundWorker.IsBusy)
        {
            _backgroundWorker.CancelAsync();
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _backgroundWorker.CancelAsync();
        _backgroundWorker.Dispose();
        _disposed = true;
    }

    private void BackgroundWorker_DoWork(object? sender, DoWorkEventArgs args)
    {
        if (sender is not BackgroundWorker worker)
        {
            return;
        }

        try
        {
            worker.ReportProgress(0, "Получение токена доступа...");

            var accessTokenTask = _tokenProvider();

            while (accessTokenTask.IsCompleted == false)
            {
                if (worker.CancellationPending)
                {
                    args.Cancel = true;
                    return;
                }

                Thread.Sleep(100);
            }

            if (accessTokenTask.IsFaulted)
            {
                throw accessTokenTask.Exception?.InnerException ?? new Exception("Ошибка получения токена доступа");
            }

            var accessToken = accessTokenTask.Result;

            if (string.IsNullOrWhiteSpace(accessToken))
            {
                throw new InvalidOperationException("Не удалось получить токен доступа. Проверьте настройки OAuth.");
            }

            worker.ReportProgress(10, "Создание экземпляра бота...");
            var bot = _botFactory(accessToken);

            worker.ReportProgress(20, "Инициализация подключения...");
            args.Result = bot;

            worker.ReportProgress(30, "Подключение к серверу Twitch...");

            var cancellationTokenSource = new CancellationTokenSource();

            var connectionTask = Task.Run(async () =>
            {
                await bot.ConnectAsync(cancellationTokenSource.Token);
            }, cancellationTokenSource.Token);

            while (connectionTask.IsCompleted == false)
            {
                if (worker.CancellationPending)
                {
                    cancellationTokenSource.Cancel();
                    args.Cancel = true;
                    return;
                }

                worker.ReportProgress(50, "Ожидание подтверждения подключения...");
                Thread.Sleep(500);
            }

            if (connectionTask.IsFaulted)
            {
                throw connectionTask.Exception?.InnerException ?? new Exception("Неизвестная ошибка подключения");
            }

            worker.ReportProgress(90, "Подключение установлено успешно");
            worker.ReportProgress(100, "Инициализация завершена");
        }
        catch (OperationCanceledException)
        {
            args.Cancel = true;
        }
        catch (Exception exception)
        {
            args.Result = exception;
        }
    }

    private void BackgroundWorker_ProgressChanged(object? sender, ProgressChangedEventArgs args)
    {
        if (args.UserState is string message)
        {
            ProgressChanged?.Invoke(this, message);
        }
    }

    private void BackgroundWorker_RunWorkerCompleted(object? sender, RunWorkerCompletedEventArgs args)
    {
        BotConnectionResult result;

        if (args.Cancelled)
        {
            result = BotConnectionResult.Cancelled();
        }
        else if (args.Error != null || args.Result is Exception)
        {
            var exception = args.Error ?? (Exception)args.Result!;
            result = BotConnectionResult.Failed(exception);
        }
        else if (args.Result is Bot bot)
        {
            result = BotConnectionResult.Success(bot);
        }
        else
        {
            result = BotConnectionResult.Failed(new InvalidOperationException("Неожиданный результат операции подключения"));
        }

        ConnectionCompleted?.Invoke(this, result);
    }
}

public class BotConnectionResult
{
    private BotConnectionResult(BotConnectionStatus status, Bot? bot = null, Exception? exception = null)
    {
        Status = status;
        Bot = bot;
        Exception = exception;
    }

    public Bot? Bot { get; }

    public Exception? Exception { get; }

    public bool IsSuccess => Status == BotConnectionStatus.Success;

    public bool IsCancelled => Status == BotConnectionStatus.Cancelled;

    public bool IsFailed => Status == BotConnectionStatus.Failed;

    private BotConnectionStatus Status { get; }

    public static BotConnectionResult Success(Bot bot)
    {
        return new(BotConnectionStatus.Success, bot);
    }

    public static BotConnectionResult Cancelled()
    {
        return new(BotConnectionStatus.Cancelled);
    }

    public static BotConnectionResult Failed(Exception exception)
    {
        return new(BotConnectionStatus.Failed, exception: exception);
    }
}

public enum BotConnectionStatus
{
    Success,
    Cancelled,
    Failed,
}
