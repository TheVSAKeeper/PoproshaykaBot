using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using KeepShell.Services.Platform;
using Microsoft.Extensions.Logging;
using PoproshaykaBot.Core.Settings.Migrations.LegacyImport;
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

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasCompletedImport))]
    [NotifyPropertyChangedFor(nameof(CanFinishImmediately))]
    [NotifyCanExecuteChangedFor(nameof(FinishImmediatelyCommand))]
    private LegacyImportViewModel? _import;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasCompletedImport))]
    [NotifyPropertyChangedFor(nameof(CanFinishImmediately))]
    [NotifyCanExecuteChangedFor(nameof(FinishImmediatelyCommand))]
    private bool _isProfileComplete;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanFinishImmediately))]
    [NotifyCanExecuteChangedFor(nameof(FinishImmediatelyCommand))]
    private bool _isReloading;

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

    public override string PageTitle => "Перенос данных";

    public override bool IsBusy => IsReloading || Import?.IsBusy == true;

    public ObservableCollection<string> MissingItems { get; } = [];

    public bool HasCompletedImport => Import is { IsCompleted: true, Result: not null };

    public bool HasMissingItems => MissingItems.Count > 0;

    public bool CanFinishImmediately => IsProfileComplete && !IsBusy;

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

    public override Task<bool> OnLeavingAsync(OnboardingContext context)
    {
        if (Import is { IsCompleted: false, NeverAskAgain: true })
        {
            _onSkipped?.Invoke(true);
        }

        UpdateCanAdvance();
        return Task.FromResult(CanAdvance);
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

    private void OnImportPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(LegacyImportViewModel.IsBusy) or nameof(LegacyImportViewModel.IsCompleted))
        {
            OnPropertyChanged(nameof(HasCompletedImport));
            OnPropertyChanged(nameof(CanFinishImmediately));
            RefreshReadiness();
            UpdateCanAdvance();
            SyncBusyState();
            FinishImmediatelyCommand.NotifyCanExecuteChanged();
        }
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
            OnIsBusyChanged();
        }
    }

    private void UpdateCanAdvance()
    {
        CanAdvance = !IsBusy;
    }

    private void RefreshReadiness()
    {
        MissingItems.Clear();

        if (!HasCompletedImport || Import?.Result is not { } result)
        {
            IsProfileComplete = false;
            ReadinessTitle = string.Empty;
            ReadinessDescription = string.Empty;
            OnPropertyChanged(nameof(HasMissingItems));
            return;
        }

        var missing = _context?.Checklist?.GetMissingItems() ?? [];
        foreach (var item in missing)
        {
            MissingItems.Add(item);
        }

        OnPropertyChanged(nameof(HasMissingItems));

        if (!result.HasChanges)
        {
            IsProfileComplete = false;
            ReadinessTitle = "Файлы не изменились";
            ReadinessDescription = "Нажмите «Далее →», чтобы продолжить настройку.";
            return;
        }

        if (missing.Count == 0 && _context?.Checklist is not null)
        {
            IsProfileComplete = true;
            ReadinessTitle = "Профиль готов к работе";
            ReadinessDescription = "Все обязательные настройки перенесены. Можно сразу перейти к боту или нажать «Далее →», чтобы проверить оформление и площадки.";
        }
        else
        {
            IsProfileComplete = false;
            ReadinessTitle = "Осталось донастроить";
            ReadinessDescription = missing.Count > 0
                ? "Перенос завершён, но для запуска бота нужно заполнить:"
                : "Нажмите «Далее →», чтобы продолжить настройку.";
        }
    }
}
