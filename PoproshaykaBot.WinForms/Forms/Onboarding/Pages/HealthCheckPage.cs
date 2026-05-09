using Microsoft.Extensions.Logging;
using PoproshaykaBot.Core.Chat;
using PoproshaykaBot.Core.Infrastructure.Events;
using PoproshaykaBot.Core.Infrastructure.Events.Chat;
using PoproshaykaBot.Core.Users;
using PoproshaykaBot.WinForms.Infrastructure.Di;
using System.Diagnostics;

namespace PoproshaykaBot.WinForms.Forms.Onboarding.Pages;

public sealed partial class HealthCheckPage : OnboardingPageBase
{
    private readonly List<IDisposable> _subs = [];

    private OnboardingContext? _context;
    private bool _initialized;
    private bool _webViewNavigationRequested;
    private string? _currentWebViewUrl;
    private string? _detectedBotStatus;

    public HealthCheckPage()
    {
        InitializeComponent();
    }

    [Inject]
    public IChatMessenger ChatMessenger { get; internal init; } = null!;

    [Inject]
    public IEventBus EventBus { get; internal init; } = null!;

    [Inject]
    public ILogger<HealthCheckPage> Logger { get; internal init; } = null!;

    public override string PageTitle => "Диагностика (опционально)";

    public override void OnEnter(OnboardingContext context)
    {
        _context = context;
        SetCanAdvance(true);

        var url = $"http://localhost:{context.Settings.Twitch.HttpServerPort}/chat";
        _overlayUrlLabel.Text = $"Адрес для Browser Source в OBS: {url}";
        ResetStatuses();

        if (!_webViewNavigationRequested || !string.Equals(_currentWebViewUrl, url, StringComparison.Ordinal))
        {
            _webViewNavigationRequested = true;
            _currentWebViewUrl = url;
            _ = InitializeWebViewAsync(url);
        }
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);

        if (_initialized || this.IsInDesignMode())
        {
            return;
        }

        _initialized = true;

        _subs.Add(EventBus.SubscribeOnUi<ChatMessageReceived>(this, OnChatMessageReceived));
        _subs.DisposeOnClose(this);
    }

    private void OnChatTestButtonClicked(object? sender, EventArgs e)
    {
        RunChatTest();
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

    private static string DescribeBotStatus(UserStatus status)
    {
        if (status.HasFlag(UserStatus.Broadcaster))
        {
            return "владелец канала";
        }

        if (status.HasFlag(UserStatus.Moderator))
        {
            return "модератор";
        }

        return "обычный пользователь";
    }

    private void OnChatMessageReceived(ChatMessageReceived @event)
    {
        if (_context is null || _detectedBotStatus is not null)
        {
            return;
        }

        var botUserId = _context.BotAccount.UserId;
        if (string.IsNullOrWhiteSpace(botUserId)
            || !string.Equals(@event.UserId, botUserId, StringComparison.Ordinal))
        {
            return;
        }

        var status = DescribeBotStatus(@event.Status);
        _detectedBotStatus = status;
        _botStatusLabel.Text = $"Бот в чате как: {status}";
        _botStatusLabel.ForeColor = Color.Green;
    }

    private async Task InitializeWebViewAsync(string url)
    {
        try
        {
            await _chatPreviewWebView.EnsureCoreWebView2Async(null);

            if (IsDisposed || _chatPreviewWebView.IsDisposed)
            {
                return;
            }

            _chatPreviewWebView.CoreWebView2.Navigate(url);
        }
        catch (Exception exception)
        {
            Logger.LogWarning(exception, "Не удалось инициализировать WebView2 для предпросмотра чата");

            if (IsDisposed)
            {
                return;
            }

            _chatPreviewWebView.Visible = false;
            _previewFallbackLabel.Visible = true;
        }
    }

    private void ResetStatuses()
    {
        _chatTestStatusLabel.Text = "Не выполнено";
        _chatTestStatusLabel.ForeColor = Color.Gray;

        if (_detectedBotStatus is null)
        {
            _botStatusLabel.Text = "Бот в чате как: ожидание сообщения...";
            _botStatusLabel.ForeColor = Color.Gray;
        }
    }

    private void RunChatTest()
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

        try
        {
            ChatMessenger.Send(message);
            _chatTestStatusLabel.Text = "✓ Сообщение поставлено в очередь отправки.";
            _chatTestStatusLabel.ForeColor = Color.Green;
        }
        catch (Exception exception)
        {
            Logger.LogWarning(exception, "Сбой постановки тестового сообщения в очередь отправки");
            _chatTestStatusLabel.Text = "✗ Не удалось поставить сообщение в очередь.";
            _chatTestStatusLabel.ForeColor = Color.DarkRed;
        }
    }
}
