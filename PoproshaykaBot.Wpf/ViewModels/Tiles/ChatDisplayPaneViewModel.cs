using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using KeepShell.Services;
using PoproshaykaBot.Core.Chat;
using PoproshaykaBot.Core.Platforms;
using PoproshaykaBot.Core.Platforms.Auth;
using PoproshaykaBot.Core.Twitch.Chat;
using PoproshaykaBot.Wpf.Bootstrap;
using PoproshaykaBot.Wpf.Infrastructure.Platforms;
using System.Windows.Input;

namespace PoproshaykaBot.Wpf.ViewModels.Tiles;

public sealed partial class ChatDisplayPaneViewModel(
    IShellLauncher shellLauncher, IChatPlatformRegistry? platformRegistry, ISettingsStore? uiSettings = null) : ObservableObject
{
    private IChatAccountMessenger? _accountMessenger;
    private string? _streamId;
    private bool _updatingAccounts;
    private StreamingPlatform? _accountsPlatform;
    private int _messageContextVersion;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasAccountChoices))]
    private IReadOnlyList<ChatDisplayAccountOption> _accountChoices = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowMessageInput))]
    [NotifyCanExecuteChangedFor(nameof(SendMessageCommand))]
    private ChatDisplayAccountOption? _selectedAccount;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SendMessageCommand))]
    private string _message = string.Empty;

    [ObservableProperty]
    private string? _messageStatus;

    [ObservableProperty]
    private bool _isShown;

    [ObservableProperty]
    private bool _hasFallback;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(FallbackActionCommand), nameof(FallbackActionText))]
    private bool _runtimeMissing;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(FallbackActionCommand), nameof(FallbackActionText))]
    private Uri? _externalUri;

    [ObservableProperty]
    private string _fallbackHeading = string.Empty;

    [ObservableProperty]
    private string _fallbackDescription = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DisplayName))]
    private StreamingPlatform _platform;

    public string DisplayName => PlatformVisuals.GetDisplayName(Platform, platformRegistry);
    public Uri? ChannelUri { get; private set; }

    public bool HasAccountChoices => AccountChoices.Count > 1;

    public bool ShowMessageInput => _streamId is not null && SelectedAccount?.Sender is not null
        && SelectedAccount.BrowserProfile is null;

    internal event Action? ChatAccountChanged;

    internal void SetAccounts(IPlatformAccountDirectory? directory, string? streamId, string defaultLabel)
    {
        _accountMessenger = platformRegistry?.Find(Platform)?.AccountMessenger;
        if (_streamId != streamId)
        {
            _messageContextVersion++;
            Message = string.Empty;
            MessageStatus = null;
        }

        _streamId = streamId;
        OnPropertyChanged(nameof(ShowMessageInput));
        SendMessageCommand.NotifyCanExecuteChanged();

        var options = new List<ChatDisplayAccountOption> { new(string.Empty, defaultLabel, null, null) };
        foreach (var account in directory?.ListAccounts() ?? [])
        {
            var authorizations = account.Authorizations.Where(static grant => grant.HasToken).ToArray();
            var browserProfile = authorizations.Select(grant => grant.ChatBrowserProfile).FirstOrDefault(profile => profile is not null);
            var sender = _accountMessenger is { } messenger
                ? authorizations.Select(grant => new ChatSenderAccount(account.Key.AccountId, grant.Role, grant.ContextId))
                    .FirstOrDefault(messenger.CanSendAs)
                : null;
            if (browserProfile is null && sender is null)
            {
                continue;
            }

            var roles = string.Join(", ", authorizations.Select(grant => PlatformAuthRoles.Describe(grant.Role)).Distinct(StringComparer.Ordinal));
            options.Add(new(account.Key.AccountId, $"{account.DisplayName} ({roles})", browserProfile, sender));
        }

        if (_accountsPlatform == Platform && options.SequenceEqual(AccountChoices))
        {
            return;
        }

        var persistedId = uiSettings?.GetStringValue(AccountSettingKey)
            ?? (_accountsPlatform == Platform ? SelectedAccount?.Id : null);
        var previous = SelectedAccount;
        _updatingAccounts = true;
        try
        {
            AccountChoices = options;
            _accountsPlatform = Platform;
            SelectedAccount = options.Find(option => option.Id == persistedId) ?? options[0];
        }
        finally
        {
            _updatingAccounts = false;
        }

        ApplyAccountChange(previous, SelectedAccount);
    }

    private string AccountSettingKey => SettingsKeys.ChatTileAccountPrefix + Platform.ToString().ToLowerInvariant();

    partial void OnSelectedAccountChanged(ChatDisplayAccountOption? oldValue, ChatDisplayAccountOption? newValue)
    {
        if (_updatingAccounts || _accountsPlatform != Platform || newValue is null || !AccountChoices.Contains(newValue))
        {
            return;
        }

        uiSettings?.SetValue(AccountSettingKey, newValue.Id);
        ApplyAccountChange(oldValue, newValue);
    }

    private void ApplyAccountChange(ChatDisplayAccountOption? previous, ChatDisplayAccountOption? current)
    {
        if (previous?.Id != current?.Id || previous?.Sender != current?.Sender)
        {
            _messageContextVersion++;
            Message = string.Empty;
            MessageStatus = null;
        }

        if (previous?.BrowserProfile != current?.BrowserProfile)
        {
            ChatAccountChanged?.Invoke();
        }
    }

    private bool CanSendMessage() => ShowMessageInput && !string.IsNullOrWhiteSpace(Message);

    [RelayCommand(CanExecute = nameof(CanSendMessage))]
    private async Task SendMessageAsync()
    {
        if (!CanSendMessage() || SelectedAccount?.Sender is not { } sender
            || _streamId is not { } streamId || _accountMessenger is not { } messenger)
        {
            return;
        }

        var contextVersion = _messageContextVersion;
        var text = Message;
        MessageStatus = "Отправка…";
        ChatAccountSendResult result;
        try
        {
            result = await messenger.SendAsAsync(sender, streamId, text);
        }
        catch (OperationCanceledException)
        {
            if (contextVersion == _messageContextVersion)
            {
                MessageStatus = "Отправка отменена";
            }

            return;
        }
        catch (Exception)
        {
            if (contextVersion == _messageContextVersion)
            {
                MessageStatus = "Не удалось отправить сообщение";
            }

            return;
        }

        if (contextVersion != _messageContextVersion)
        {
            return;
        }

        MessageStatus = result.DeliveryStatus switch
        {
            ChatDeliveryStatus.Sent => "Сообщение отправлено",
            ChatDeliveryStatus.AuthorizationRequired => $"Авторизуйте аккаунт {DisplayName} заново",
            ChatDeliveryStatus.Rejected => $"{DisplayName} отклонил сообщение",
            ChatDeliveryStatus.Unconfirmed => "Доставка не подтверждена. Проверьте чат перед повтором",
            ChatDeliveryStatus.ChatUnavailable => "Чат эфира недоступен",
            ChatDeliveryStatus.QuotaExhausted => "Квота отправки исчерпана",
            ChatDeliveryStatus.ObserverMode => "Отправка выключена в режиме наблюдателя",
            ChatDeliveryStatus.QueueClosed => "Очередь отправки остановлена",
            _ => DescribeEnqueueResult(result.EnqueueResult),
        };

        if (result is { DeliveryStatus: not ChatDeliveryStatus.Sent, SentParts: > 0 })
        {
            MessageStatus = $"Сообщение отправлено частично. {MessageStatus}";
        }

        if (result.DeliveryStatus == ChatDeliveryStatus.Sent && Message == text)
        {
            Message = string.Empty;
        }
    }

    private string DescribeEnqueueResult(ChatEnqueueResult result) =>
        result switch
        {
            ChatEnqueueResult.ObserverMode => "Отправка выключена в режиме наблюдателя",
            ChatEnqueueResult.QuotaBudgetReached => "Квота отправки исчерпана",
            ChatEnqueueResult.QueueClosed => $"Подключите {DisplayName}, чтобы отправлять сообщения",
            ChatEnqueueResult.NotConnected => "Чат эфира недоступен",
            ChatEnqueueResult.AuthorizationRequired => $"Авторизуйте аккаунт {DisplayName} заново",
            _ => "Не удалось поставить сообщение в очередь",
        };

    public ICommand? FallbackActionCommand => RuntimeMissing ? OpenRuntimeDownloadCommand
        : ExternalUri is not null ? OpenInBrowserCommand : null;

    public string? FallbackActionText => RuntimeMissing ? "Скачать WebView2 Runtime"
        : ExternalUri is not null ? "Открыть в браузере" : null;

    internal bool SetTarget(StreamingPlatform platform, Uri? channelUri, Uri? externalUri, bool shown)
    {
        ExternalUri = externalUri;
        var changed = IsShown != shown || Platform != platform || ChannelUri != channelUri;
        if (changed)
        {
            _messageContextVersion++;
            MessageStatus = null;
        }

        IsShown = shown;
        Platform = platform;
        ChannelUri = channelUri;
        if (changed && shown)
        {
            HasFallback = channelUri is null;
            RuntimeMissing = false;
            if (channelUri is null)
            {
                var twitch = platform == StreamingPlatform.Twitch;
                FallbackHeading = twitch ? "Канал не указан" : "Чат эфира недоступен";
                FallbackDescription = twitch ? "Укажите канал в настройках Twitch, чтобы открыть чат."
                    : "Чат появится, когда у выбранного канала начнётся эфир с доступным чатом.";
            }
        }

        return changed;
    }

    internal void ShowRuntimeMissingFallback()
    {
        HasFallback = true;
        RuntimeMissing = true;
        FallbackHeading = "Требуется Microsoft Edge WebView2 Runtime";
        FallbackDescription = "Установите его, чтобы открыть чат в приложении.";
    }

    internal void ShowGenericFallback()
    {
        HasFallback = true;
        RuntimeMissing = false;
        FallbackHeading = $"Не удалось открыть чат {DisplayName}";
        FallbackDescription = ExternalUri is not null ? "Откройте чат в браузере или попробуйте обновить его."
            : "Попробуйте обновить чат. Подробности – в журнале.";
    }

    [RelayCommand]
    private void OpenInBrowser()
    {
        if (ExternalUri is not null)
        {
            shellLauncher.Open(ExternalUri.AbsoluteUri);
        }
    }

    [RelayCommand]
    private void OpenRuntimeDownload() => shellLauncher.Open("https://go.microsoft.com/fwlink/p/?LinkId=2124703");
}

public sealed record ChatDisplayAccountOption(
    string Id, string Label, PlatformChatBrowserProfile? BrowserProfile, ChatSenderAccount? Sender);
