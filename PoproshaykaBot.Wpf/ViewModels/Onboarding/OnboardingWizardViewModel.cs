using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using PoproshaykaBot.Core.Infrastructure.Events;
using PoproshaykaBot.Core.Infrastructure.Events.Lifecycle;
using PoproshaykaBot.Core.Infrastructure.Hosting;
using PoproshaykaBot.Core.Platforms;
using PoproshaykaBot.Core.Platforms.Auth;
using PoproshaykaBot.Core.Server;
using PoproshaykaBot.Core.Settings;
using PoproshaykaBot.Core.Settings.Migrations.LegacyImport;
using PoproshaykaBot.Core.Settings.Onboarding;
using PoproshaykaBot.Core.Settings.Stores;
using PoproshaykaBot.Core.Twitch.Auth;
using PoproshaykaBot.Core.YouTube;
using PoproshaykaBot.Wpf.Bootstrap;
using System.ComponentModel;
using System.Text.Json;

namespace PoproshaykaBot.Wpf.ViewModels.Onboarding;

public sealed partial class OnboardingWizardViewModel : ObservableObject, IDisposable
{
    private SettingsManager _settingsManager;
    private AccountsStore _accountsStore;
    private KestrelHttpServer _kestrelHttpServer;
    private BotConnectionManager _botConnectionManager;
    private IEventBus _eventBus;
    private readonly ILogger<OnboardingWizardViewModel> _logger;
    private YouTubeSettingsStore? _youtubeSettingsStore;
    private YouTubeAccountsStore? _youtubeAccountsStore;
    private ChannelCatalogStore? _twitchCatalog;
    private OnboardingChecklist? _checklist;
    private IDialogService? _dialogService;

    private OnboardingContext _context;
    private readonly List<IOnboardingPageViewModel> _pages;
    private TwitchSettings _originalTwitch;
    private YouTubeSettings _originalYouTube;
    private string _originalYouTubeClientSecret = string.Empty;

