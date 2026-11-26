using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using PoproshaykaBot.Core.Chat.History;
using PoproshaykaBot.Core.Infrastructure;
using PoproshaykaBot.Core.Settings.Migrations.LegacyImport;
using PoproshaykaBot.Core.Settings.Stores;
using PoproshaykaBot.Core.Statistics;
using PoproshaykaBot.Wpf.Bootstrap;
using PoproshaykaBot.Wpf.Infrastructure;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;

namespace PoproshaykaBot.Wpf.ViewModels.Migration;

public sealed partial class LegacyImportViewModel : ObservableObject
{
    private readonly IFilePicker _filePicker;
    private readonly ILogger _logger;
    private readonly bool _isSettingsEntry;
    private readonly StatisticsAutoSaver? _statisticsAutoSaver;
    private readonly SettingsWriteGate? _settingsWriteGate;
    private readonly UiSettingsWriteGuard? _uiSettingsGuard;
    private readonly ChatHistoryCacheStore? _chatHistoryCache;
    private readonly Func<string, bool, ILogger?, LegacyImportResult> _import;
    private readonly Func<string, LegacyDataSummary?> _inspect;
    private HashSet<string> _targetFiles = new(StringComparer.OrdinalIgnoreCase);

    internal Func<LegacyImportResult, bool, Task>? AfterImportAsync { get; set; }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ImportCommand))]
    [NotifyPropertyChangedFor(nameof(AuthorizationNotice))]
    [NotifyPropertyChangedFor(nameof(CanOverwriteExisting))]
    [NotifyPropertyChangedFor(nameof(ConflictNotice))]
    private LegacyImportSourceViewModel? _selectedSource;

    [ObservableProperty]
    private bool _overwriteExisting;

    [ObservableProperty]
    private bool _neverAskAgain;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanConfigure))]
    [NotifyCanExecuteChangedFor(nameof(ConfigureCommand))]
    [NotifyCanExecuteChangedFor(nameof(ImportCommand))]
    [NotifyCanExecuteChangedFor(nameof(PickFolderCommand))]
    private bool _isCompleted;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsBusy))]
    [NotifyPropertyChangedFor(nameof(CanSelectSource))]
    [NotifyCanExecuteChangedFor(nameof(ImportCommand))]
    [NotifyCanExecuteChangedFor(nameof(PickFolderCommand))]
    [NotifyCanExecuteChangedFor(nameof(CloseCommand))]
    private string? _busyMessage;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Explanation))]
    private bool _hasSources;

    [ObservableProperty]
    private string? _pickerNotice;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsBlocked))]
    [NotifyPropertyChangedFor(nameof(CanSelectSource))]
    [NotifyCanExecuteChangedFor(nameof(ImportCommand))]
    [NotifyCanExecuteChangedFor(nameof(PickFolderCommand))]
    private string? _blockedNotice;

    [ObservableProperty]
    private string _resultHeadline = string.Empty;

    [ObservableProperty]
    private string? _resultAuthorizationNotice;

    [ObservableProperty]
    private string? _resultUnmigratedNotice;

    [ObservableProperty]
    private string? _resultRestartNotice;

    public LegacyImportViewModel(
        IReadOnlyList<LegacyDataSummary> candidates,
        LegacyDataSummary? target,
        bool isSettingsEntry,
        IFilePicker filePicker,
        ILogger logger,
        StatisticsAutoSaver? statisticsAutoSaver = null,
        SettingsWriteGate? settingsWriteGate = null,
        UiSettingsWriteGuard? uiSettingsGuard = null,
        ChatHistoryCacheStore? chatHistoryCache = null,
        Func<string, bool, ILogger?, LegacyImportResult>? import = null,
        Func<string, LegacyDataSummary?>? inspect = null)
    {
        _filePicker = filePicker;
        _logger = logger;
        _isSettingsEntry = isSettingsEntry;
        _statisticsAutoSaver = statisticsAutoSaver;
        _settingsWriteGate = settingsWriteGate;
        _uiSettingsGuard = uiSettingsGuard;
        _chatHistoryCache = chatHistoryCache;
        _import = import ?? LegacyDataImporter.Import;
        _inspect = inspect ?? (path => LegacyDataScanner.Inspect(path, _logger));
        CanDismiss = !isSettingsEntry;
        SetSources(candidates, target);
    }

    public event EventHandler? CloseRequested;

    public ObservableCollection<LegacyImportSourceViewModel> Sources { get; } = [];

    public ObservableCollection<string> ResultCounts { get; } = [];

    public ObservableCollection<string> ResultFailures { get; } = [];

    public bool IsBusy => BusyMessage is not null;

    public bool IsBlocked => !string.IsNullOrEmpty(BlockedNotice);

    public bool CanSelectSource => !IsBusy && !IsBlocked;

    public bool CanOverwriteExisting => SelectedSource is { Summary.Kind: not LegacyDataSourceKind.CurrentBaseDirectory } source
        && source.Summary.Files.Select(file => file.RelativeTargetPath).Concat(source.Summary.GeneratedTargetPaths)
            .Any(path => _targetFiles.Contains(Normalize(Path.Combine(AppPaths.BaseDirectory, path))));

    public string? ConflictNotice => SelectedSource?.Summary.Kind == LegacyDataSourceKind.CurrentBaseDirectory
        ? "При переносе на месте исправные рабочие файлы сохраняются. Одноимённые старые файлы останутся в исходной папке."
        : CanOverwriteExisting
            ? "По умолчанию существующие файлы сохраняются. При замене рядом останутся резервные копии без секретов."
            : null;

    public bool CanDismiss { get; }

    public string Explanation => HasSources
        ? "Выберите папку, чтобы перенести настройки и накопленную статистику."
        : "Укажите папку с данными предыдущей версии бота.";

    public string SkipButtonText => _isSettingsEntry ? "Закрыть" : "Пропустить";

    public LegacyImportResult? Result { get; private set; }

    public bool CanConfigure => !_isSettingsEntry && IsCompleted && Result?.HasChanges == true;

    public bool ConfigureRequested { get; private set; }

    public string? AuthorizationNotice => SelectedSource?.Summary switch
    {
        { HasFlatTwitchTokens: true } => "Старые токены из общего файла настроек не переносятся. После переноса проверим, нужно ли снова войти в Twitch.",
        { HasOAuthTokens: true } => "После переноса проверим, подходят ли сохранённые разрешения Twitch.",
        _ => null,
    };

    private bool CanPickFolder() => !IsBusy && !IsCompleted && !IsBlocked;

    private bool CanImport() => CanPickFolder() && SelectedSource is not null;

    private bool CanClose() => !IsBusy;

    partial void OnSelectedSourceChanged(LegacyImportSourceViewModel? value)
    {
        OverwriteExisting = false;
    }

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

    internal async Task LoadSourcesAsync()
    {
        BusyMessage = "Идёт поиск данных предыдущей версии…";
        try
        {
            var (candidates, target) = await ScanAsync(_logger);
            SetSources(candidates, target);
            _logger.LegacyImportOpenedFromSettings(candidates.Count);
        }
        catch (Exception exception)
        {
            _logger.LegacyImportSourceSearchFailed(exception);
            PickerNotice = "Не удалось выполнить поиск. Попробуйте выбрать папку вручную.";
        }
        finally
        {
            BusyMessage = null;
        }
    }

    [RelayCommand(CanExecute = nameof(CanImport))]
    private async Task ImportAsync()
    {
        if (!CanImport() || SelectedSource is not { } source)
        {
            return;
        }

        var path = source.Path;
        var overwrite = CanOverwriteExisting && OverwriteExisting;
        BusyMessage = "Идёт перенос данных…";

        LegacyImportResult Import()
        {
            return _import(path, overwrite, _logger);
        }

        Task<LegacyImportResult> WithStatisticsScopeAsync()
        {
            return _statisticsAutoSaver is null
                ? Task.FromResult(Import())
                : _statisticsAutoSaver.RunExternalWriteAsync(Import, static imported => imported.ExternalWrite);
        }

        Task<LegacyImportResult> WithCacheScopeAsync()
        {
            return _chatHistoryCache is null
                ? WithStatisticsScopeAsync()
                : _chatHistoryCache.RunExternalWriteAsync(WithStatisticsScopeAsync, static imported => imported.CopiedChatHistory);
        }

        LegacyImportResult result;
        var uiPreferencesKept = false;
        _uiSettingsGuard?.Suspend();

        try
        {
            try
            {
                result = await Task.Run(() => _settingsWriteGate is null
                    ? WithCacheScopeAsync()
                    : _settingsWriteGate.RunExternalWriteAsync(WithCacheScopeAsync, static imported => imported.RewrittenSettingsFiles));
            }
            catch (Exception exception)
            {
                _logger.LegacyImportInterrupted(exception);
                ResultHeadline = "Перенос прервался";
                ResultRestartNotice = "Часть файлов могла перенестись. Перезапустите приложение, чтобы оно перечитало рабочую папку.";
                IsCompleted = true;
                return;
            }

            uiPreferencesKept = !result.CopiedUiPreferences;

            Result = result;
            ResultHeadline = LegacyImportText.BuildHeadline(result);
            Fill(ResultCounts, LegacyImportText.BuildCounts(result));
            Fill(ResultFailures, LegacyImportText.BuildFailures(result));
            ResultAuthorizationNotice = LegacyImportText.BuildAuthorizationNotice(result, _isSettingsEntry);
            ResultUnmigratedNotice = LegacyImportText.DescribeUnmigrated(result);
            ResultRestartNotice = LegacyImportText.BuildRestartNotice(result, _isSettingsEntry);
            IsCompleted = true;

            if (AfterImportAsync is { } afterImport)
            {
                try
                {
                    await afterImport(result, NeverAskAgain);
                }
                catch (Exception exception)
                {
                    _logger.LegacyImportReloadFailed(exception);
                    ResultRestartNotice = "Данные перенесены, но обновить мастер на лету не удалось. Перезапустите приложение, чтобы применить перенесённые настройки.";
                }
            }
        }
        finally
        {
            _uiSettingsGuard?.Resume(rewritten: !uiPreferencesKept);
            BusyMessage = null;
        }
    }

    [RelayCommand(CanExecute = nameof(CanPickFolder))]
    private async Task PickFolderAsync()
    {
        if (!CanPickFolder())
        {
            return;
        }
        var path = _filePicker.PickFolder("Папка с данными предыдущей версии");

        if (string.IsNullOrEmpty(path))
        {
            return;
        }

        var known = Sources.FirstOrDefault(source => AreSamePath(source.Path, path));

        if (known is not null)
        {
            PickerNotice = null;
            SelectedSource = known;
            return;
        }

        BusyMessage = "Проверяем выбранную папку…";
        try
        {
            if (await Task.Run(() => _inspect(path)) is not { } summary)
            {
                PickerNotice = "В этой папке нет данных бота. Обычно они лежат в папке PoproshaykaBot.";
                return;
            }

            PickerNotice = null;
            var source = new LegacyImportSourceViewModel(summary);
            Sources.Add(source);
            HasSources = true;
            SelectedSource = source;
        }
        catch (Exception exception)
        {
            _logger.LegacyImportSourceSearchFailed(exception);
            PickerNotice = "Не удалось проверить папку. Выберите другую папку или попробуйте снова.";
        }
        finally
        {
            BusyMessage = null;
        }
    }

    [RelayCommand(CanExecute = nameof(CanClose))]
    private void Close()
    {
        CloseRequested?.Invoke(this, EventArgs.Empty);
    }

    [RelayCommand(CanExecute = nameof(CanConfigure))]
    private void Configure()
    {
        if (!CanConfigure)
        {
            return;
        }

        ConfigureRequested = true;
        Close();
    }

    private static bool AreSamePath(string first, string second)
    {
        return string.Equals(Normalize(first), Normalize(second), StringComparison.OrdinalIgnoreCase);
    }

    private static string Normalize(string path)
    {
        try
        {
            return Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        }
        catch (Exception exception) when (exception is ArgumentException or NotSupportedException or PathTooLongException or IOException)
        {
            return path;
        }
    }

    private static void Fill(ObservableCollection<string> target, IReadOnlyList<string> lines)
    {
        target.Clear();

        foreach (var line in lines)
        {
            target.Add(line);
        }
    }

    private void SetSources(IReadOnlyList<LegacyDataSummary> candidates, LegacyDataSummary? target)
    {
        _targetFiles = new(target?.Files.Select(file => Normalize(file.SourcePath)) ?? [], StringComparer.OrdinalIgnoreCase);

        foreach (var candidate in candidates)
        {
            Sources.Add(new(candidate));
        }

        HasSources = Sources.Count > 0;
    }
}
