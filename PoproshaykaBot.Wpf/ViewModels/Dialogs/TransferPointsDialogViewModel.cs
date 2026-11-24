using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using PoproshaykaBot.Core.Platforms;
using PoproshaykaBot.Core.Settings;
using PoproshaykaBot.Core.Settings.Stores;
using PoproshaykaBot.Core.Statistics;
using PoproshaykaBot.Core.Users;
using PoproshaykaBot.Wpf.Bootstrap;
using PoproshaykaBot.Wpf.Infrastructure;
using PoproshaykaBot.Wpf.Infrastructure.Platforms;
using System.Collections.ObjectModel;

namespace PoproshaykaBot.Wpf.ViewModels.Dialogs;

public sealed partial class TransferPointsDialogViewModel : ObservableObject, IDialogViewModel, ICancelableDialog
{
    private readonly IUserStatisticsRepository _userStatistics;
    private readonly PointsTransferService _pointsTransfer;
    private readonly ChannelCatalogStore? _channelCatalog;
    private readonly PointTerm _pointTerm;
    private readonly IDialogService _dialogService;
    private readonly ILogger? _logger;
    private readonly IReadOnlySet<PlatformUserKey> _filteredUserKeys;
    private readonly IReadOnlySet<PlatformUserKey> _initialSelectedUserKeys;
    private readonly HashSet<PlatformUserKey> _checkedUserKeys = [];
    private readonly bool _hasPageFilter;
    private bool _suppressPreviewRebuild;
    private bool _suppressRowSelectionSync;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSources))]
    [NotifyPropertyChangedFor(nameof(IsCrossPlatform))]
    [NotifyCanExecuteChangedFor(nameof(ExecuteTransferCommand))]
    private TransferSourceOption? _selectedSource;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasTargets))]
    [NotifyPropertyChangedFor(nameof(IsCrossPlatform))]
    [NotifyCanExecuteChangedFor(nameof(ExecuteTransferCommand))]
    private TransferTargetOption? _selectedTarget;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsScopeAll))]
    [NotifyPropertyChangedFor(nameof(IsScopeFiltered))]
    [NotifyPropertyChangedFor(nameof(IsScopeSelected))]
    [NotifyPropertyChangedFor(nameof(IsManualSelection))]
    private PointsTransferScope _selectedScope = PointsTransferScope.All;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsModeAllCounters))]
    [NotifyPropertyChangedFor(nameof(IsModeAdjustmentsOnly))]
    private PointsTransferMode _selectedMode = PointsTransferMode.AllCounters;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanCancel))]
    [NotifyPropertyChangedFor(nameof(CanEditOptions))]
    [NotifyCanExecuteChangedFor(nameof(ExecuteTransferCommand))]
    [NotifyCanExecuteChangedFor(nameof(CloseCommand))]
    private bool _isBusy;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CloseButtonText))]
    [NotifyPropertyChangedFor(nameof(CanEditOptions))]
    [NotifyCanExecuteChangedFor(nameof(ExecuteTransferCommand))]
    private bool _hasCompleted;

    [ObservableProperty]
    private bool _hasCompletionWarning;

    [ObservableProperty]
    private string? _completionMessage;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ExecuteTransferCommand))]
    private int _transferredCount;

    [ObservableProperty]
    private int _skippedCount;

    [ObservableProperty]
    private string _previewSummaryText = "Выберите источник и целевой канал.";

    [ObservableProperty]
    private string _previewDetailsText = string.Empty;

    public TransferPointsDialogViewModel(
        IUserStatisticsRepository userStatistics,
        PointsTransferService pointsTransfer,
        ChannelCatalogStore? channelCatalog,
        PointTerm pointTerm,
        IDialogService dialogService,
        PointsTransferSource? initialSource = null,
        IReadOnlyCollection<PlatformUserKey>? filteredUserKeys = null,
        IReadOnlyCollection<PlatformUserKey>? initialSelectedUserKeys = null,
        bool hasPageFilter = false,
        ILogger? logger = null)
    {
        ArgumentNullException.ThrowIfNull(userStatistics);
        ArgumentNullException.ThrowIfNull(pointsTransfer);
        ArgumentNullException.ThrowIfNull(pointTerm);
        ArgumentNullException.ThrowIfNull(dialogService);

        _userStatistics = userStatistics;
        _pointsTransfer = pointsTransfer;
        _channelCatalog = channelCatalog;
        _pointTerm = pointTerm;
        _dialogService = dialogService;
        _logger = logger;
        _filteredUserKeys = filteredUserKeys is null ? new HashSet<PlatformUserKey>() : [.. filteredUserKeys];
        _initialSelectedUserKeys = initialSelectedUserKeys is null ? new HashSet<PlatformUserKey>() : [.. initialSelectedUserKeys];
        _hasPageFilter = hasPageFilter && _filteredUserKeys.Count > 0;

        BuildSourceOptions(initialSource);
        RebuildTargetOptions();
        InitializeCheckedKeysForSource();
        RebuildPreview();
    }

    public event EventHandler<bool>? RequestClose;

    public string Title => "Перенос баллов между источниками";

    public ObservableCollection<TransferSourceOption> SourceOptions { get; } = [];

    public ObservableCollection<TransferTargetOption> TargetOptions { get; } = [];

    public ObservableCollection<TransferUserRowViewModel> PreviewUsers { get; } = [];

    public PointsTransferExecutionResult? ExecutionResult { get; private set; }

    public bool HasSources => SourceOptions.Count > 0;

    public bool HasTargets => TargetOptions.Count > 0;

    public bool HasPreviewUsers => PreviewUsers.Count > 0;

    public bool CanUseFilteredScope => _hasPageFilter;

    public bool IsManualSelection => SelectedScope == PointsTransferScope.Selected;

    public bool IsCrossPlatform => SelectedSource is not null
        && SelectedTarget is not null
        && SelectedSource.Source.Platform != SelectedTarget.Channel.Platform;

    public bool CanCancel => !IsBusy;

    public bool CanEditOptions => !IsBusy && !HasCompleted;

    public string CloseButtonText => HasCompleted ? "Готово" : "Отмена";

    public bool IsScopeAll
    {
        get => SelectedScope == PointsTransferScope.All;
        set
        {
            if (value)
            {
                SelectedScope = PointsTransferScope.All;
            }
        }
    }

    public bool IsScopeFiltered
    {
        get => SelectedScope == PointsTransferScope.Filtered;
        set
        {
            if (value && CanUseFilteredScope)
            {
                SelectedScope = PointsTransferScope.Filtered;
            }
        }
    }

    public bool IsScopeSelected
    {
        get => SelectedScope == PointsTransferScope.Selected;
        set
        {
            if (value)
            {
                SelectedScope = PointsTransferScope.Selected;
            }
        }
    }

    public bool IsModeAllCounters
    {
        get => SelectedMode == PointsTransferMode.AllCounters;
        set
        {
            if (value)
            {
                SelectedMode = PointsTransferMode.AllCounters;
            }
        }
    }

    public bool IsModeAdjustmentsOnly
    {
        get => SelectedMode == PointsTransferMode.AdjustmentsOnly;
        set
        {
            if (value)
            {
                SelectedMode = PointsTransferMode.AdjustmentsOnly;
            }
        }
    }

    partial void OnSelectedSourceChanged(TransferSourceOption? value)
    {
        if (_suppressPreviewRebuild)
        {
            return;
        }

        _suppressPreviewRebuild = true;
        try
        {
            RebuildTargetOptions();
            InitializeCheckedKeysForSource();
        }
        finally
        {
            _suppressPreviewRebuild = false;
        }

        RebuildPreview();
    }

    partial void OnSelectedTargetChanged(TransferTargetOption? value)
    {
        if (!_suppressPreviewRebuild)
        {
            RebuildPreview();
        }
    }

    partial void OnSelectedScopeChanged(PointsTransferScope value)
    {
        if (!_suppressPreviewRebuild)
        {
            RebuildPreview();
        }
    }

    partial void OnSelectedModeChanged(PointsTransferMode value)
    {
        if (!_suppressPreviewRebuild)
        {
            RebuildPreview();
        }
    }

    [RelayCommand]
    private void SelectAllUsers()
    {
        _suppressRowSelectionSync = true;
        try
        {
            foreach (var row in PreviewUsers)
            {
                row.IsSelected = true;
                _checkedUserKeys.Add(row.SourceUserKey);
            }
        }
        finally
        {
            _suppressRowSelectionSync = false;
        }

        RebuildPreview();
    }

    [RelayCommand]
    private void ClearSelectedUsers()
    {
        _suppressRowSelectionSync = true;
        try
        {
            foreach (var row in PreviewUsers)
            {
                row.IsSelected = false;
            }

            _checkedUserKeys.Clear();
        }
        finally
        {
            _suppressRowSelectionSync = false;
        }

        RebuildPreview();
    }

    [RelayCommand(CanExecute = nameof(CanExecuteTransfer))]
    private async Task ExecuteTransferAsync()
    {
        if (SelectedSource is null || SelectedTarget is null || IsBusy || HasCompleted)
        {
            return;
        }

        var request = BuildRequest();
        if (request is null)
        {
            return;
        }

        var preview = _pointsTransfer.Preview(request);
        if (preview.TransferredCount == 0)
        {
            return;
        }

        if (preview.TransferredCount > 1
            && !_dialogService.ConfirmWarning(
                "Подтверждение переноса баллов",
                BuildConfirmationMessage(preview)))
        {
            return;
        }

        var confirmedUserKeys = preview.Users
            .Where(static u => u.WillTransfer)
            .Select(static u => u.SourceUserKey)
            .ToArray();
        var executionRequest = request with { SourceUserKeys = confirmedUserKeys };

        IsBusy = true;
        try
        {
            var execution = await _pointsTransfer.TransferAsync(executionRequest);
            ExecutionResult = execution;
            HasCompleted = true;

            var result = execution.Result;
            var summary = $"Перенесено: {result.TransferredCount} чел. ({FormatNumber(result.TotalPoints)} {_pointTerm.ForCount(result.TotalPoints)})";
            if (result.SkippedCount > 0)
            {
                summary += $", пропущено: {result.SkippedCount} чел.";
                if (result.MissingLinkSkippedCount > 0 || result.AmbiguousLinkSkippedCount > 0)
                {
                    var reasons = new List<string>(2);
                    if (result.MissingLinkSkippedCount > 0)
                    {
                        reasons.Add($"нет связи – {result.MissingLinkSkippedCount}");
                    }

                    if (result.AmbiguousLinkSkippedCount > 0)
                    {
                        reasons.Add($"несколько аккаунтов – {result.AmbiguousLinkSkippedCount}");
                    }

                    summary += $" ({string.Join(", ", reasons)})";
                }
            }

            summary += ".";

            if (!execution.PersistedToDisk)
            {
                HasCompletionWarning = true;
                summary += execution.SaveError is { Length: > 0 } error
                    ? $" Изменения применены в памяти, но не записаны на диск: {error}"
                    : " Изменения применены только в памяти (запись каталога отключена до перезапуска).";
            }
            else
            {
                HasCompletionWarning = false;
            }

            CompletionMessage = summary;
            _logger?.UserPointsTransferred(result.TransferredCount, result.SkippedCount, result.TotalPoints);
        }
        catch (Exception ex)
        {
            _logger?.UserPointsTransferFailed(ex);
            _dialogService.Error("Ошибка переноса", $"Не удалось выполнить перенос баллов: {ex.Message}");
        }
        finally
        {
            IsBusy = false;
        }
    }

    private bool CanExecuteTransfer()
    {
        return !IsBusy
            && !HasCompleted
            && SelectedSource is not null
            && SelectedTarget is not null
            && TransferredCount > 0;
    }

    [RelayCommand(CanExecute = nameof(CanCancel))]
    private void Close()
    {
        RequestClose?.Invoke(this, HasCompleted);
    }

    private void BuildSourceOptions(PointsTransferSource? initialSource)
    {
        SourceOptions.Clear();

        var sourceChannels = UserStatisticsChannelCatalog.BuildChannels(
            _userStatistics,
            _channelCatalog,
            includeCatalogChannels: false,
            out _);

        foreach (var channel in sourceChannels)
        {
            SourceOptions.Add(new(PointsTransferSource.ForChannel(channel.Key), channel.FullLabel));
        }

        foreach (var platform in _userStatistics.GetUnattributedPlatforms())
        {
            var label = $"{PlatformVisuals.GetDisplayName(platform)} · без канала";
            SourceOptions.Add(new(PointsTransferSource.ForUnattributed(platform), label));
        }

        OnPropertyChanged(nameof(HasSources));

        _suppressPreviewRebuild = true;
        try
        {
            SelectedSource = initialSource is { IsValid: true } requested
                ? SourceOptions.FirstOrDefault(o => o.Source == requested) ?? SourceOptions.FirstOrDefault()
                : SourceOptions.FirstOrDefault();
        }
        finally
        {
            _suppressPreviewRebuild = false;
        }
    }

    private void RebuildTargetOptions()
    {
        var previousTarget = SelectedTarget?.Channel;
        TargetOptions.Clear();

        var knownTargets = UserStatisticsChannelCatalog.BuildChannels(
            _userStatistics,
            _channelCatalog,
            includeCatalogChannels: true,
            out _);

        foreach (var channel in knownTargets)
        {
            if (SelectedSource is not null && SelectedSource.Source.MatchesTarget(channel.Key))
            {
                continue;
            }

            TargetOptions.Add(new(channel.Key, channel.FullLabel));
        }

        OnPropertyChanged(nameof(HasTargets));

        SelectedTarget = previousTarget is { } prev
            ? TargetOptions.FirstOrDefault(t => t.Channel == prev) ?? TargetOptions.FirstOrDefault()
            : TargetOptions.FirstOrDefault();
    }

    private void InitializeCheckedKeysForSource()
    {
        _checkedUserKeys.Clear();

        if (SelectedSource is null || _initialSelectedUserKeys.Count == 0)
        {
            return;
        }

        foreach (var key in _initialSelectedUserKeys)
        {
            if (key.Platform == SelectedSource.Source.Platform)
            {
                _checkedUserKeys.Add(key);
            }
        }
    }

    private void RebuildPreview()
    {
        PreviewUsers.Clear();

        if (SelectedSource is null || SelectedTarget is null)
        {
            TransferredCount = 0;
            SkippedCount = 0;
            PreviewSummaryText = HasSources
                ? "Выберите целевой канал для предпросмотра."
                : "В статистике нет источников с данными для переноса.";
            PreviewDetailsText = string.Empty;
            OnPropertyChanged(nameof(HasPreviewUsers));
            return;
        }

        var listRequest = SelectedScope == PointsTransferScope.Selected
            ? new PointsTransferRequest(SelectedSource.Source, SelectedTarget.Channel, SelectedMode, null)
            : BuildRequest();

        if (listRequest is null)
        {
            OnPropertyChanged(nameof(HasPreviewUsers));
            return;
        }

        var listPreview = _pointsTransfer.Preview(listRequest);

        _suppressRowSelectionSync = true;
        try
        {
            foreach (var user in listPreview.Users)
            {
                var isChecked = SelectedScope != PointsTransferScope.Selected || _checkedUserKeys.Contains(user.SourceUserKey);
                PreviewUsers.Add(new TransferUserRowViewModel(
                    user,
                    SelectedSource.Source.Platform,
                    _pointTerm,
                    isChecked,
                    OnUserRowSelectionChanged));
            }
        }
        finally
        {
            _suppressRowSelectionSync = false;
        }

        OnPropertyChanged(nameof(HasPreviewUsers));

        var effectivePreview = SelectedScope == PointsTransferScope.Selected
            ? _pointsTransfer.Preview(new PointsTransferRequest(SelectedSource.Source, SelectedTarget.Channel, SelectedMode, _checkedUserKeys))
            : listPreview;

        TransferredCount = effectivePreview.TransferredCount;
        SkippedCount = effectivePreview.SkippedCount;

        if (effectivePreview.Users.Count == 0)
        {
            PreviewSummaryText = SelectedScope == PointsTransferScope.Selected
                ? "Отметьте хотя бы одного зрителя в списке."
                : "В выбранном охвате нет пользователей для переноса.";
            PreviewDetailsText = string.Empty;
            return;
        }

        var summary = $"Перенесётся: {effectivePreview.TransferredCount} чел. · {FormatNumber(effectivePreview.TotalPoints)} {_pointTerm.ForCount(effectivePreview.TotalPoints)}";
        if (effectivePreview.SkippedCount > 0)
        {
            summary += $" · пропущено: {effectivePreview.SkippedCount} чел.";
        }

        PreviewSummaryText = summary;
        PreviewDetailsText = $"Сообщений: {FormatNumber(effectivePreview.TotalMessages)} · бонусов: +{FormatNumber(effectivePreview.TotalBonusPoints)} · штрафов: −{FormatNumber(effectivePreview.TotalPenaltyPoints)}";
    }

    private void OnUserRowSelectionChanged(PlatformUserKey userKey, bool isSelected)
    {
        if (_suppressRowSelectionSync)
        {
            return;
        }

        if (isSelected)
        {
            _checkedUserKeys.Add(userKey);
        }
        else
        {
            _checkedUserKeys.Remove(userKey);
        }

        if (SelectedSource is null || SelectedTarget is null || SelectedScope != PointsTransferScope.Selected)
        {
            return;
        }

        var effectivePreview = _pointsTransfer.Preview(new PointsTransferRequest(
            SelectedSource.Source,
            SelectedTarget.Channel,
            SelectedMode,
            _checkedUserKeys));

        TransferredCount = effectivePreview.TransferredCount;
        SkippedCount = effectivePreview.SkippedCount;

        if (_checkedUserKeys.Count == 0)
        {
            PreviewSummaryText = "Отметьте хотя бы одного зрителя в списке.";
            PreviewDetailsText = string.Empty;
            return;
        }

        var summary = $"Перенесётся: {effectivePreview.TransferredCount} чел. · {FormatNumber(effectivePreview.TotalPoints)} {_pointTerm.ForCount(effectivePreview.TotalPoints)}";
        if (effectivePreview.SkippedCount > 0)
        {
            summary += $" · пропущено: {effectivePreview.SkippedCount} чел.";
        }

        PreviewSummaryText = summary;
        PreviewDetailsText = $"Сообщений: {FormatNumber(effectivePreview.TotalMessages)} · бонусов: +{FormatNumber(effectivePreview.TotalBonusPoints)} · штрафов: −{FormatNumber(effectivePreview.TotalPenaltyPoints)}";
    }

    private PointsTransferRequest? BuildRequest()
    {
        if (SelectedSource is null || SelectedTarget is null)
        {
            return null;
        }

        IReadOnlyCollection<PlatformUserKey>? filter = SelectedScope switch
        {
            PointsTransferScope.Filtered => _filteredUserKeys,
            PointsTransferScope.Selected => _checkedUserKeys,
            _ => null,
        };

        return new PointsTransferRequest(SelectedSource.Source, SelectedTarget.Channel, SelectedMode, filter);
    }

    private string BuildConfirmationMessage(PointsTransferPreview preview)
    {
        var sourceLabel = SelectedSource?.Label ?? string.Empty;
        var targetLabel = SelectedTarget?.Label ?? string.Empty;
        var cleanupNote = preview.Mode == PointsTransferMode.AllCounters
            ? "Перенесённые записи в источнике будут удалены."
            : "Бонусы и штрафы у перенесённых записей в источнике будут обнулены, сообщения останутся.";

        var skippedNote = preview.SkippedCount > 0
            ? $"\nПропущено (без однозначной связи или без данных): {preview.SkippedCount} чел."
            : string.Empty;

        return $"Перенести баллы {preview.TransferredCount} пользователей из «{sourceLabel}» в «{targetLabel}»?\n\n"
            + $"Итого к переносу: {FormatNumber(preview.TotalPoints)} {_pointTerm.ForCount(preview.TotalPoints)} "
            + $"(сообщений: {FormatNumber(preview.TotalMessages)}, бонусов: +{FormatNumber(preview.TotalBonusPoints)}, штрафов: −{FormatNumber(preview.TotalPenaltyPoints)})."
            + $"{skippedNote}\n\n{cleanupNote}";
    }

    private static string FormatNumber(long value) => value.ToString("N0", UiCulture.Russian);

    private static string FormatNumber(ulong value) => value.ToString("N0", UiCulture.Russian);
}

