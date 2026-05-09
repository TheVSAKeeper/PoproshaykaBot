using Microsoft.Extensions.Logging;
using PoproshaykaBot.Core.Twitch.Onboarding;
using PoproshaykaBot.WinForms.Infrastructure.Di;
using System.Diagnostics;

namespace PoproshaykaBot.WinForms.Forms.Onboarding.Pages;

public sealed partial class HealthCheckPage : OnboardingPageBase
{
    private OnboardingContext? _context;
    private CancellationTokenSource? _chatTestCts;
    private CancellationTokenSource? _previewCts;
    private IAsyncDisposable? _chatPreviewSession;
    private bool _previewStartRequested;

    public HealthCheckPage()
    {
        InitializeComponent();
        Disposed += OnPageDisposed;
    }

    [Inject]
    public IOnboardingHealthChecker HealthChecker { get; internal init; } = null!;

    [Inject]
    public ILogger<HealthCheckPage> Logger { get; internal init; } = null!;

    public override string PageTitle => "Диагностика (опционально)";

    public override void OnEnter(OnboardingContext context)
    {
        _context = context;
        SetCanAdvance(true);

        _overlayUrlLabel.Text = $"Адрес для Browser Source в OBS: http://localhost:{context.Settings.Twitch.HttpServerPort}/chat";
        ResetStatuses();

        if (!_previewStartRequested)
        {
            _previewStartRequested = true;
            _previewCts = new();
            _ = StartChatPreviewAsync(_previewCts.Token);
        }
    }

    private void OnPageDisposed(object? sender, EventArgs e)
    {
        _chatTestCts?.Cancel();
        _chatTestCts?.Dispose();
        _chatTestCts = null;

        _previewCts?.Cancel();
        _previewCts?.Dispose();
        _previewCts = null;

        var session = _chatPreviewSession;
        _chatPreviewSession = null;
        if (session is not null)
        {
            _ = DisposePreviewSessionAsync(session);
        }
    }

    private async void OnChatTestButtonClicked(object? sender, EventArgs e)
    {
        await RunChatTestAsync();
    }

    private void OnOverlayButtonClicked(object? sender, EventArgs e)
    {
        if (_context is null)
        {
            return;
        }

        var url = $"http://localhost:{_context.Settings.Twitch.HttpServerPort}/chat";

        try
        {
            using var process = Process.Start(new ProcessStartInfo
            {
                FileName = url,
                UseShellExecute = true,
            });
        }
        catch (Exception exception)
        {
            Logger.LogWarning(exception, "Не удалось открыть OBS-оверлей в браузере");
            MessageBox.Show(this,
                $"Не удалось открыть браузер. Скопируйте адрес вручную:{Environment.NewLine}{url}",
                "OBS-оверлей",
                MessageBoxButtons.OK,
                MessageBoxIcon.Warning);
        }
    }

    private static async Task DisposePreviewSessionAsync(IAsyncDisposable session)
    {
        try
        {
            await session.DisposeAsync();
        }
        catch
        {
        }
    }

    private async Task StartChatPreviewAsync(CancellationToken cancellationToken)
    {
        if (_context is null)
        {
            return;
        }

        var broadcasterId = _context.BroadcasterAccount.UserId;
        var botId = _context.BotAccount.UserId;
        var clientId = _context.Settings.Twitch.ClientId;
        var token = _context.BotAccount.AccessToken;

        if (string.IsNullOrWhiteSpace(broadcasterId)
            || string.IsNullOrWhiteSpace(botId)
            || string.IsNullOrWhiteSpace(clientId)
            || string.IsNullOrWhiteSpace(token))
        {
            return;
        }

        try
        {
            var result = await HealthChecker.StartChatPreviewAsync(broadcasterId,
                botId,
                clientId,
                token,
                cancellationToken);

            if (cancellationToken.IsCancellationRequested || IsDisposed)
            {
                if (result.Session is not null)
                {
                    await DisposePreviewSessionAsync(result.Session);
                }

                return;
            }

            if (result.Status == OnboardingChatPreviewStatus.Started && result.Session is not null)
            {
                _chatPreviewSession = result.Session;
                Logger.LogDebug("Onboarding: подписка channel.chat.message для предпросмотра активна");
            }
            else
            {
                Logger.LogDebug("Onboarding: предпросмотр чата не запущен (статус {Status})", result.Status);
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception exception)
        {
            Logger.LogDebug(exception, "Onboarding: исключение при запуске предпросмотра чата");
        }
    }

    private void ResetStatuses()
    {
        _chatTestStatusLabel.Text = "Не выполнено";
        _chatTestStatusLabel.ForeColor = Color.Gray;
    }

    private async Task RunChatTestAsync()
    {
        if (_context is null)
        {
            return;
        }

        var message = _chatTestMessageTextBox.Text.Trim();
        if (string.IsNullOrEmpty(message))
        {
            _chatTestStatusLabel.Text = "Введите текст сообщения.";
            _chatTestStatusLabel.ForeColor = Color.DarkOrange;
            return;
        }

        var broadcasterId = _context.BroadcasterAccount.UserId;
        var senderId = _context.BotAccount.UserId;
        var clientId = _context.Settings.Twitch.ClientId;
        var token = _context.BotAccount.AccessToken;

        if (string.IsNullOrWhiteSpace(broadcasterId)
            || string.IsNullOrWhiteSpace(senderId)
            || string.IsNullOrWhiteSpace(token))
        {
            _chatTestStatusLabel.Text = "Сначала пройдите авторизацию бота и стримера.";
            _chatTestStatusLabel.ForeColor = Color.DarkOrange;
            return;
        }

        _chatTestCts?.Cancel();
        _chatTestCts?.Dispose();
        _chatTestCts = new();

        _chatTestButton.Enabled = false;
        _chatTestStatusLabel.Text = "Отправка...";
        _chatTestStatusLabel.ForeColor = Color.Blue;

        try
        {
            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(_chatTestCts.Token);
            timeoutCts.CancelAfter(TimeSpan.FromSeconds(8));

            var outcome = await HealthChecker.SendChatTestMessageAsync(broadcasterId,
                senderId,
                clientId,
                token,
                message,
                timeoutCts.Token);

            switch (outcome)
            {
                case ChatTestMessageOutcome.Sent:
                    _chatTestStatusLabel.Text = "✓ Сообщение отправлено.";
                    _chatTestStatusLabel.ForeColor = Color.Green;
                    break;

                case ChatTestMessageOutcome.Forbidden:
                    _chatTestStatusLabel.Text = "✗ Twitch отклонил отправку. Проверьте scope user:write:chat и доступ бота к чату.";
                    _chatTestStatusLabel.ForeColor = Color.DarkRed;
                    break;

                case ChatTestMessageOutcome.Skipped:
                    _chatTestStatusLabel.Text = "Отправка пропущена.";
                    _chatTestStatusLabel.ForeColor = Color.Gray;
                    break;

                default:
                    _chatTestStatusLabel.Text = "Не удалось отправить (сетевая ошибка).";
                    _chatTestStatusLabel.ForeColor = Color.DarkOrange;
                    break;
            }
        }
        catch (Exception exception)
        {
            Logger.LogDebug(exception, "Сбой отправки тестового сообщения в onboarding");
            _chatTestStatusLabel.Text = "Не удалось отправить.";
            _chatTestStatusLabel.ForeColor = Color.DarkOrange;
        }
        finally
        {
            if (!IsDisposed)
            {
                _chatTestButton.Enabled = true;
            }
        }
    }

}
