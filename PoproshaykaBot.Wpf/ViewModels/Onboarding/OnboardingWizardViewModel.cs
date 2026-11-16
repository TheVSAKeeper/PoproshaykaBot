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
using PoproshaykaBot.Core.Settings.Stores;
using PoproshaykaBot.Core.Twitch.Auth;
using PoproshaykaBot.Core.YouTube;
using PoproshaykaBot.Core.YouTube.Auth;
using PoproshaykaBot.Wpf.Bootstrap;
using System.Text.Json;

namespace PoproshaykaBot.Wpf.ViewModels.Onboarding;

public sealed partial class OnboardingWizardViewModel : ObservableObject
{
    private readonly SettingsManager _settingsManager;
    private readonly AccountsStore _accountsStore;
    private readonly YouTubeSettingsStore? _youtubeSettingsStore;
    private readonly YouTubeAccountsStore? _youtubeAccountsStore;
    private readonly KestrelHttpServer _kestrelHttpServer;
    private readonly BotConnectionManager _botConnectionManager;
    private readonly IEventBus _eventBus;
    private readonly ILogger<OnboardingWizardViewModel> _logger;
    private readonly OnboardingContext _context;
    private readonly List<IOnboardingPageViewModel> _pages;
    private readonly int _originalHttpServerPort;
    private readonly string _originalChannel;
    private readonly TwitchAccountSettings _originalBotAccount;
    private readonly TwitchAccountSettings _originalBroadcasterAccount;
    private readonly YouTubeSettings _originalYouTubeSettings;
    private readonly string _originalYouTubeClientSecret;
    private readonly YouTubeAccountSettings _originalYouTubeBotAccount;
    private readonly YouTubeAccountSettings _originalYouTubeBroadcasterAccount;