public sealed record TransferSourceOption(PointsTransferSource Source, string Label);

public sealed record TransferTargetOption(ChannelKey Channel, string Label);

public sealed partial class TransferUserRowViewModel : ObservableObject
{
    private readonly Action<PlatformUserKey, bool> _onSelectionChanged;

    [ObservableProperty]
    private bool _isSelected;

    public TransferUserRowViewModel(
        PointsTransferUserPreview preview,
        StreamingPlatform sourcePlatform,
        PointTerm pointTerm,
        bool isSelected,
        Action<PlatformUserKey, bool> onSelectionChanged)
    {
        SourceUserKey = preview.SourceUserKey;
        SourcePlatform = sourcePlatform;
        _isSelected = isSelected;
        _onSelectionChanged = onSelectionChanged;

        DisplayName = preview.TargetUserName is { Length: > 0 } targetName && preview.TargetUserKey != preview.SourceUserKey
            ? $"{preview.SourceUserName} → {targetName}"
            : preview.SourceUserName;

        CountersText = $"{preview.Points.ToString("N0", UiCulture.Russian)} {pointTerm.ForCount(preview.Points)} · {preview.MessageCount.ToString("N0", UiCulture.Russian)} сообщ.";
        WillTransfer = preview.WillTransfer;
        IsWarning = preview.SkipReason is PointsTransferSkipReason.MissingTargetAccountLink or PointsTransferSkipReason.AmbiguousTargetAccountLink;
        StatusText = preview.SkipReason switch
        {
            PointsTransferSkipReason.None => "Перенесётся",
            PointsTransferSkipReason.MissingTargetAccountLink => "Пропущен: нет связанного аккаунта",
            PointsTransferSkipReason.AmbiguousTargetAccountLink => "Пропущен: несколько связанных аккаунтов",
            PointsTransferSkipReason.NothingToTransfer => "Пропущен: нет бонусов и штрафов",
            _ => string.Empty,
        };
    }

    public PlatformUserKey SourceUserKey { get; }

    public StreamingPlatform SourcePlatform { get; }

    public string DisplayName { get; }

    public string CountersText { get; }

    public bool WillTransfer { get; }

    public bool IsWarning { get; }

    public string StatusText { get; }

    partial void OnIsSelectedChanged(bool value)
    {
        _onSelectionChanged(SourceUserKey, value);
    }
}