    private bool _completed;
    private bool _rollbackPerformed;
    private bool _disposed;
    private bool _isExecutingSecondaryAction;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HeaderTitle))]
    [NotifyPropertyChangedFor(nameof(StepIndicator))]
    [NotifyPropertyChangedFor(nameof(IsLastPage))]
    [NotifyPropertyChangedFor(nameof(ShowBackButton))]
    [NotifyPropertyChangedFor(nameof(NextButtonText))]
    [NotifyPropertyChangedFor(nameof(SecondaryActionText))]
    [NotifyPropertyChangedFor(nameof(HasSecondaryAction))]
    [NotifyPropertyChangedFor(nameof(IsNextActionSecondary))]
    [NotifyPropertyChangedFor(nameof(IsOperationBusy))]
    [NotifyCanExecuteChangedFor(nameof(NextCommand))]
    [NotifyCanExecuteChangedFor(nameof(SecondaryActionCommand))]
    [NotifyCanExecuteChangedFor(nameof(BackCommand))]
    [NotifyCanExecuteChangedFor(nameof(CancelCommand))]
    [NotifyCanExecuteChangedFor(nameof(HandleEscapeCommand))]
    private IOnboardingPageViewModel? _currentPage;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(NextButtonText))]
    [NotifyPropertyChangedFor(nameof(SecondaryActionText))]
    [NotifyPropertyChangedFor(nameof(HasSecondaryAction))]
    [NotifyPropertyChangedFor(nameof(IsNextActionSecondary))]
    [NotifyPropertyChangedFor(nameof(IsOperationBusy))]
    [NotifyCanExecuteChangedFor(nameof(NextCommand))]
    [NotifyCanExecuteChangedFor(nameof(SecondaryActionCommand))]
    [NotifyCanExecuteChangedFor(nameof(BackCommand))]
    [NotifyCanExecuteChangedFor(nameof(CancelCommand))]
    [NotifyCanExecuteChangedFor(nameof(HandleEscapeCommand))]
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
        IDialogService? dialogService = null,
        OnboardingChecklist? checklist = null)
    {
        _settingsManager = settingsManager;
        _accountsStore = accountsStore;
        _dialogService = dialogService;
        _kestrelHttpServer = kestrelHttpServer;
        _botConnectionManager = botConnectionManager;
        _eventBus = eventBus;
        _logger = logger;
        _youtubeSettingsStore = youtubeSettingsStore;
        _youtubeAccountsStore = youtubeAccountsStore;
        _twitchCatalog = twitchCatalog;
        _checklist = checklist;

        _pages = [.. pages];
        foreach (var importPage in _pages.OfType<LegacyImportPageViewModel>())
        {
            importPage.FinishRequested += OnImportFinishRequested;
            importPage.SkipRequested += OnImportSkipRequested;
            importPage.NavigateToStepRequested += OnNavigateToStepRequested;
        }

        foreach (var completionPage in _pages.OfType<CompletionPageViewModel>())
        {
            completionPage.NavigateToStepRequested += OnNavigateToStepRequested;
        }

        (_originalTwitch, _originalYouTube, _originalYouTubeClientSecret, _context) = BuildContext(
            _settingsManager,
            _accountsStore,
            _youtubeSettingsStore,
            _youtubeAccountsStore,
            _twitchCatalog,
            _checklist,
            includeLegacyImport: false,
            importResult: null);
        _context.PlatformsChanged += OnContextPlatformsChanged;

        var active = ActivePages;
        if (active.Count > 0)
        {
            NavigateTo(active[0]);
        }
    }

    public event Action? CloseRequested;

    public int PageCount => ActivePages.Count;

    public bool IsPageInteractive => !_context.CancellationStarted;

    public bool IsOperationBusy => IsBusy || CurrentPage?.IsBusy == true;

    public string HeaderTitle => IsPageInteractive ? CurrentPage?.PageTitle ?? string.Empty : "Отмена настройки";

    public string StepIndicator
    {
        get
        {
            var active = ActivePages;
            if (active.Count == 0 || CurrentPage is null)
            {
                return string.Empty;
            }

            var index = IndexOfPage(active, CurrentPage);
            return index < 0 ? string.Empty : $"Шаг {index + 1} из {active.Count}";
        }
    }

    public bool IsLastPage
    {
        get
        {
            var active = ActivePages;
            if (active.Count == 0 || CurrentPage is null)
            {
                return false;
            }

            return IndexOfPage(active, CurrentPage) == active.Count - 1;
        }
    }

    public bool ShowBackButton => CurrentPage is { AllowBackNavigation: true } && IndexOfPage(ActivePages, CurrentPage) > 0;

    public string NextButtonText => IsLastPage
        ? CurrentPage?.NextButtonTextOverride ?? "Готово"
        : CurrentPage?.NextButtonTextOverride ?? "Далее";

    public string? SecondaryActionText => CurrentPage?.SecondaryActionText;

    public bool HasSecondaryAction => !string.IsNullOrEmpty(SecondaryActionText);

    public bool IsNextActionSecondary => CurrentPage?.IsNextActionSecondary == true;

    private IReadOnlyList<IOnboardingPageViewModel> ActivePages =>
        [.. _pages.Where(page => page.IsApplicable(_context))];

    internal void AttachStartupSession(StartupOnboardingSession session)
    {
        ArgumentNullException.ThrowIfNull(session);

        _context.IncludeLegacyImport = session.IncludeLegacyImport;

        foreach (var importPage in _pages.OfType<LegacyImportPageViewModel>())
        {
            importPage.ConfigureStartupSession(
                session.Candidates,
                session.Target,
                async (result, neverAskAgain) =>
                {
                    if (_context.HasProvisionalWrites || _context.TwitchSettingsWritten)
                    {
                        throw new InvalidOperationException("Перенос недоступен после сохранения пробных настроек.");
                    }

                    if (session.OnImportCompletedAsync is not null)
                    {
                        var nextServices = await session.OnImportCompletedAsync(result, neverAskAgain);
                        if (nextServices is not null)
                        {
                            CompleteContainerReload(nextServices, result);
                        }
                    }
                },
                session.OnLegacyImportSkipped);
        }

        if (CurrentPage is { } current)
        {
            current.OnEnter(_context);
        }

        NotifyNavigationStateChanged();
    }

    internal void DiscardAppearanceRollback()
    {
        foreach (var page in _pages.OfType<AppearancePageViewModel>())
        {
            page.DiscardRollback();
        }
    }

    internal void PrepareForContainerReload()
    {
        for (var i = _pages.Count - 1; i >= 0; i--)
        {
            var page = _pages[i];
            if (page is WelcomePageViewModel or LegacyImportPageViewModel)
            {
                continue;
            }

            if (page is CompletionPageViewModel completionPage)
            {
                completionPage.NavigateToStepRequested -= OnNavigateToStepRequested;
            }

            if (page is IDisposable disposable)
            {
                disposable.Dispose();
            }

            _pages.RemoveAt(i);
        }
    }

    internal void CompleteContainerReload(IServiceProvider newServices, LegacyImportResult result)
    {
        ArgumentNullException.ThrowIfNull(newServices);

        if (_context.HasProvisionalWrites || _context.TwitchSettingsWritten)
        {
            throw new InvalidOperationException("Перенос недоступен после сохранения пробных настроек.");
        }

        if (result.CopiedUiPreferences)
        {
            DiscardAppearanceRollback();
        }

        _settingsManager = newServices.GetRequiredService<SettingsManager>();
        _accountsStore = newServices.GetRequiredService<AccountsStore>();
        _kestrelHttpServer = newServices.GetRequiredService<KestrelHttpServer>();
        _botConnectionManager = newServices.GetRequiredService<BotConnectionManager>();
        _eventBus = newServices.GetRequiredService<IEventBus>();
        _youtubeSettingsStore = newServices.GetService<YouTubeSettingsStore>();
        _youtubeAccountsStore = newServices.GetService<YouTubeAccountsStore>();
        _twitchCatalog = newServices.GetService<ChannelCatalogStore>();
        _dialogService = newServices.GetService<IDialogService>();
        _checklist = newServices.GetService<OnboardingChecklist>();

        PrepareForContainerReload();

        foreach (var page in newServices.GetServices<IOnboardingPageViewModel>())
        {
            if (page is WelcomePageViewModel or LegacyImportPageViewModel)
            {
                if (page is IDisposable disposable)
                {
                    disposable.Dispose();
                }

                continue;
            }

            if (page is CompletionPageViewModel completionPage)
            {
                completionPage.NavigateToStepRequested += OnNavigateToStepRequested;
            }

            _pages.Add(page);
        }

        _context.PlatformsChanged -= OnContextPlatformsChanged;
        var includeLegacyImport = _context.IncludeLegacyImport;
        (_originalTwitch, _originalYouTube, _originalYouTubeClientSecret, _context) = BuildContext(
            _settingsManager,
            _accountsStore,
            _youtubeSettingsStore,
            _youtubeAccountsStore,
            _twitchCatalog,
            _checklist,
            includeLegacyImport,
            result);
        _context.PlatformsChanged += OnContextPlatformsChanged;

        foreach (var importPage in _pages.OfType<LegacyImportPageViewModel>())
        {
            importPage.RefreshContext(_context);
        }

        NotifyNavigationStateChanged();
    }

    internal void StartAt(OnboardingStep step)
    {
        if (step == OnboardingStep.None)
        {
            return;
        }

        _context.AutoDetectChannel = !_context.HasConfiguredTwitchChannel;
        var active = ActivePages;
        var target = active.FirstOrDefault(page => step switch
        {
            OnboardingStep.LegacyImport => page is LegacyImportPageViewModel,
            OnboardingStep.Appearance => page is AppearancePageViewModel,
            OnboardingStep.Platforms => page is PlatformsPageViewModel,
            OnboardingStep.Credentials => page is CredentialsPageViewModel,
            OnboardingStep.BotAuthorization => page is BotAuthorizationPageViewModel,
            OnboardingStep.BroadcasterAuthorization => page is BroadcasterAuthorizationPageViewModel,
            OnboardingStep.YouTube => page is YouTubeOnboardingPageViewModel,
            _ => false,
        });

        if (target is not null)
        {
            NavigateTo(target);
        }
    }

    public Task<bool> RequestCloseAsync() => RequestCloseAsync(confirmIfModified: false);

    public async Task<bool> RequestCloseAsync(bool confirmIfModified)
    {
        if (_completed || _rollbackPerformed)
        {
            return true;
        }

        if (confirmIfModified && !ConfirmCloseIfNeeded())
        {
            return false;
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

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _context.PlatformsChanged -= OnContextPlatformsChanged;

        if (CurrentPage is { } previous)
        {
            previous.CanAdvanceChanged -= OnCurrentPageCanAdvanceChanged;
            previous.IsBusyChanged -= OnCurrentPageIsBusyChanged;
            if (previous is INotifyPropertyChanged notifyPrevious)
            {
                notifyPrevious.PropertyChanged -= OnCurrentPagePropertyChanged;
            }
        }

        foreach (var importPage in _pages.OfType<LegacyImportPageViewModel>())
        {
            importPage.FinishRequested -= OnImportFinishRequested;
            importPage.SkipRequested -= OnImportSkipRequested;
            importPage.NavigateToStepRequested -= OnNavigateToStepRequested;
        }

        foreach (var completionPage in _pages.OfType<CompletionPageViewModel>())
        {
            completionPage.NavigateToStepRequested -= OnNavigateToStepRequested;
        }

        foreach (var disposable in _pages.OfType<IDisposable>())
        {
            disposable.Dispose();
        }
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

        if (current is LegacyImportPageViewModel importPage)
        {
            if (importPage.IsProfileComplete && !importPage.HasStartedSettingsReview)
            {
                CompleteWizard();
                return;
            }

            if (importPage.HasMissingStep && !importPage.HasStartedSettingsReview)
            {
                importPage.MarkSettingsReviewStarted();
                StartAt(importPage.FirstMissingStep);
                return;
            }
        }

        AdvanceFromPage(current);
    }

    private void AdvanceFromPage(IOnboardingPageViewModel current)
    {
        var active = ActivePages;
        var currentIndex = IndexOfPage(active, current);
        if (currentIndex < 0 || currentIndex == active.Count - 1)
        {
            CompleteWizard();
            return;
        }

        NavigateTo(active[currentIndex + 1]);
    }

    private bool CanGoNext()
    {
        return IsPageInteractive && !IsOperationBusy && CurrentPage?.CanAdvance == true;
    }

    [RelayCommand(CanExecute = nameof(CanExecuteSecondaryAction))]
    private async Task SecondaryActionAsync()
    {
        if (!CanExecuteSecondaryAction() || CurrentPage is not { } current)
        {
            return;
        }

        _isExecutingSecondaryAction = true;
        IsBusy = true;
        try
        {
            await current.OnSecondaryActionAsync(_context);
        }
        finally
        {
            _isExecutingSecondaryAction = false;
            IsBusy = false;
        }
    }

    private bool CanExecuteSecondaryAction()
    {
        return IsPageInteractive && !IsOperationBusy && HasSecondaryAction && CurrentPage?.CanExecuteSecondaryAction == true;
    }

    [RelayCommand(CanExecute = nameof(CanGoBack))]
    private void Back()
    {
        if (!CanGoBack() || CurrentPage is null)
        {
            return;
        }

        var active = ActivePages;
        var currentIndex = IndexOfPage(active, CurrentPage);
        if (currentIndex > 0)
        {
            CurrentPage.OnNavigatingBack(_context);
            NavigateTo(active[currentIndex - 1]);
        }
    }

    private bool CanGoBack()
    {
        if (!IsPageInteractive || IsOperationBusy || CurrentPage is not { AllowBackNavigation: true })
        {
            return false;
        }

        return IndexOfPage(ActivePages, CurrentPage) > 0;
    }

    [RelayCommand(CanExecute = nameof(CanCancel))]
    private void Cancel()
    {
        if (!CanCancel())
        {
            return;
        }

        CloseRequested?.Invoke();
    }

    [RelayCommand(CanExecute = nameof(CanCancel))]
    private void HandleEscape()
    {
        if (!CanCancel())
        {
            return;
        }

        if (CurrentPage?.TryHandleEscape() == true)
        {
            return;
        }

        CloseRequested?.Invoke();
    }

    private bool CanCancel()
    {
        return !IsOperationBusy;
    }

    private void CompleteWizard()
    {
        foreach (var page in _pages.OfType<AppearancePageViewModel>())
        {
            page.Commit();
        }

        foreach (var importPage in _pages.OfType<LegacyImportPageViewModel>())
        {
            importPage.CommitSkipDismissal();
        }

        _completed = true;
        CloseRequested?.Invoke();
    }

    private void OnImportFinishRequested()
    {
        if (_context.CancellationStarted || (IsOperationBusy && !_isExecutingSecondaryAction))
        {
            return;
        }

        CompleteWizard();
    }

    private void OnImportSkipRequested()
    {
        if (_context.CancellationStarted || (IsOperationBusy && !_isExecutingSecondaryAction) || CurrentPage is not { } current)
        {
            return;
        }

        AdvanceFromPage(current);
    }

    private void OnNavigateToStepRequested(OnboardingStep step)
    {
        if (_context.CancellationStarted || (IsOperationBusy && !_isExecutingSecondaryAction))
        {
            return;
        }

        StartAt(step);
    }

    private void OnContextPlatformsChanged(object? sender, EventArgs e)
    {
        NotifyNavigationStateChanged();
    }

    private void NotifyNavigationStateChanged()
    {
        OnPropertyChanged(nameof(PageCount));
        OnPropertyChanged(nameof(StepIndicator));
        OnPropertyChanged(nameof(IsLastPage));
        OnPropertyChanged(nameof(ShowBackButton));
        OnPropertyChanged(nameof(NextButtonText));
        OnPropertyChanged(nameof(SecondaryActionText));
        OnPropertyChanged(nameof(HasSecondaryAction));
        OnPropertyChanged(nameof(IsNextActionSecondary));
        OnPropertyChanged(nameof(IsOperationBusy));
        NextCommand.NotifyCanExecuteChanged();
        SecondaryActionCommand.NotifyCanExecuteChanged();
        BackCommand.NotifyCanExecuteChanged();
        CancelCommand.NotifyCanExecuteChanged();
        HandleEscapeCommand.NotifyCanExecuteChanged();
    }

    private void NavigateTo(IOnboardingPageViewModel page)
    {
        if (CurrentPage is { } previous)
        {
            previous.CanAdvanceChanged -= OnCurrentPageCanAdvanceChanged;
            previous.IsBusyChanged -= OnCurrentPageIsBusyChanged;
            if (previous is INotifyPropertyChanged notifyPrevious)
            {
                notifyPrevious.PropertyChanged -= OnCurrentPagePropertyChanged;
            }
        }

        page.OnEnter(_context);
        page.CanAdvanceChanged += OnCurrentPageCanAdvanceChanged;
        page.IsBusyChanged += OnCurrentPageIsBusyChanged;
        if (page is INotifyPropertyChanged notifyPage)
        {
            notifyPage.PropertyChanged += OnCurrentPagePropertyChanged;
        }

        CurrentPage = page;
        NotifyNavigationStateChanged();
    }

    private void OnCurrentPageCanAdvanceChanged(object? sender, EventArgs e)
    {
        OnPropertyChanged(nameof(NextButtonText));
        OnPropertyChanged(nameof(SecondaryActionText));
        OnPropertyChanged(nameof(HasSecondaryAction));
        OnPropertyChanged(nameof(IsNextActionSecondary));
        NextCommand.NotifyCanExecuteChanged();
        SecondaryActionCommand.NotifyCanExecuteChanged();
    }

    private void OnCurrentPagePropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(IOnboardingPageViewModel.NextButtonTextOverride)
            or nameof(OnboardingPageViewModelBase.NextButtonText)
            or null)
        {
            OnPropertyChanged(nameof(NextButtonText));
        }

        if (e.PropertyName is nameof(IOnboardingPageViewModel.SecondaryActionText) or null)
        {
            OnPropertyChanged(nameof(SecondaryActionText));
            OnPropertyChanged(nameof(HasSecondaryAction));
            SecondaryActionCommand.NotifyCanExecuteChanged();
        }

        if (e.PropertyName is nameof(IOnboardingPageViewModel.CanExecuteSecondaryAction) or null)
        {
            SecondaryActionCommand.NotifyCanExecuteChanged();
        }

        if (e.PropertyName is nameof(IOnboardingPageViewModel.AllowBackNavigation) or null)
        {
            OnPropertyChanged(nameof(ShowBackButton));
            BackCommand.NotifyCanExecuteChanged();
        }

        if (e.PropertyName is nameof(IOnboardingPageViewModel.IsNextActionSecondary) or null)
        {
            OnPropertyChanged(nameof(IsNextActionSecondary));
        }

        if (e.PropertyName is nameof(IOnboardingPageViewModel.PageTitle) or null)
        {
            OnPropertyChanged(nameof(HeaderTitle));
        }
    }

    private void OnCurrentPageIsBusyChanged(object? sender, EventArgs e)
    {
        OnPropertyChanged(nameof(IsOperationBusy));
        NextCommand.NotifyCanExecuteChanged();
        SecondaryActionCommand.NotifyCanExecuteChanged();
        BackCommand.NotifyCanExecuteChanged();
        CancelCommand.NotifyCanExecuteChanged();
        HandleEscapeCommand.NotifyCanExecuteChanged();
    }

    private static int IndexOfPage(IReadOnlyList<IOnboardingPageViewModel> pages, IOnboardingPageViewModel target)
    {
        for (var i = 0; i < pages.Count; i++)
        {
            if (ReferenceEquals(pages[i], target))
            {
                return i;
            }
        }

        return -1;
    }

    private static (TwitchSettings OriginalTwitch, YouTubeSettings OriginalYouTube, string OriginalYouTubeClientSecret, OnboardingContext Context) BuildContext(
        SettingsManager settingsManager,
        AccountsStore accountsStore,
        YouTubeSettingsStore? youtubeSettingsStore,
        YouTubeAccountsStore? youtubeAccountsStore,
        ChannelCatalogStore? twitchCatalog,
        OnboardingChecklist? checklist,
        bool includeLegacyImport,
        LegacyImportResult? importResult)
    {
        var originalTwitch = DeepClone(settingsManager.Current.Twitch);
        var twitchChannel = twitchCatalog?.GetActiveChannel(StreamingPlatform.Twitch);
        var youtubeChannel = twitchCatalog?.GetActiveChannel(StreamingPlatform.YouTube);
        accountsStore.TryLoadContext(PlatformAuthRole.Bot, twitchChannel?.BotContextId, out var originalBot);
        accountsStore.TryLoadContext(PlatformAuthRole.Broadcaster, twitchChannel?.BroadcasterContextId, out var originalBroadcaster);
        var loadedYouTube = youtubeSettingsStore?.Load();
        var loadedYouTubeSecret = youtubeAccountsStore?.LoadClientSecret() ?? string.Empty;
        var context = new OnboardingContext(
            DeepClone(settingsManager.Current),
            originalBot is null ? new() : DeepClone(originalBot),
            originalBroadcaster is null ? new() : DeepClone(originalBroadcaster),
            loadedYouTube,
            loadedYouTubeSecret,
            youtubeAccountsStore?.Load(PlatformAuthRole.Bot, youtubeChannel?.BotContextId),
            youtubeAccountsStore?.Load(PlatformAuthRole.Broadcaster, youtubeChannel?.BroadcasterContextId))
        {
            TwitchCatalog = twitchCatalog,
            TwitchChannel = twitchChannel,
            YouTubeChannel = youtubeChannel,
            AutoDetectChannel = twitchChannel is null,
            IncludeLegacyImport = includeLegacyImport,
            ImportResult = importResult,
            Checklist = checklist,
        };
        context.CaptureTwitchAccounts(originalBot, originalBroadcaster);
        if (twitchChannel is not null && !string.IsNullOrWhiteSpace(twitchChannel.Handle))
        {
            context.Settings.Twitch.Channel = twitchChannel.Handle.TrimStart('@');
        }

        var baselineYouTube = DeepClone(context.YouTubeSettings);
        return (originalTwitch, baselineYouTube, loadedYouTubeSecret, context);
    }

    private bool ConfirmCloseIfNeeded()
    {
        if (_context.CancellationStarted || _dialogService is null || App.IsHeadless)
        {
            return true;
        }

        var hasImportedFiles = _pages.OfType<LegacyImportPageViewModel>()
            .Any(page => page.Import?.Result is { HasChanges: true } || page.IsImportInterrupted || page.IsImportReloadFailed);
        var hasNeverAskAgain = _pages.OfType<LegacyImportPageViewModel>()
            .Any(page => page.Import is { IsCompleted: false, NeverAskAgain: true });
        var hasAppearanceChanges = _pages.OfType<AppearancePageViewModel>()
            .Any(page => page.HasPendingChanges);
        var hasDataChanges = HasUnsavedOnboardingData();

        if (!hasImportedFiles && !hasNeverAskAgain && !hasAppearanceChanges && !hasDataChanges)
        {
            return true;
        }

        var rollbackLines = new List<string>();
        if (hasDataChanges)
        {
            rollbackLines.Add("– несохранённый ввод настроек и авторизации будет отменён;");
        }

        if (hasAppearanceChanges)
        {
            rollbackLines.Add("– изменения темы и размера текста вернутся к прежним значениям;");
        }

        if (hasNeverAskAgain)
        {
            rollbackLines.Add("– отметка «Больше не предлагать перенос при запуске» не сохранится;");
        }

        var parts = new List<string>();
        if (rollbackLines.Count > 0)
        {
            parts.Add("Что откатится:\n" + string.Join("\n", rollbackLines));
        }

        if (hasImportedFiles)
        {
            parts.Add("Что останется:\n– перенесённые файлы из предыдущей версии останутся в рабочей папке.");
        }

        parts.Add("Закрыть мастер и отложить настройку?");

        return _dialogService.ConfirmWarning(
            "Отложить настройку?",
            string.Join("\n\n", parts),
            defaultYes: false);
    }

    private bool HasUnsavedOnboardingData()
    {
        if (_context.HasProvisionalWrites
            || _context.TwitchSettingsWritten
            || _context.HasPendingAccountDrafts
            || !JsonStoreClone.SameValues(_context.YouTubeSettings, _originalYouTube)
            || TwitchSettingsChanged(_context.Settings.Twitch, _originalTwitch))
        {
            return true;
        }

        return CurrentPage switch
        {
            PlatformsPageViewModel platforms => platforms.TwitchEnabled != _originalTwitch.Enabled
                || platforms.YouTubeEnabled != _originalYouTube.Enabled,
            CredentialsPageViewModel credentials => !string.Equals(credentials.ClientId.Trim(), _originalTwitch.ClientId?.Trim() ?? string.Empty, StringComparison.Ordinal)
                || !string.Equals(credentials.ClientSecret.Trim(), _originalTwitch.ClientSecret?.Trim() ?? string.Empty, StringComparison.Ordinal)
                || !string.Equals(credentials.Channel.Trim().TrimStart('@'), (_originalTwitch.Channel ?? string.Empty).Trim().TrimStart('@'), StringComparison.Ordinal)
                || !string.Equals(credentials.RedirectUri.Trim(), NormalizeRedirectUri(_originalTwitch.RedirectUri), StringComparison.Ordinal),
            YouTubeOnboardingPageViewModel youtube => !string.Equals(youtube.Editor.ClientId.Trim(), _originalYouTube.ClientId?.Trim() ?? string.Empty, StringComparison.Ordinal)
                || !string.Equals(youtube.Editor.ClientSecret.Trim(), _originalYouTubeClientSecret.Trim(), StringComparison.Ordinal)
                || !string.Equals(youtube.Editor.ProjectId.Trim(), _originalYouTube.ProjectId?.Trim() ?? string.Empty, StringComparison.Ordinal)
                || !string.Equals(youtube.Editor.Channel.Trim(), _originalYouTube.Channel?.Trim() ?? string.Empty, StringComparison.Ordinal),
            _ => false,
        };
    }

    private bool TwitchSettingsChanged(TwitchSettings current, TwitchSettings original)
    {
        var baselineChannel = _context.TwitchChannel is { } channel && !string.IsNullOrWhiteSpace(channel.Handle)
            ? channel.Handle.TrimStart('@')
            : original.Channel ?? string.Empty;

        return current.Enabled != original.Enabled
            || !string.Equals(current.ClientId ?? string.Empty, original.ClientId ?? string.Empty, StringComparison.Ordinal)
            || !string.Equals(current.ClientSecret ?? string.Empty, original.ClientSecret ?? string.Empty, StringComparison.Ordinal)
            || !string.Equals(current.Channel ?? string.Empty, baselineChannel, StringComparison.Ordinal)
            || !string.Equals(NormalizeRedirectUri(current.RedirectUri), NormalizeRedirectUri(original.RedirectUri), StringComparison.Ordinal)
            || current.HttpServerPort != original.HttpServerPort
            || current.ChatDisplayAccount != original.ChatDisplayAccount;
    }

    private static string NormalizeRedirectUri(string? redirectUri) =>
        string.IsNullOrWhiteSpace(redirectUri)
            ? CredentialsPageViewModel.DefaultRedirectUri
            : redirectUri.Trim();

    private static T DeepClone<T>(T source) where T : class, new()
    {
        var json = JsonSerializer.Serialize(source);
        return JsonSerializer.Deserialize<T>(json) ?? new();
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
        await _eventBus.PublishAsync(new PlatformAuthorizationRefreshed(StreamingPlatform.YouTube, PlatformAuthRole.Bot,
            _context.YouTubeContextId(PlatformAuthRole.Bot)));
        await _eventBus.PublishAsync(new PlatformAuthorizationRefreshed(StreamingPlatform.YouTube, PlatformAuthRole.Broadcaster,
            _context.YouTubeContextId(PlatformAuthRole.Broadcaster)));
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
                settings.Twitch.Enabled = _originalTwitch.Enabled;
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