    private int _currentPageIndex = -1;
    private bool _completed;
    private bool _rollbackPerformed;
    private bool _pagesDisposed;

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
        YouTubeAccountsStore? youtubeAccountsStore = null)
    {
        _settingsManager = settingsManager;
        _accountsStore = accountsStore;
        _youtubeSettingsStore = youtubeSettingsStore;
        _youtubeAccountsStore = youtubeAccountsStore;
        _kestrelHttpServer = kestrelHttpServer;
        _botConnectionManager = botConnectionManager;
        _eventBus = eventBus;
        _logger = logger;

        _pages = [.. pages];

        _originalHttpServerPort = settingsManager.Current.Twitch.HttpServerPort;
        _originalChannel = settingsManager.Current.Twitch.Channel;
        _originalBotAccount = DeepClone(accountsStore.LoadBot());
        _originalBroadcasterAccount = DeepClone(accountsStore.LoadBroadcaster());
        _originalYouTubeSettings = youtubeSettingsStore is not null ? DeepClone(youtubeSettingsStore.Load()) : new();
        _originalYouTubeClientSecret = youtubeAccountsStore?.LoadClientSecret() ?? string.Empty;
        _originalYouTubeBotAccount = youtubeAccountsStore is not null ? DeepClone(youtubeAccountsStore.LoadBot()) : new();
        _originalYouTubeBroadcasterAccount = youtubeAccountsStore is not null ? DeepClone(youtubeAccountsStore.LoadBroadcaster()) : new();

        _context = new(
            DeepClone(settingsManager.Current),
            DeepClone(_originalBotAccount),
            DeepClone(_originalBroadcasterAccount),
            DeepClone(_originalYouTubeSettings),
            _originalYouTubeClientSecret,
            DeepClone(_originalYouTubeBotAccount),
            DeepClone(_originalYouTubeBroadcasterAccount));

        if (_pages.Count > 0)
        {
            NavigateTo(0);
        }
    }

    public event Action? CloseRequested;

    public int PageCount => _pages.Count;

    public string HeaderTitle => CurrentPage?.PageTitle ?? string.Empty;

    public string StepIndicator => _pages.Count == 0
        ? string.Empty
        : $"Шаг {_currentPageIndex + 1} из {_pages.Count}";

    public bool IsLastPage => _currentPageIndex >= 0 && _currentPageIndex == _pages.Count - 1;

    public string NextButtonText => IsLastPage ? "Готово" : "Далее →";

    public async Task<bool> RequestCloseAsync()
    {
        if (_completed || _rollbackPerformed)
        {
            DisposePages();
            return true;
        }

        _rollbackPerformed = true;

        await StopBotIfRunningAsync();
        RollbackChannel();
        RollbackYouTubeSettings();
        await RollbackHttpServerPortAsync();
        await RollbackAccountsAsync();
        await RollbackYouTubeAccountsAsync();

        DisposePages();

        return true;
    }

    private void DisposePages()
    {
        if (_pagesDisposed)
        {
            return;
        }

        _pagesDisposed = true;
        foreach (var page in _pages)
        {
            if (page is IDisposable disposable)
            {
                try
                {
                    disposable.Dispose();
                }
                catch (Exception exception)
                {
                    _logger.OnboardingPageDisposeFailed(exception, page.GetType().Name);
                }
            }
        }
    }

    private static bool AccountsEqual(TwitchAccountSettings left, TwitchAccountSettings right)
    {
        return string.Equals(left.AccessToken, right.AccessToken, StringComparison.Ordinal)
               && string.Equals(left.RefreshToken, right.RefreshToken, StringComparison.Ordinal)
               && string.Equals(left.Login, right.Login, StringComparison.Ordinal)
               && string.Equals(left.UserId, right.UserId, StringComparison.Ordinal);
    }

    private static T DeepClone<T>(T source) where T : class, new()
    {
        var json = JsonSerializer.Serialize(source);
        return JsonSerializer.Deserialize<T>(json) ?? new();
    }

    [RelayCommand(CanExecute = nameof(CanGoNext))]
    private async Task NextAsync()
    {
        if (CurrentPage is not { } current)
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
            _completed = true;
            CloseRequested?.Invoke();
            return;
        }

        NavigateTo(_currentPageIndex + 1);
    }

    private bool CanGoNext()
    {
        return !IsBusy && CurrentPage?.CanAdvance == true;
    }

    [RelayCommand(CanExecute = nameof(CanGoBack))]
    private void Back()
    {
        if (_currentPageIndex <= 0)
        {
            return;
        }

        NavigateTo(_currentPageIndex - 1);
    }

    private bool CanGoBack()
    {
        return !IsBusy && _currentPageIndex > 0;
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

    private async Task RollbackAccountsAsync()
    {
        if (!AccountsDifferFromOriginal())
        {
            return;
        }

        try
        {
            var written = _accountsStore.SaveAll(_originalBotAccount, _originalBroadcasterAccount);
            await _eventBus.PublishAsync(new PlatformAuthorizationRefreshed(StreamingPlatform.Twitch, PlatformAuthRole.Bot));
            await _eventBus.PublishAsync(new PlatformAuthorizationRefreshed(StreamingPlatform.Twitch, PlatformAuthRole.Broadcaster));

            if (written)
            {
                _logger.OnboardingAccountsRolledBack();
            }
            else
            {
                _logger.OnboardingAccountsRolledBackInMemory();
            }
        }
        catch (Exception exception)
        {
            _logger.OnboardingAccountsRollbackFailed(exception);
        }
    }

    private bool AccountsDifferFromOriginal()
    {
        var liveBot = _accountsStore.LoadBot();
        var liveBroadcaster = _accountsStore.LoadBroadcaster();
        return !AccountsEqual(liveBot, _originalBotAccount)
               || !AccountsEqual(liveBroadcaster, _originalBroadcasterAccount);
    }

    private void RollbackYouTubeSettings()
    {
        if (_youtubeSettingsStore is null)
        {
            return;
        }

        var live = _youtubeSettingsStore.Load();
        if (string.Equals(JsonSerializer.Serialize(live), JsonSerializer.Serialize(_originalYouTubeSettings), StringComparison.Ordinal))
        {
            return;
        }

        try
        {
            _youtubeSettingsStore.Save(DeepClone(_originalYouTubeSettings));
        }
        catch (Exception exception)
        {
            _logger.OnboardingAccountsRollbackFailed(exception);
        }
    }

    private async Task RollbackYouTubeAccountsAsync()
    {
        if (_youtubeAccountsStore is null)
        {
            return;
        }

        var liveSecret = _youtubeAccountsStore.LoadClientSecret();
        var liveBot = _youtubeAccountsStore.LoadBot();
        var liveBroadcaster = _youtubeAccountsStore.LoadBroadcaster();
        if (string.Equals(liveSecret, _originalYouTubeClientSecret, StringComparison.Ordinal)
            && string.Equals(JsonSerializer.Serialize(liveBot), JsonSerializer.Serialize(_originalYouTubeBotAccount), StringComparison.Ordinal)
            && string.Equals(JsonSerializer.Serialize(liveBroadcaster), JsonSerializer.Serialize(_originalYouTubeBroadcasterAccount), StringComparison.Ordinal))
        {
            return;
        }

        try
        {
            _youtubeAccountsStore.SaveAll(
                _originalYouTubeClientSecret,
                DeepClone(_originalYouTubeBotAccount),
                DeepClone(_originalYouTubeBroadcasterAccount));
            await _eventBus.PublishAsync(new PlatformAuthorizationRefreshed(StreamingPlatform.YouTube, PlatformAuthRole.Bot));
            await _eventBus.PublishAsync(new PlatformAuthorizationRefreshed(StreamingPlatform.YouTube, PlatformAuthRole.Broadcaster));
        }
        catch (Exception exception)
        {
            _logger.OnboardingAccountsRollbackFailed(exception);
        }
    }

    private void RollbackChannel()
    {
        var liveChannel = _settingsManager.Current.Twitch.Channel;
        if (string.Equals(liveChannel, _originalChannel, StringComparison.Ordinal))
        {
            return;
        }

        _settingsManager.UpdateCurrent(settings => settings.Twitch.Channel = _originalChannel);
        _logger.OnboardingChannelRolledBack(_originalChannel);
    }

    private async Task RollbackHttpServerPortAsync()
    {
        var currentPort = _settingsManager.Current.Twitch.HttpServerPort;

        if (currentPort == _originalHttpServerPort)
        {
            return;
        }

        try
        {
            if (_kestrelHttpServer.IsRunning)
            {
                await _kestrelHttpServer.StopAsync();
            }

            _settingsManager.UpdateCurrent(settings => settings.Twitch.HttpServerPort = _originalHttpServerPort);

            await _kestrelHttpServer.StartAsync();

            _logger.OnboardingHttpServerRolledBack(_originalHttpServerPort);
        }
        catch (Exception exception)
        {
            _logger.OnboardingHttpServerRollbackFailed(exception, _originalHttpServerPort);
        }
    }
}
