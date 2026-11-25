using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using PoproshaykaBot.Core.Infrastructure.Events;
using PoproshaykaBot.Core.Infrastructure.Events.Lifecycle;
using PoproshaykaBot.Core.Infrastructure.Hosting;
using PoproshaykaBot.Core.Platforms;
using PoproshaykaBot.Core.Platforms.Auth;
using PoproshaykaBot.Core.Server;
using PoproshaykaBot.Core.Settings;
using PoproshaykaBot.Core.Settings.Onboarding;
using PoproshaykaBot.Core.Settings.Stores;
using PoproshaykaBot.Core.Twitch.Auth;
using PoproshaykaBot.Core.YouTube;
using PoproshaykaBot.Wpf.Bootstrap;
using System.Text.Json;

namespace PoproshaykaBot.Wpf.ViewModels.Onboarding;

public sealed partial class OnboardingWizardViewModel : ObservableObject
{
    private readonly SettingsManager _settingsManager;
    private readonly KestrelHttpServer _kestrelHttpServer;
    private readonly BotConnectionManager _botConnectionManager;
    private readonly IEventBus _eventBus;
    private readonly ILogger<OnboardingWizardViewModel> _logger;
    private readonly OnboardingContext _context;
    private readonly List<IOnboardingPageViewModel> _pages;
    private readonly TwitchSettings _originalTwitch;
    private readonly IDialogService? _dialogService;

