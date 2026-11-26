using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using KeepShell.Services.Platform;
using Microsoft.Extensions.Logging;
using PoproshaykaBot.Core.Settings.Migrations.LegacyImport;
using PoproshaykaBot.Core.Settings.Onboarding;
using PoproshaykaBot.Wpf.ViewModels.Migration;
using System.Collections.ObjectModel;
using System.ComponentModel;

namespace PoproshaykaBot.Wpf.ViewModels.Onboarding;

public sealed partial class LegacyImportPageViewModel : OnboardingPageViewModelBase, IDisposable
{
    private const string ProvisionalWritesBlockedMessage =
        "Перенос недоступен после сохранения пробных настроек. Отмените мастер и откройте его снова.";

    private readonly IFilePicker _filePicker;
    private readonly ILogger<LegacyImportPageViewModel> _logger;
    private OnboardingContext? _context;
    private Action<bool>? _onSkipped;
    private bool _wasBusy;
    private bool _hasStartedSettingsReview;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasCompletedImport))]
    [NotifyPropertyChangedFor(nameof(HasReadinessSummary))]
    [NotifyPropertyChangedFor(nameof(HasMissingStep))]
    [NotifyPropertyChangedFor(nameof(AllowBackNavigation))]
    [NotifyPropertyChangedFor(nameof(NextButtonText))]
    [NotifyPropertyChangedFor(nameof(SecondaryActionText))]
    [NotifyPropertyChangedFor(nameof(CanExecuteSecondaryAction))]
    [NotifyPropertyChangedFor(nameof(IsNextActionSecondary))]
    [NotifyPropertyChangedFor(nameof(CanFinishImmediately))]
    [NotifyPropertyChangedFor(nameof(CanGoToMissingStep))]
    [NotifyPropertyChangedFor(nameof(CanReviewSettings))]
    [NotifyCanExecuteChangedFor(nameof(FinishImmediatelyCommand))]
    [NotifyCanExecuteChangedFor(nameof(GoToMissingStepCommand))]
    [NotifyCanExecuteChangedFor(nameof(ReviewSettingsCommand))]
    private LegacyImportViewModel? _import;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasCompletedImport))]
    [NotifyPropertyChangedFor(nameof(HasReadinessSummary))]
    [NotifyPropertyChangedFor(nameof(HasMissingStep))]
    [NotifyPropertyChangedFor(nameof(AllowBackNavigation))]
    [NotifyPropertyChangedFor(nameof(NextButtonText))]
    [NotifyPropertyChangedFor(nameof(SecondaryActionText))]
    [NotifyPropertyChangedFor(nameof(CanExecuteSecondaryAction))]
    [NotifyPropertyChangedFor(nameof(IsNextActionSecondary))]
    [NotifyPropertyChangedFor(nameof(CanFinishImmediately))]
    [NotifyPropertyChangedFor(nameof(CanGoToMissingStep))]
    [NotifyPropertyChangedFor(nameof(CanReviewSettings))]
    [NotifyCanExecuteChangedFor(nameof(FinishImmediatelyCommand))]
    [NotifyCanExecuteChangedFor(nameof(GoToMissingStepCommand))]
    [NotifyCanExecuteChangedFor(nameof(ReviewSettingsCommand))]
    private bool _isProfileComplete;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasReadinessSummary))]
    [NotifyPropertyChangedFor(nameof(HasMissingStep))]
    [NotifyPropertyChangedFor(nameof(AllowBackNavigation))]
    [NotifyPropertyChangedFor(nameof(NextButtonText))]
    [NotifyPropertyChangedFor(nameof(SecondaryActionText))]
    [NotifyPropertyChangedFor(nameof(CanExecuteSecondaryAction))]
    [NotifyPropertyChangedFor(nameof(IsNextActionSecondary))]
    [NotifyPropertyChangedFor(nameof(CanFinishImmediately))]
    [NotifyPropertyChangedFor(nameof(CanGoToMissingStep))]
    [NotifyPropertyChangedFor(nameof(CanReviewSettings))]
    [NotifyCanExecuteChangedFor(nameof(FinishImmediatelyCommand))]
    [NotifyCanExecuteChangedFor(nameof(GoToMissingStepCommand))]
    [NotifyCanExecuteChangedFor(nameof(ReviewSettingsCommand))]
    private bool _isReloading;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasReadinessSummary))]
    [NotifyPropertyChangedFor(nameof(HasMissingStep))]
    [NotifyPropertyChangedFor(nameof(AllowBackNavigation))]
    [NotifyPropertyChangedFor(nameof(NextButtonText))]
    [NotifyPropertyChangedFor(nameof(SecondaryActionText))]
    [NotifyPropertyChangedFor(nameof(CanExecuteSecondaryAction))]
    [NotifyPropertyChangedFor(nameof(IsNextActionSecondary))]
    [NotifyPropertyChangedFor(nameof(CanFinishImmediately))]
    [NotifyPropertyChangedFor(nameof(CanGoToMissingStep))]
    [NotifyPropertyChangedFor(nameof(CanReviewSettings))]
    [NotifyCanExecuteChangedFor(nameof(FinishImmediatelyCommand))]
    [NotifyCanExecuteChangedFor(nameof(GoToMissingStepCommand))]
    [NotifyCanExecuteChangedFor(nameof(ReviewSettingsCommand))]
    private bool _isReloadFailed;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasMissingStep))]
    [NotifyPropertyChangedFor(nameof(NextButtonText))]
    [NotifyPropertyChangedFor(nameof(SecondaryActionText))]
    [NotifyPropertyChangedFor(nameof(CanExecuteSecondaryAction))]
    [NotifyPropertyChangedFor(nameof(CanGoToMissingStep))]
    [NotifyCanExecuteChangedFor(nameof(GoToMissingStepCommand))]
    private OnboardingStep _firstMissingStep;

    [ObservableProperty]
    private bool _isImportBlockedByProvisionalWrites;

    [ObservableProperty]
    private string _readinessTitle = string.Empty;

    [ObservableProperty]
    private string _readinessDescription = string.Empty;

    public LegacyImportPageViewModel(IFilePicker filePicker, ILogger<LegacyImportPageViewModel> logger)
    {
        _filePicker = filePicker;
        _logger = logger;
    }

    public event Action? FinishRequested;

    public event Action? SkipRequested;

    public event Action<OnboardingStep>? NavigateToStepRequested;

    public override string PageTitle => "Перенос данных";

    internal bool HasStartedSettingsReview => _hasStartedSettingsReview;

    private bool IsImportPrimaryActionActive =>
        !HasCompletedImport
        && !IsImportBlockedByProvisionalWrites
        && Import is { HasSources: true } or { SelectedSource: not null };

    public override bool AllowBackNavigation => !HasCompletedImport;

    public override string? NextButtonText
    {
        get
        {
            if (!HasCompletedImport)
            {
                return IsImportPrimaryActionActive ? "Перенести" : "Продолжить без переноса";
            }

            if (IsImportInterrupted || IsImportReloadFailed)
            {
                return "Далее";
            }

            if (IsProfileComplete)
            {
                return _hasStartedSettingsReview ? "Далее" : "Открыть панель";
            }

            if (HasMissingStep)
            {
                return _hasStartedSettingsReview ? "Далее" : DescribeMissingStepAction(FirstMissingStep);
            }

            return "Далее";
        }
    }

    public override string? SecondaryActionText
    {
        get
        {
            if (IsImportPrimaryActionActive)
            {
                return "Продолжить без переноса";
            }

            if (!HasReadinessSummary)
            {
                return null;
            }

            if (IsProfileComplete)
            {
                return _hasStartedSettingsReview ? "Открыть панель" : "Проверить настройки";
            }

            if (HasMissingStep)
            {
                return _hasStartedSettingsReview
                    ? DescribeMissingStepAction(FirstMissingStep)
                    : "Проверить всё с оформления";
            }

            return null;
        }
    }

    public override bool CanExecuteSecondaryAction => !IsBusy && !string.IsNullOrEmpty(SecondaryActionText);

    public override bool IsNextActionSecondary => false;

    public override bool IsBusy => IsReloading || Import?.IsBusy == true;

    public ObservableCollection<string> MissingItems { get; } = [];

    public bool HasCompletedImport => Import is { IsCompleted: true };

    public bool IsImportInterrupted => Import?.IsInterrupted == true;

    public bool IsImportReloadFailed => IsReloadFailed || Import?.IsReloadFailed == true;

    public bool HasReadinessSummary => HasCompletedImport && !IsReloading && Import?.Result is not null && !IsImportInterrupted && !IsImportReloadFailed;

    public bool HasMissingItems => MissingItems.Count > 0;

    public bool HasMissingStep => HasReadinessSummary && !IsProfileComplete && FirstMissingStep != OnboardingStep.None;

    public bool CanFinishImmediately => HasReadinessSummary && IsProfileComplete && !IsBusy;

    public bool CanGoToMissingStep => HasMissingStep && !IsBusy;

    public bool CanReviewSettings => HasReadinessSummary && !IsBusy;

    public string ReviewSettingsButtonText => IsProfileComplete
        ? "Проверить настройки"
        : "Проверить всё с оформления";

    public override bool IsApplicable(OnboardingContext context) => context.IncludeLegacyImport;

    internal void ConfigureStartupSession(
        IReadOnlyList<LegacyDataSummary> candidates,
        LegacyDataSummary? target,
        Func<LegacyImportResult, bool, Task>? afterImportAsync,
        Action<bool>? onSkipped,
        Func<string, bool, ILogger?, LegacyImportResult>? importOverride = null)
    {
        if (Import is { } previous)
        {
            previous.PropertyChanged -= OnImportPropertyChanged;
        }

        _onSkipped = onSkipped;
        _hasStartedSettingsReview = false;
        IsReloadFailed = false;
        var viewModel = new LegacyImportViewModel(
            candidates,
            target,
            isSettingsEntry: false,
            _filePicker,
            _logger,
            import: importOverride);

        viewModel.AfterImportAsync = async (result, neverAskAgain) =>
        {
            IsReloading = true;
            IsReloadFailed = false;
            UpdateCanAdvance();
            SyncBusyState();
            try
            {
                if (_context is { } context && (context.HasProvisionalWrites || context.TwitchSettingsWritten))
                {
                    throw new InvalidOperationException("Перенос недоступен после сохранения пробных настроек.");
                }

                if (_context is not null)
                {
                    _context.ImportResult = result;
                }

                if (afterImportAsync is not null)
                {
                    await afterImportAsync(result, neverAskAgain);
                }
            }
            catch
            {
                IsReloadFailed = true;
                throw;
            }
            finally
            {
                IsReloading = false;
                RefreshReadiness();
                UpdateCanAdvance();
                SyncBusyState();
            }
        };

        viewModel.PropertyChanged += OnImportPropertyChanged;
        Import = viewModel;
        SyncProvisionalWriteLock();
        RefreshReadiness();
        UpdateCanAdvance();
        SyncBusyState();
    }

    internal void RefreshContext(OnboardingContext context)
    {
        _context = context;
        SyncProvisionalWriteLock();
        RefreshReadiness();
        UpdateCanAdvance();
        SyncBusyState();
    }

    public override void OnEnter(OnboardingContext context)
    {
        _context = context;

        if (Import is null)
        {
            ConfigureStartupSession([], null, null, null);
        }

        SyncProvisionalWriteLock();
        RefreshReadiness();
        UpdateCanAdvance();
        SyncBusyState();
    }

    public override async Task<bool> OnLeavingAsync(OnboardingContext context)
    {
        if (IsImportPrimaryActionActive)
        {
            if (Import is { } import && import.ImportCommand.CanExecute(null))
            {
                await import.ImportCommand.ExecuteAsync(null);
            }

            UpdateCanAdvance();
            return false;
        }

        UpdateCanAdvance();
        return CanAdvance;
    }

    public override Task OnSecondaryActionAsync(OnboardingContext context)
    {
        if (!CanExecuteSecondaryAction)
        {
            return Task.CompletedTask;
        }

        if (IsImportPrimaryActionActive)
        {
            SkipRequested?.Invoke();
            return Task.CompletedTask;
        }

        if (HasReadinessSummary)
        {
            if (IsProfileComplete)
            {
                if (_hasStartedSettingsReview)
                {
                    FinishImmediately();
                }
                else
                {
                    ReviewSettings();
                }
            }
            else if (HasMissingStep)
            {
                if (_hasStartedSettingsReview)
                {
                    GoToMissingStep();
                }
                else
                {
                    ReviewSettings();
                }
            }
        }

        return Task.CompletedTask;
    }

    internal void MarkSettingsReviewStarted()
    {
        if (!_hasStartedSettingsReview)
        {
            _hasStartedSettingsReview = true;
            NotifyFooterPropertiesChanged();
        }
    }

    internal void CommitSkipDismissal()
    {
        if (Import is { IsCompleted: false, NeverAskAgain: true })
        {
            _onSkipped?.Invoke(true);
        }
    }

    [RelayCommand(CanExecute = nameof(CanFinishImmediately))]
    private void FinishImmediately()
    {
        if (!CanFinishImmediately)
        {
            return;
        }

        FinishRequested?.Invoke();
    }

    [RelayCommand(CanExecute = nameof(CanGoToMissingStep))]
    private void GoToMissingStep()
    {
        if (!CanGoToMissingStep)
        {
            return;
        }

        MarkSettingsReviewStarted();
        NavigateToStepRequested?.Invoke(FirstMissingStep);
    }

    [RelayCommand(CanExecute = nameof(CanReviewSettings))]
    private void ReviewSettings()
    {
        if (!CanReviewSettings)
        {
            return;
        }

        MarkSettingsReviewStarted();
        NavigateToStepRequested?.Invoke(OnboardingStep.Appearance);
    }

    public void Dispose()
    {
        if (Import is { } current)
        {
            current.PropertyChanged -= OnImportPropertyChanged;
        }
    }

    partial void OnIsReloadingChanged(bool value)
    {
        SyncBusyState();
    }

    partial void OnImportChanged(LegacyImportViewModel? value)
    {
        SyncBusyState();
    }

    partial void OnIsImportBlockedByProvisionalWritesChanged(bool value)
    {
        NotifyFooterPropertiesChanged();
        UpdateCanAdvance();
    }

    partial void OnIsProfileCompleteChanged(bool value)
    {
        OnPropertyChanged(nameof(ReviewSettingsButtonText));
    }

    private void OnImportPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(LegacyImportViewModel.SelectedSource)
            or nameof(LegacyImportViewModel.HasSources)
            or nameof(LegacyImportViewModel.BlockedNotice))
        {
            NotifyFooterPropertiesChanged();
            UpdateCanAdvance();
            return;
        }

        if (e.PropertyName is nameof(LegacyImportViewModel.IsBusy)
            or nameof(LegacyImportViewModel.IsCompleted)
            or nameof(LegacyImportViewModel.IsInterrupted)
            or nameof(LegacyImportViewModel.IsReloadFailed))
        {
            if (Import is { IsCompleted: false, IsInterrupted: false })
            {
                IsReloadFailed = false;
                _hasStartedSettingsReview = false;
            }

            OnPropertyChanged(nameof(HasCompletedImport));
            OnPropertyChanged(nameof(IsImportInterrupted));
            OnPropertyChanged(nameof(IsImportReloadFailed));
            OnPropertyChanged(nameof(HasReadinessSummary));
            OnPropertyChanged(nameof(HasMissingStep));
            NotifyFooterPropertiesChanged();
            OnPropertyChanged(nameof(CanFinishImmediately));
            OnPropertyChanged(nameof(CanGoToMissingStep));
            OnPropertyChanged(nameof(CanReviewSettings));
            RefreshReadiness();
            UpdateCanAdvance();
            SyncBusyState();
            FinishImmediatelyCommand.NotifyCanExecuteChanged();
            GoToMissingStepCommand.NotifyCanExecuteChanged();
            ReviewSettingsCommand.NotifyCanExecuteChanged();
        }
    }

    private void NotifyFooterPropertiesChanged()
    {
        OnPropertyChanged(nameof(AllowBackNavigation));
        OnPropertyChanged(nameof(NextButtonText));
        OnPropertyChanged(nameof(SecondaryActionText));
        OnPropertyChanged(nameof(CanExecuteSecondaryAction));
        OnPropertyChanged(nameof(IsNextActionSecondary));
    }

    private void SyncProvisionalWriteLock()
    {
        var blocked = _context is { } context && (context.HasProvisionalWrites || context.TwitchSettingsWritten);
        IsImportBlockedByProvisionalWrites = blocked;
        if (Import is not null)
        {
            Import.BlockedNotice = blocked ? ProvisionalWritesBlockedMessage : null;
        }
    }

    private void SyncBusyState()
    {
        var busy = IsBusy;
        if (busy != _wasBusy)
        {
            _wasBusy = busy;
            OnPropertyChanged(nameof(IsBusy));
            OnPropertyChanged(nameof(CanExecuteSecondaryAction));
            OnIsBusyChanged();
        }
    }

    private void UpdateCanAdvance()
    {
        if (IsBusy || IsImportInterrupted || IsImportReloadFailed)
        {
            CanAdvance = false;
            return;
        }

        if (IsImportPrimaryActionActive)
        {
            CanAdvance = Import?.ImportCommand.CanExecute(null) == true;
            return;
        }

        CanAdvance = true;
    }

    private void RefreshReadiness()
    {
        MissingItems.Clear();

        if (!HasCompletedImport || IsImportInterrupted || IsImportReloadFailed || IsReloading || Import?.Result is not { } result)
        {
            if (IsImportReloadFailed && Import is not null)
            {
                Import.ResultAuthorizationNotice = null;
            }

            FirstMissingStep = OnboardingStep.None;
            IsProfileComplete = false;
            ReadinessTitle = string.Empty;
            ReadinessDescription = string.Empty;
            OnPropertyChanged(nameof(HasMissingItems));
            OnPropertyChanged(nameof(HasReadinessSummary));
            OnPropertyChanged(nameof(HasMissingStep));
            NotifyFooterPropertiesChanged();
            OnPropertyChanged(nameof(ReviewSettingsButtonText));
            return;
        }

        var checklist = _context?.Checklist;
        var missing = checklist?.GetMissingItems() ?? [];
        foreach (var item in missing)
        {
            MissingItems.Add(item);
        }

        FirstMissingStep = checklist?.FirstMissingStep ?? OnboardingStep.None;
        OnPropertyChanged(nameof(HasMissingItems));

        if (_context is not null)
        {
            var twitchEnabled = _context.Settings.Twitch.Enabled;
            var youtubeEnabled = _context.YouTubeSettings.Enabled;
            var twitchBotRequired = checklist is not null
                ? twitchEnabled && checklist.HasMissingTwitchBotAuthorization
                : twitchEnabled && result.BotAuthorizationRequired;
            var youtubeBotRequired = checklist is not null
                && youtubeEnabled
                && checklist.HasMissingYouTubeBotAuthorization;

            Import.ResultAuthorizationNotice = LegacyImportText.BuildAuthorizationNotice(
                result,
                isSettingsEntry: false,
                twitchEnabled: twitchEnabled,
                twitchBotRequired: twitchBotRequired,
                youtubeBotRequired: youtubeBotRequired);
        }

        if (missing.Count == 0 && checklist is not null)
        {
            IsProfileComplete = true;
            ReadinessTitle = "Профиль готов к работе";
            ReadinessDescription = result.HasChanges
                ? "Все обязательные настройки перенесены. Можно сразу открыть панель или проверить настройки с шага оформления."
                : "Рабочий профиль уже готов к работе. Можно сразу открыть панель или проверить настройки с шага оформления.";
        }
        else
        {
            IsProfileComplete = false;
            if (!result.HasChanges && missing.Count == 0)
            {
                ReadinessTitle = "Файлы не изменились";
                ReadinessDescription = "Нажмите «Далее», чтобы продолжить настройку.";
            }
            else
            {
                ReadinessTitle = "Осталось донастроить";
                ReadinessDescription = result.HasChanges
                    ? "Перенос завершён, но для запуска бота нужно заполнить:"
                    : "Файлы не изменились, но для запуска бота нужно заполнить:";
            }
        }

        OnPropertyChanged(nameof(HasReadinessSummary));
        OnPropertyChanged(nameof(HasMissingStep));
        NotifyFooterPropertiesChanged();
        OnPropertyChanged(nameof(ReviewSettingsButtonText));
    }

    private static string DescribeMissingStepAction(OnboardingStep step)
    {
        return step switch
        {
            OnboardingStep.Platforms => "Перейти к площадкам",
            OnboardingStep.Credentials => "Перейти к ключам Twitch",
            OnboardingStep.BotAuthorization => "Перейти ко входу бота",
            OnboardingStep.YouTube => "Перейти к YouTube",
            OnboardingStep.Appearance => "Перейти к оформлению",
            _ => "Дозаполнить настройки",
        };
    }
}
