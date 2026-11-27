using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using KeepShell.Services.Platform;
using Microsoft.Extensions.Logging;
using PoproshaykaBot.Core.Infrastructure;
using PoproshaykaBot.Core.Settings.Migrations.LegacyImport;
using PoproshaykaBot.Wpf.Bootstrap;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;

namespace PoproshaykaBot.Wpf.ViewModels.Migration;

public enum FirstRunStartStep
{
    None = 0,
    Choice = 1,
    InvalidFolder = 2,
    Summary = 3,
}

public enum FirstRunStartOutcome
{
    None = 0,
    StartFresh = 1,
    OpenDashboard = 2,
    ConnectYouTube = 3,
}

public enum FirstRunStorageLocation
{
    None = 0,
    Portable = 1,
    AppData = 2,
}

internal sealed class AppDataAlreadyRunningException(string message) : InvalidOperationException(message);

internal sealed class AppDataBackupRestoreFailedException : InvalidOperationException
{
    public AppDataBackupRestoreFailedException(string message)
        : base(message)
    {
    }

    public AppDataBackupRestoreFailedException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}

public sealed partial class FirstRunStartViewModel : ObservableObject, IDialogViewModel
{
    private readonly IFilePicker _filePicker;
    private readonly ILogger _logger;
    private readonly Func<string, FirstRunStorageLocation, ILogger?, LegacyImportResult> _import;
    private readonly Func<string, LegacyDataSummary?> _inspect;
    private readonly Func<LegacyImportResult, FirstRunStorageLocation, Task>? _afterImportAsync;
    private readonly Func<FirstRunStorageLocation, bool>? _onStartFresh;
    private readonly Func<LegacyImportResult, FirstRunImportSnapshot>? _readSummarySnapshot;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsChoiceStep), nameof(IsInvalidFolderStep), nameof(IsSummaryStep), nameof(Title))]
    [NotifyCanExecuteChangedFor(nameof(ImportCommand), nameof(PickFolderCommand), nameof(StartFreshCommand), nameof(ConnectYouTubeCommand), nameof(OpenDashboardCommand), nameof(CloseCommand))]
    private FirstRunStartStep _currentStep = FirstRunStartStep.Choice;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasDetectedSource), nameof(PrimaryImportButtonText))]
    [NotifyCanExecuteChangedFor(nameof(ImportCommand))]
    private LegacyImportSourceViewModel? _selectedSource;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsPortableStorageSelected), nameof(IsAppDataStorageSelected))]
    private FirstRunStorageLocation _selectedStorageLocation = FirstRunStorageLocation.Portable;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsBusy), nameof(CanMutate))]
    [NotifyCanExecuteChangedFor(nameof(ImportCommand), nameof(PickFolderCommand), nameof(StartFreshCommand), nameof(ConnectYouTubeCommand), nameof(OpenDashboardCommand), nameof(CloseCommand))]
    private string? _busyMessage;

    [ObservableProperty]
    private string? _invalidFolderMessage;

    [ObservableProperty]
    private string? _storageErrorNotice;

    [ObservableProperty]
    private string _summaryHeadline = "Данные перенесены";

    [ObservableProperty]
    private string? _reloadNotice;

    [ObservableProperty]
    private bool _isPartialSummary;

    public FirstRunStartViewModel(
        IReadOnlyList<LegacyDataSummary> candidates,
        IFilePicker filePicker,
        ILogger logger,
        Func<string, FirstRunStorageLocation, ILogger?, LegacyImportResult> import,
        Func<FirstRunStorageLocation, bool> onStartFresh,
        Func<string, LegacyDataSummary?>? inspect = null,
        Func<LegacyImportResult, FirstRunStorageLocation, Task>? afterImportAsync = null,
        Func<LegacyImportResult, FirstRunImportSnapshot>? readSummarySnapshot = null)
    {
        ArgumentNullException.ThrowIfNull(candidates);
        ArgumentNullException.ThrowIfNull(filePicker);
        ArgumentNullException.ThrowIfNull(logger);
        ArgumentNullException.ThrowIfNull(import);
        ArgumentNullException.ThrowIfNull(onStartFresh);

        _filePicker = filePicker;
        _logger = logger;
        _import = import;
        _onStartFresh = onStartFresh;
        _inspect = inspect ?? (path => LegacyDataScanner.Inspect(path, _logger));
        _afterImportAsync = afterImportAsync;
        _readSummarySnapshot = readSummarySnapshot;

        if (candidates.Count > 0)
        {
            SelectedSource = new LegacyImportSourceViewModel(candidates[0]);
        }
    }

    internal static FirstRunStartViewModel CreatePreview(
        IReadOnlyList<LegacyDataSummary> candidates,
        IFilePicker filePicker,
        ILogger logger)
    {
        return new(
            candidates,
            filePicker,
            logger,
            static (_, _, _) => throw new NotSupportedException("Предпросмотр галереи не выполняет перенос."),
            static _ => throw new NotSupportedException("Предпросмотр галереи не выполняет запуск с нуля."));
    }

    public event EventHandler<bool>? RequestClose;

    public string Title => CurrentStep switch
    {
        FirstRunStartStep.Summary => "Переезд завершён",
        FirstRunStartStep.InvalidFolder => "Не удалось найти данные",
        _ => "Первый запуск",
    };

    public bool IsChoiceStep => CurrentStep == FirstRunStartStep.Choice;

    public bool IsInvalidFolderStep => CurrentStep == FirstRunStartStep.InvalidFolder;

    public bool IsSummaryStep => CurrentStep == FirstRunStartStep.Summary;

    public bool HasDetectedSource => SelectedSource is not null;

    public bool IsBusy => BusyMessage is not null;

    public bool IsPortableStorageSelected
    {
        get => SelectedStorageLocation == FirstRunStorageLocation.Portable;
        set
        {
            if (value)
            {
                SelectedStorageLocation = FirstRunStorageLocation.Portable;
            }
        }
    }

    public bool IsAppDataStorageSelected
    {
        get => SelectedStorageLocation == FirstRunStorageLocation.AppData;
        set
        {
            if (value)
            {
                SelectedStorageLocation = FirstRunStorageLocation.AppData;
            }
        }
    }

    public string PrimaryImportButtonText => "Перенести данные из старой версии";

    public ObservableCollection<FirstRunSummaryItem> TransferredItems { get; } = [];

    public ObservableCollection<FirstRunSummaryItem> AttentionItems { get; } = [];

    public bool HasAttentionItems => AttentionItems.Count > 0;

    public FirstRunStartOutcome Outcome { get; private set; }

    public LegacyImportResult? Result { get; private set; }

    public bool CanMutate => !IsBusy;

    internal static Task<(IReadOnlyList<LegacyDataSummary> Candidates, LegacyDataSummary? Target)> ScanAsync(ILogger logger)
    {
        return Task.Run(() =>
        {
            var start = Stopwatch.GetTimestamp();
            var candidates = LegacyDataScanner.Scan(logger);
            var target = candidates.FirstOrDefault(candidate => candidate.Kind == LegacyDataSourceKind.CurrentBaseDirectory)
                ?? LegacyDataScanner.Inspect(AppPaths.BaseDirectory, logger);
            if (logger.IsEnabled(LogLevel.Information))
            {
                logger.LegacyImportSearchCompleted(Stopwatch.GetElapsedTime(start).TotalMilliseconds);
            }

            return (candidates, target);
        });
    }

    public void ShowGallerySummary(FirstRunImportSummary summary, string? reloadNotice = null)
    {
        ApplySummary(summary);
        ReloadNotice = reloadNotice;
        CurrentStep = FirstRunStartStep.Summary;
    }

    public void ShowGalleryInvalidFolder(string message)
    {
        InvalidFolderMessage = message;
        CurrentStep = FirstRunStartStep.InvalidFolder;
    }

    [RelayCommand(CanExecute = nameof(CanMutate))]
    private async Task ImportAsync()
    {
        if (IsBusy)
        {
            return;
        }

        StorageErrorNotice = null;

        if (SelectedSource is null)
        {
            var picked = await PickFolderCoreAsync();
            if (!picked || SelectedSource is null)
            {
                return;
            }
        }

        await RunImportAsync(SelectedSource.Path);
    }

    [RelayCommand(CanExecute = nameof(CanMutate))]
    private async Task PickFolderAsync()
    {
        if (IsBusy)
        {
            return;
        }

        StorageErrorNotice = null;
        await PickFolderCoreAsync();
    }

    [RelayCommand(CanExecute = nameof(CanMutate))]
    private void StartFresh()
    {
        if (IsBusy)
        {
            return;
        }

        StorageErrorNotice = null;

        try
        {
            if (_onStartFresh is not null && !_onStartFresh(SelectedStorageLocation))
            {
                return;
            }
        }
        catch (AppDataAlreadyRunningException exception)
        {
            StorageErrorNotice = exception.Message;
            return;
        }
        catch (Exception exception)
        {
            _logger.LegacyImportInterrupted(exception);
            StorageErrorNotice = "Не удалось подготовить выбранную папку для настроек. Выберите другое расположение или проверьте права доступа.";
            return;
        }

        Outcome = FirstRunStartOutcome.StartFresh;
        RequestClose?.Invoke(this, false);
    }

    [RelayCommand(CanExecute = nameof(CanMutate))]
    private void ConnectYouTube()
    {
        if (IsBusy)
        {
            return;
        }

        Outcome = FirstRunStartOutcome.ConnectYouTube;
        RequestClose?.Invoke(this, true);
    }

    [RelayCommand(CanExecute = nameof(CanMutate))]
    private void OpenDashboard()
    {
        if (IsBusy)
        {
            return;
        }

        Outcome = FirstRunStartOutcome.OpenDashboard;
        RequestClose?.Invoke(this, true);
    }

    [RelayCommand(CanExecute = nameof(CanMutate))]
    private void Close()
    {
        if (IsBusy)
        {
            return;
        }

        if (CurrentStep == FirstRunStartStep.Summary)
        {
            Outcome = FirstRunStartOutcome.OpenDashboard;
            RequestClose?.Invoke(this, true);
            return;
        }

        Outcome = FirstRunStartOutcome.StartFresh;
        RequestClose?.Invoke(this, false);
    }

    private async Task<bool> PickFolderCoreAsync()
    {
        var path = _filePicker.PickFolder("Папка с данными предыдущей версии");
        if (string.IsNullOrWhiteSpace(path))
        {
            return false;
        }

        BusyMessage = "Проверяем выбранную папку…";
        try
        {
            var summary = await Task.Run(() => _inspect(path));
            if (summary is null)
            {
                InvalidFolderMessage = LegacyImportText.DiagnoseInvalidFolder(path);
                CurrentStep = FirstRunStartStep.InvalidFolder;
                return false;
            }

            SelectedSource = new LegacyImportSourceViewModel(summary);
            InvalidFolderMessage = null;
            CurrentStep = FirstRunStartStep.Choice;
            return true;
        }
        catch (Exception exception)
        {
            _logger.LegacyImportSourceSearchFailed(exception);
            InvalidFolderMessage = "Не удалось открыть выбранную папку. Проверьте права доступа и выберите другую папку или начните с нуля.";
            CurrentStep = FirstRunStartStep.InvalidFolder;
            return false;
        }
        finally
        {
            BusyMessage = null;
        }
    }

    private async Task RunImportAsync(string sourcePath)
    {
        BusyMessage = "Переносим настройки, профили и статистику…";
        ReloadNotice = null;
        StorageErrorNotice = null;

        try
        {
            var location = SelectedStorageLocation;
            LegacyImportResult result;
            try
            {
                result = await Task.Run(() => _import(sourcePath, location, _logger));
            }
            catch (AppDataAlreadyRunningException exception)
            {
                Result = null;
                StorageErrorNotice = exception.Message;
                return;
            }
            catch (AppDataBackupRestoreFailedException exception)
            {
                _logger.LegacyImportInterrupted(exception);
                Result = null;
                InvalidFolderMessage = exception.Message;
                CurrentStep = FirstRunStartStep.InvalidFolder;
                return;
            }
            catch (Exception exception)
            {
                _logger.LegacyImportInterrupted(exception);
                Result = null;
                InvalidFolderMessage = "Не удалось завершить перенос данных из этой папки: файлы могли оказаться повреждены или заняты другой программой. Выберите другую папку или начните с нуля.";
                CurrentStep = FirstRunStartStep.InvalidFolder;
                return;
            }

            if (!result.HasChanges && result.Failures.Count > 0)
            {
                Result = result;
                InvalidFolderMessage = LegacyImportText.BuildInvalidImportReason(result);
                CurrentStep = FirstRunStartStep.InvalidFolder;
                return;
            }

            Result = result;

            if (_afterImportAsync is not null)
            {
                try
                {
                    await _afterImportAsync(result, location);
                }
                catch (Exception exception)
                {
                    _logger.LegacyImportReloadFailed(exception);
                    ReloadNotice = "Данные перенесены, но применить часть настроек сразу не удалось – перезапустите программу при необходимости.";
                }
            }

            var snapshot = _readSummarySnapshot is not null
                ? _readSummarySnapshot(result)
                : FirstRunSummaryReader.Read(result, _logger);
            var summary = LegacyImportText.BuildFirstRunSummary(result, snapshot);
            ApplySummary(summary);
            CurrentStep = FirstRunStartStep.Summary;
        }
        finally
        {
            BusyMessage = null;
        }
    }

    private void ApplySummary(FirstRunImportSummary summary)
    {
        SummaryHeadline = summary.Headline;
        IsPartialSummary = summary.IsPartial;

        TransferredItems.Clear();
        foreach (var item in summary.TransferredItems)
        {
            TransferredItems.Add(item);
        }

        AttentionItems.Clear();
        foreach (var item in summary.AttentionItems)
        {
            AttentionItems.Add(item);
        }

        OnPropertyChanged(nameof(HasAttentionItems));
    }
}