    private int _currentPageIndex = -1;
    private bool _completed;
    private bool _rollbackPerformed;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HeaderTitle))]
    [NotifyPropertyChangedFor(nameof(StepIndicator))]
    [NotifyPropertyChangedFor(nameof(IsLastPage))]
    [NotifyPropertyChangedFor(nameof(NextButtonText))]
    [NotifyCanExecuteChangedFor(nameof(NextCommand))]
    [NotifyCanExecuteChangedFor(nameof(BackCommand))]
    private IOnboardingPageViewModel? _currentPage;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(NextButtonText))]
    [NotifyCanExecuteChangedFor(nameof(NextCommand))]
    [NotifyCanExecuteChangedFor(nameof(BackCommand))]
    [NotifyCanExecuteChangedFor(nameof(CancelCommand))]
    private bool _isBusy;

    public OnboardingWizardViewModel(
        IEnumerable<IOnboardingPageViewModel> pages,
        SettingsManager settingsManager,
        AccountsStore accountsStore,
        KestrelHttpServer kestrelHttpServer,
        BotConnectionManager botConnectionManager,
        IEventBus eventBus,
        ILogger<OnboardingWizardViewModel> logger,
        YouTubeSettingsStore? youtubeSettingsStore = null,
        YouTubeAccountsStore? youtubeAccountsStore = null,
        ChannelCatalogStore? twitchCatalog = null,
        IDialogService? dialogService = null)
    {
        _settingsManager = settingsManager;
        _dialogService = dialogService;
        _kestrelHttpServer = kestrelHttpServer;
        _botConnectionManager = botConnectionManager;
        _eventBus = eventBus;
        _logger = logger;

        _pages = [.. pages];

        _originalTwitch = DeepClone(settingsManager.Current.Twitch);
        var twitchChannel = twitchCatalog?.GetActiveChannel(StreamingPlatform.Twitch);
        accountsStore.TryLoadContext(PlatformAuthRole.Bot, twitchChannel?.BotContextId, out var originalBot);
        accountsStore.TryLoadContext(PlatformAuthRole.Broadcaster, twitchChannel?.BroadcasterContextId, out var originalBroadcaster);
        _context = new(
            DeepClone(settingsManager.Current),
            originalBot is null ? new() : DeepClone(originalBot),
            originalBroadcaster is null ? new() : DeepClone(originalBroadcaster),
            youtubeSettingsStore?.Load(),
            youtubeAccountsStore?.LoadClientSecret() ?? string.Empty,
            youtubeAccountsStore?.LoadBot(),
            youtubeAccountsStore?.LoadBroadcaster())
        {
            TwitchCatalog = twitchCatalog,
            TwitchChannel = twitchChannel,
            AutoDetectChannel = twitchChannel is null,
        };
        _context.CaptureTwitchAccounts(originalBot, originalBroadcaster);
        if (twitchChannel is not null && !string.IsNullOrWhiteSpace(twitchChannel.Handle))
        {
            _context.Settings.Twitch.Channel = twitchChannel.Handle.TrimStart('@');
        }

        if (_pages.Count > 0)
        {
            NavigateTo(0);
        }
    }

    public event Action? CloseRequested;

    public int PageCount => _pages.Count;

    public bool IsPageInteractive => !_context.CancellationStarted;

    public string HeaderTitle => IsPageInteractive ? CurrentPage?.PageTitle ?? string.Empty : "Отмена настройки";

    public string StepIndicator => _pages.Count == 0
        ? string.Empty
        : $"Шаг {_currentPageIndex + 1} из {_pages.Count}";

    public bool IsLastPage => _currentPageIndex >= 0 && _currentPageIndex == _pages.Count - 1;

    public string NextButtonText => IsLastPage ? "Готово" : "Далее →";

    internal void StartAt(OnboardingStep step)
    {
        if (step == OnboardingStep.None)
        {
            return;
        }

        _context.AutoDetectChannel = !_context.HasConfiguredTwitchChannel;
        var index = _pages.FindIndex(page => step switch
        {
            OnboardingStep.Appearance => page is AppearancePageViewModel,
            OnboardingStep.Credentials => page is CredentialsPageViewModel,
            OnboardingStep.BotAuthorization => page is BotAuthorizationPageViewModel,
            OnboardingStep.YouTube => page is YouTubeOnboardingPageViewModel,
            _ => false,
        });
        NavigateTo(index);
    }

    public async Task<bool> RequestCloseAsync()
    {
        if (_completed || _rollbackPerformed)
        {
            return true;
        }

        _context.CancellationStarted = true;
        OnPropertyChanged(nameof(IsPageInteractive));
        OnPropertyChanged(nameof(HeaderTitle));
        NextCommand.NotifyCanExecuteChanged();
        BackCommand.NotifyCanExecuteChanged();

        if (_context.TwitchSettingsWritten)
        {
            await StopBotIfRunningAsync();
        }

        var currentPort = _settingsManager.Current.Twitch.HttpServerPort;
        var settingsRestored = RollbackTwitchSettings();
        if (settingsRestored)
        {
            await RollbackHttpServerPortAsync(currentPort);
        }
        var accountsRestored = await RollbackAccountsAsync();
        _rollbackPerformed = (settingsRestored && accountsRestored)
            || _dialogService?.ConfirmWarning(
                "Часть изменений осталась",
                "Не удалось полностью отменить настройки. Чтобы повторить восстановление, вернитесь в мастер и попробуйте закрыть его снова. Закрыть мастер сейчас?",
                defaultYes: false) == true;
        return _rollbackPerformed;
    }

    private static T DeepClone<T>(T source) where T : class, new()
    {
        var json = JsonSerializer.Serialize(source);
        return JsonSerializer.Deserialize<T>(json) ?? new();
    }

    [RelayCommand(CanExecute = nameof(CanGoNext))]
    private async Task NextAsync()
    {
        if (!CanGoNext() || CurrentPage is not { } current)
        {
            return;
        }

        IsBusy = true;
        try
        {
            if (!await current.OnLeavingAsync(_context))
            {
                return;
            }
        }
        finally
        {
            IsBusy = false;
        }

        if (IsLastPage)
        {
            foreach (var page in _pages.OfType<AppearancePageViewModel>())
            {
                page.Commit();
            }

            _completed = true;
            CloseRequested?.Invoke();
            return;
        }

        NavigateTo(_currentPageIndex + 1);
    }

    private bool CanGoNext()
    {
        return IsPageInteractive && !IsBusy && CurrentPage?.CanAdvance == true;
    }

    [RelayCommand(CanExecute = nameof(CanGoBack))]
    private void Back()
    {
        if (!CanGoBack())
        {
            return;
        }

        NavigateTo(_currentPageIndex - 1);
    }

    private bool CanGoBack()
    {
        return IsPageInteractive && !IsBusy && _currentPageIndex > 0;
    }

    [RelayCommand(CanExecute = nameof(CanCancel))]
    private void Cancel()
    {
        CloseRequested?.Invoke();
    }

    private bool CanCancel()
    {
        return !IsBusy;
    }

    private void NavigateTo(int index)
    {
        if (index < 0 || index >= _pages.Count)
        {
            return;
        }

        if (CurrentPage is { } previous)
        {
            previous.CanAdvanceChanged -= OnCurrentPageCanAdvanceChanged;
        }

        var page = _pages[index];
        _currentPageIndex = index;

        page.OnEnter(_context);
        page.CanAdvanceChanged += OnCurrentPageCanAdvanceChanged;

        CurrentPage = page;
    }

    private void OnCurrentPageCanAdvanceChanged(object? sender, EventArgs e)
    {
        NextCommand.NotifyCanExecuteChanged();
    }

    private async Task StopBotIfRunningAsync()
    {
        var phase = _botConnectionManager.CurrentPhase;
        if (phase is BotLifecyclePhase.Idle or BotLifecyclePhase.Disconnected)
        {
            return;
        }

        try
        {
            await _botConnectionManager.ShutdownAsync();
            _logger.OnboardingBotStopped();
        }
        catch (Exception exception)
        {
            _logger.OnboardingBotStopFailed(exception);
        }
    }

    private async Task<bool> RollbackAccountsAsync()
    {
        if (!_context.HasProvisionalWrites)
        {
            return true;
        }

        var restored = true;
        try
        {
            _context.RestoreProvisionalWrites();
        }
        catch (Exception exception)
        {
            _logger.OnboardingAccountsRollbackFailed(exception);
            restored = false;
        }

        await _eventBus.PublishAsync(new PlatformAuthorizationRefreshed(StreamingPlatform.Twitch, PlatformAuthRole.Bot,
            _context.TwitchContextId(PlatformAuthRole.Bot)));
        await _eventBus.PublishAsync(new PlatformAuthorizationRefreshed(StreamingPlatform.Twitch, PlatformAuthRole.Broadcaster,
            _context.TwitchContextId(PlatformAuthRole.Broadcaster)));
        await _eventBus.PublishAsync(new PlatformAuthorizationRefreshed(StreamingPlatform.YouTube, PlatformAuthRole.Bot));
        await _eventBus.PublishAsync(new PlatformAuthorizationRefreshed(StreamingPlatform.YouTube, PlatformAuthRole.Broadcaster));
        if (restored)
        {
            _logger.OnboardingAccountsRolledBack();
        }

        return restored;
    }

    private bool RollbackTwitchSettings()
    {
        if (!_context.TwitchSettingsWritten)
        {
            return true;
        }

        try
        {
            var written = _settingsManager.MutateIfWritable(settings =>
            {
                settings.Twitch.ClientId = _originalTwitch.ClientId;
                settings.Twitch.ClientSecret = _originalTwitch.ClientSecret;
                settings.Twitch.Channel = _originalTwitch.Channel;
                settings.Twitch.RedirectUri = _originalTwitch.RedirectUri;
                settings.Twitch.HttpServerPort = _originalTwitch.HttpServerPort;
                settings.Twitch.ChatDisplayAccount = _originalTwitch.ChatDisplayAccount;
            });
            if (written)
            {
                _context.TwitchSettingsWritten = false;
            }

            return written;
        }
        catch (Exception exception)
        {
            _logger.OnboardingSettingsSaveFailed(exception);
            return false;
        }
    }

    private async Task RollbackHttpServerPortAsync(int currentPort)
    {
        var originalPort = _originalTwitch.HttpServerPort;

        if (currentPort == originalPort)
        {
            return;
        }

        try
        {
            if (_kestrelHttpServer.IsRunning)
            {
                await _kestrelHttpServer.StopAsync();
            }

            _settingsManager.UpdateCurrent(settings => settings.Twitch.HttpServerPort = originalPort);

            await _kestrelHttpServer.StartAsync();

            _logger.OnboardingHttpServerRolledBack(originalPort);
        }
        catch (Exception exception)
        {
            _logger.OnboardingHttpServerRollbackFailed(exception, originalPort);
        }
    }
}
