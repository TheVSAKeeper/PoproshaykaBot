using Microsoft.Extensions.Logging;
using PoproshaykaBot.Core.Infrastructure.Events;
using PoproshaykaBot.Core.Infrastructure.Events.Lifecycle;
using PoproshaykaBot.Core.Infrastructure.Hosting;
using PoproshaykaBot.Core.Settings;
using PoproshaykaBot.Core.Settings.Stores;
using PoproshaykaBot.Core.Twitch.Auth;
using PoproshaykaBot.WinForms.Infrastructure.Di;

namespace PoproshaykaBot.WinForms.Forms.Onboarding.Pages;

public sealed partial class BotConnectionPage : OnboardingPageBase
{
    private readonly List<IDisposable> _subs = [];

    private OnboardingContext? _context;
    private bool _initialized;
    private BotLifecyclePhase _currentPhase = BotLifecyclePhase.Idle;
    private string? _joinedChannel;
    private string _lastStatusMessage = string.Empty;

    public BotConnectionPage()
    {
        InitializeComponent();
    }

    [Inject]
    public BotConnectionManager BotConnectionManager { get; internal init; } = null!;

    [Inject]
    public IEventBus EventBus { get; internal init; } = null!;

    [Inject]
    public AccountsStore AccountsStore { get; internal init; } = null!;

    [Inject]
    public SettingsManager SettingsManager { get; internal init; } = null!;

    [Inject]
    public ILogger<BotConnectionPage> Logger { get; internal init; } = null!;

    public override string PageTitle => "Подключение бота";

    public override void OnEnter(OnboardingContext context)
    {
        _context = context;
        _currentPhase = BotConnectionManager.CurrentPhase;
        SetCanAdvance(_currentPhase == BotLifecyclePhase.Connected);

        try
        {
            SettingsManager.SaveSettings(context.Settings);
            AccountsStore.SaveAll(context.BotAccount, context.BroadcasterAccount);
        }
        catch (Exception exception)
        {
            Logger.LogError(exception, "Не удалось сохранить настройки и токены перед подключением бота");
            SetStatus("✗ Не удалось сохранить настройки", Color.DarkRed, exception.Message);
            _retryButton.Visible = true;
            return;
        }

        _ = EventBus.PublishAsync(new TwitchAuthorizationRefreshed(TwitchOAuthRole.Bot));
        _ = EventBus.PublishAsync(new TwitchAuthorizationRefreshed(TwitchOAuthRole.Broadcaster));

        UpdateUiForPhase();

        if (BotConnectionManager.IsBusy)
        {
            return;
        }

        if (_currentPhase == BotLifecyclePhase.Connected)
        {
            return;
        }

        TryStartConnection();
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);

        if (_initialized || this.IsInDesignMode())
        {
            return;
        }

        _initialized = true;

        _subs.Add(EventBus.SubscribeOnUi<BotLifecyclePhaseChanged>(this, OnPhaseChanged));
        _subs.Add(EventBus.SubscribeOnUi<BotConnectionStatusUpdated>(this, OnStatusUpdated));
        _subs.Add(EventBus.SubscribeOnUi<BotJoinedChannel>(this, OnJoinedChannel));
        _subs.DisposeOnClose(this);
    }

    private void OnRetryButtonClicked(object? sender, EventArgs e)
    {
        if (BotConnectionManager.IsBusy)
        {
            return;
        }

        _retryButton.Visible = false;
        TryStartConnection();
    }

    private static string SafeFailureMessage(Exception exception)
    {
        return exception switch
        {
            OperationCanceledException => "Подключение отменено",
            HttpRequestException => "Ошибка сети при подключении к Twitch",
            InvalidOperationException invalid => invalid.Message,
            _ => "Неизвестная ошибка подключения",
        };
    }

    private void OnPhaseChanged(BotLifecyclePhaseChanged @event)
    {
        _currentPhase = @event.Phase;

        if (@event.Phase == BotLifecyclePhase.Failed && @event.Exception is not null)
        {
            _lastStatusMessage = SafeFailureMessage(@event.Exception);
        }

        UpdateUiForPhase();
    }

    private void OnStatusUpdated(BotConnectionStatusUpdated @event)
    {
        _lastStatusMessage = @event.Message;
        _detailsLabel.Text = @event.Message;
    }

    private void OnJoinedChannel(BotJoinedChannel @event)
    {
        _joinedChannel = @event.Channel;
        UpdateUiForPhase();
    }

    private void TryStartConnection()
    {
        try
        {
            BotConnectionManager.StartConnection();
        }
        catch (InvalidOperationException exception)
        {
            Logger.LogWarning(exception, "Запуск подключения отклонён: уже выполняется");
        }
    }

    private void UpdateUiForPhase()
    {
        switch (_currentPhase)
        {
            case BotLifecyclePhase.Connecting:
                SetStatus("🔌 Подключение...", Color.Blue, _lastStatusMessage);
                _retryButton.Visible = false;
                SetCanAdvance(false);
                break;

            case BotLifecyclePhase.Connected:
                var channel = string.IsNullOrWhiteSpace(_joinedChannel)
                    ? _context?.Settings.Twitch.Channel ?? string.Empty
                    : _joinedChannel;

                var channelText = string.IsNullOrWhiteSpace(channel) ? string.Empty : $" к чату @{channel}";
                SetStatus($"✅ Бот подключён{channelText}", Color.Green, "Можно идти дальше.");
                _retryButton.Visible = false;
                SetCanAdvance(true);
                break;

            case BotLifecyclePhase.Disconnecting:
                SetStatus("⏳ Отключение...", Color.Orange, _lastStatusMessage);
                _retryButton.Visible = false;
                SetCanAdvance(false);
                break;

            case BotLifecyclePhase.Disconnected:
                SetStatus("Бот отключён", Color.Gray, "Нажмите «Повторить подключение».");
                _retryButton.Visible = true;
                SetCanAdvance(false);
                break;

            case BotLifecyclePhase.Cancelled:
                SetStatus("❌ Подключение отменено", Color.DarkOrange, "Нажмите «Повторить подключение».");
                _retryButton.Visible = true;
                SetCanAdvance(false);
                break;

            case BotLifecyclePhase.Failed:
                SetStatus("✗ Не удалось подключиться", Color.DarkRed, _lastStatusMessage);
                _retryButton.Visible = true;
                SetCanAdvance(false);
                break;

            case BotLifecyclePhase.Idle:
            default:
                SetStatus("Готовимся к подключению...", Color.Blue, _lastStatusMessage);
                _retryButton.Visible = false;
                SetCanAdvance(false);
                break;
        }
    }

    private void SetStatus(string status, Color color, string details)
    {
        _statusLabel.Text = status;
        _statusLabel.ForeColor = color;
        _detailsLabel.Text = details;
    }
}
