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
    private readonly TwitchSettings _originalTwitch;
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
        YouTubeAccountsStore? youtubeAccountsStore = null,
        ChannelCatalogStore? twitchCatalog = null)
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

        _originalTwitch = DeepClone(settingsManager.Current.Twitch);
        var twitchChannel = twitchCatalog?.GetActiveChannel(StreamingPlatform.Twitch);
        accountsStore.TryLoadContext(PlatformAuthRole.Bot, twitchChannel?.BotContextId, out var originalBot);
        accountsStore.TryLoadContext(PlatformAuthRole.Broadcaster, twitchChannel?.BroadcasterContextId, out var originalBroadcaster);
        _originalYouTubeSettings = youtubeSettingsStore is not null ? DeepClone(youtubeSettingsStore.Load()) : new();
        _originalYouTubeClientSecret = youtubeAccountsStore?.LoadClientSecret() ?? string.Empty;
        _originalYouTubeBotAccount = youtubeAccountsStore is not null ? DeepClone(youtubeAccountsStore.LoadBot()) : new();
        _originalYouTubeBroadcasterAccount = youtubeAccountsStore is not null ? DeepClone(youtubeAccountsStore.LoadBroadcaster()) : new();

        _context = new(
            DeepClone(settingsManager.Current),
            originalBot is null ? new() : DeepClone(originalBot),
            originalBroadcaster is null ? new() : DeepClone(originalBroadcaster),
            DeepClone(_originalYouTubeSettings),
            _originalYouTubeClientSecret,
            DeepClone(_originalYouTubeBotAccount),
            DeepClone(_originalYouTubeBroadcasterAccount))
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

    public string HeaderTitle => CurrentPage?.PageTitle ?? string.Empty;

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
            DisposePages();
            return true;
        }

        _rollbackPerformed = true;

        if (_context.TwitchSettingsWritten)
        {
            await StopBotIfRunningAsync();
        }

        RollbackYouTubeSettings();
        await RollbackHttpServerPortAsync();
        RollbackTwitchSettings();
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
        if (!_context.TwitchAccountsApplied)
        {
            return;
        }

        try
        {
            var written = _context.RestoreTwitchChanges(_accountsStore);
            await _eventBus.PublishAsync(new PlatformAuthorizationRefreshed(StreamingPlatform.Twitch, PlatformAuthRole.Bot,
                _context.TwitchContextId(PlatformAuthRole.Bot)));
            await _eventBus.PublishAsync(new PlatformAuthorizationRefreshed(StreamingPlatform.Twitch, PlatformAuthRole.Broadcaster,
                _context.TwitchContextId(PlatformAuthRole.Broadcaster)));

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

    private void RollbackTwitchSettings()
    {
        if (!_context.TwitchSettingsApplied)
        {
            return;
        }

        try
        {
            _settingsManager.Mutate(settings =>
            {
                settings.Twitch.ClientId = _originalTwitch.ClientId;
                settings.Twitch.ClientSecret = _originalTwitch.ClientSecret;
                settings.Twitch.Channel = _originalTwitch.Channel;
                settings.Twitch.RedirectUri = _originalTwitch.RedirectUri;
                settings.Twitch.HttpServerPort = _originalTwitch.HttpServerPort;
                settings.Twitch.ChatDisplayAccount = _originalTwitch.ChatDisplayAccount;
            });
        }
        catch (Exception exception)
        {
            _logger.OnboardingSettingsSaveFailed(exception);
        }
    }

    private async Task RollbackHttpServerPortAsync()
    {
        var currentPort = _settingsManager.Current.Twitch.HttpServerPort;
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
