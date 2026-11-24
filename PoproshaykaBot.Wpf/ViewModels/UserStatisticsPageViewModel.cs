using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using KeepShell.Services.Platform;
using Microsoft.Extensions.Logging;
using PoproshaykaBot.Core.Chat;
using PoproshaykaBot.Core.Infrastructure;
using PoproshaykaBot.Core.Infrastructure.Events;
using PoproshaykaBot.Core.Infrastructure.Events.Moderation;
using PoproshaykaBot.Core.Infrastructure.Events.Settings;
using PoproshaykaBot.Core.Settings;
using PoproshaykaBot.Core.Settings.Stores;
using PoproshaykaBot.Core.Statistics;
using PoproshaykaBot.Core.Statistics.Linking;
using PoproshaykaBot.Core.Users;
using PoproshaykaBot.Core.Platforms;
using PoproshaykaBot.Wpf.Bootstrap;
using PoproshaykaBot.Wpf.Infrastructure;
using PoproshaykaBot.Wpf.Infrastructure.Platforms;
using PoproshaykaBot.Wpf.ViewModels.Dialogs;
using Serilog;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Text;
using System.Windows.Input;

namespace PoproshaykaBot.Wpf.ViewModels;

public sealed partial class UserStatisticsPageViewModel : ObservableObject, IPageHeader, IPageRefresh, IDisposable
{
    public const long MaxAdjustment = 1_000_000;

    private const string AdjustmentSource = "WPF, страница «Пользователи», блок «Баланс»";

    private readonly IUserStatisticsRepository _userStatistics;
    private readonly StatisticsAutoSaver _statisticsAutoSaver;
    private readonly UserRankService _userRankService;
    private readonly UserPointsManagementService _pointsManagement;
    private readonly IChannelProvider _channelProvider;
    private readonly IChatPlatformRegistry? _platformRegistry;
    private readonly SettingsManager _settingsManager;
    private readonly IDialogService _dialogService;
    private readonly ISettingsStore _settings;
    private readonly UserAvatarProvider _avatars;
    private readonly StatisticsPreload? _preload;
    private readonly IUiDispatcher? _uiDispatcher;
    private readonly ChannelCatalogStore? _channelCatalog;
    private readonly IAccountLinkingService? _accountLinking;
    private readonly PointsTransferService _pointsTransfer;
    private readonly ILogger<UserStatisticsPageViewModel>? _logger;
    private readonly List<UserStatisticsRowViewModel> _allRows = [];
    private readonly ObservableCollection<UserStatisticsRowViewModel> _rows = [];
    private readonly ObservableCollection<UserStatisticsSliceOption> _sliceOptions = [];
    private readonly ObservableCollection<AdjustmentTargetOption> _adjustmentTargetOptions = [];
    private readonly List<IDisposable> _subscriptions = [];
    private int _platformSliceCount;
    private int _allChannelsCount;
    private bool _isHydrated;
    private bool _isDisposed;
    private bool _summaryUserToggled;
    private Task? _activeHydrationTask;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSelectedRow))]
    [NotifyPropertyChangedFor(nameof(SelectedPlaceText))]
    [NotifyPropertyChangedFor(nameof(CanManageLinks))]
    [NotifyCanExecuteChangedFor(nameof(ApplyAdjustmentCommand))]
    [NotifyCanExecuteChangedFor(nameof(LinkAccountCommand))]
    [NotifyPropertyChangedFor(nameof(RankLadderSteps))]
    [NotifyPropertyChangedFor(nameof(RankLadderToggleText))]
    private UserStatisticsRowViewModel? _selectedRow;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(RankLadderSteps))]
    [NotifyPropertyChangedFor(nameof(RankLadderToggleText))]
    private bool _isRankLadderExpanded;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(BalanceToggleCaption))]
    private bool _isBalanceExpanded;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowExpandedSummary))]
    [NotifyPropertyChangedFor(nameof(SummaryToggleCaption))]
    private bool _isSummaryExpanded;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowExpandedSummary))]
    [NotifyPropertyChangedFor(nameof(SummaryToggleCaption))]
    private bool _isSummaryHeightConstrained;

    [ObservableProperty]
    private string _filterText = string.Empty;

    [ObservableProperty]
    private PlatformFilterOption _selectedPlatformFilter = PlatformFilterOption.All;

    [ObservableProperty]
    private UserStatisticsSliceOption _selectedSliceOption = UserStatisticsSliceOption.All;

    [ObservableProperty]
    private AdjustmentTargetOption? _selectedAdjustmentTarget;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(EmptyActionCommand))]
    private bool _isFilterActive;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PointsSortIndicator))]
    [NotifyPropertyChangedFor(nameof(MessagesSortIndicator))]
    [NotifyPropertyChangedFor(nameof(NameSortIndicator))]
    [NotifyPropertyChangedFor(nameof(RankSortIndicator))]
    private UserStatisticsSortKey _sortKey = UserStatisticsSortKey.Points;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PointsSortIndicator))]
    [NotifyPropertyChangedFor(nameof(MessagesSortIndicator))]
    [NotifyPropertyChangedFor(nameof(NameSortIndicator))]
    [NotifyPropertyChangedFor(nameof(RankSortIndicator))]
    private bool _sortDescending = true;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(AdjustmentAmount))]
    [NotifyPropertyChangedFor(nameof(AdjustmentError))]
    [NotifyPropertyChangedFor(nameof(HasAdjustmentError))]
    [NotifyPropertyChangedFor(nameof(ActionButtonText))]
    [NotifyCanExecuteChangedFor(nameof(ApplyAdjustmentCommand))]
    [NotifyCanExecuteChangedFor(nameof(IncreaseAdjustmentCommand))]
    [NotifyCanExecuteChangedFor(nameof(DecreaseAdjustmentCommand))]
    private string _adjustmentText = string.Empty;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ApplyAdjustmentCommand))]
    private bool _isBusy;

    private bool _suppressFilterRebuild;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SummaryLine))]
    private string _totalUsers = "0";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SummaryLine))]
    private string _totalMessages = "0";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SummaryLine))]
    private string _totalPoints = "0";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SummaryLine))]
    private string _totalBonus = "0";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SummaryLine))]
    private string _totalPenalty = "0";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SummaryLine))]
    private bool _hasTotalBonus;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SummaryLine))]
    private bool _hasTotalPenalty;

    public UserStatisticsPageViewModel(
        IUserStatisticsRepository userStatistics,
        StatisticsAutoSaver statisticsAutoSaver,
        UserRankService userRankService,
        UserPointsManagementService pointsManagement,
        IChannelProvider channelProvider,
        SettingsManager settingsManager,
        IDialogService dialogService,
        IEventBus eventBus,
        ISettingsStore settings,
        UserAvatarProvider avatars,
        IChatPlatformRegistry? platformRegistry = null,
        StatisticsPreload? preload = null,
        IUiDispatcher? uiDispatcher = null,
        ChannelCatalogStore? channelCatalog = null,
        IAccountLinkingService? accountLinking = null,
        ILogger<UserStatisticsPageViewModel>? logger = null,
        PointsTransferService? pointsTransfer = null)
    {
        _userStatistics = userStatistics;
        _statisticsAutoSaver = statisticsAutoSaver;
        _userRankService = userRankService;
        _pointsManagement = pointsManagement;
        _channelProvider = channelProvider;
        _platformRegistry = platformRegistry;
        _settingsManager = settingsManager;
        _dialogService = dialogService;
        _settings = settings;
        _avatars = avatars;
        _preload = preload;
        _uiDispatcher = uiDispatcher;
        _channelCatalog = channelCatalog;
        _accountLinking = accountLinking;
        _logger = logger;
        _pointsTransfer = pointsTransfer ?? new PointsTransferService(userStatistics, eventBus, statisticsAutoSaver);
        _isBalanceExpanded = settings.GetBool(SettingsKeys.UserBalanceExpanded, true);
        _isSummaryExpanded = settings.GetBool(SettingsKeys.UserSummaryExpanded, true);

        _subscriptions.Add(eventBus.SubscribeOnUi<UserPunished>(_ => Reload()));
        _subscriptions.Add(eventBus.SubscribeOnUi<UserRewarded>(_ => Reload()));
        _subscriptions.Add(eventBus.SubscribeOnUi<RanksSettingsChanged>(_ => Reload()));
        _subscriptions.Add(eventBus.SubscribeOnUi<AccountLinksChanged>(_ => Reload()));
        _subscriptions.Add(eventBus.SubscribeOnUi<UserPointsTransferred>(_ => Reload()));

        if (_channelCatalog is not null)
        {
            _channelCatalog.Changed += OnChannelCatalogChanged;
        }

        RefreshSliceOptions();

        if (_preload is null)
        {
            Reload();
        }
        else
        {
            _preload.ScheduleAfterLoad(CompleteDeferredHydration);
        }
    }

    public void OnEnter()
    {
        _summaryUserToggled = false;
        OnPropertyChanged(nameof(ShowExpandedSummary));
        OnPropertyChanged(nameof(SummaryToggleCaption));

        if (_isDisposed || _isHydrated)
        {
            return;
        }

        if (_preload is null || _preload.IsCompleted)
        {
            Reload();
            return;
        }

        _activeHydrationTask ??= UiAsync.RunGuardedAsync(HydrateAfterPreloadAsync, LogHydrationFailure);
    }

    public string PageTitle => "Пользователи";

    public string? PageDescription => "Статистика сообщений, баллов и рангов";

    public event Action<string, StatusSeverity>? StatusReported;

    public ObservableCollection<UserStatisticsRowViewModel> Users => _rows;

    public IReadOnlyList<PlatformFilterOption> PlatformOptions => PlatformFilterOption.AllOptions;

    public ObservableCollection<UserStatisticsSliceOption> SliceOptions => _sliceOptions;

    public ObservableCollection<AdjustmentTargetOption> AdjustmentTargetOptions => _adjustmentTargetOptions;

    public bool HasMultipleAdjustmentTargets => _adjustmentTargetOptions.Count > 1;

    public bool CanManageLinks => _accountLinking is not null && SelectedRow is not null;

    public bool HasUsers => _rows.Count > 0;

    public string EmptyHeading => IsFilterActive ? "Ничего не найдено" : "Статистики пока нет";

    public string EmptyDescription => IsFilterActive
        ? "Ни один пользователь не подходит под фильтр. Сбросьте фильтр, чтобы увидеть всех."
        : "Пользователи появятся здесь, когда бот начнёт собирать сообщения чата.";

    public bool HasSelectedRow => SelectedRow is not null;

    public string? EmptyActionText => "Сбросить фильтр";

    public IRelayCommand? EmptyActionCommand => IsFilterActive ? ClearFilterCommand : null;

    public TableSortIndicator PointsSortIndicator => IndicatorFor(UserStatisticsSortKey.Points);

    public TableSortIndicator MessagesSortIndicator => IndicatorFor(UserStatisticsSortKey.Messages);

    public TableSortIndicator NameSortIndicator => IndicatorFor(UserStatisticsSortKey.Name);

    public TableSortIndicator RankSortIndicator => IndicatorFor(UserStatisticsSortKey.Rank);

    public string VisibleCountText => IsFilterActive
        ? UserStatisticsRanking.DescribeVisibleCount(_rows.Count, _allChannelsCount)
        : string.Empty;

    public string SelectedPlaceText => SelectedRow is { } row
        ? UserStatisticsRanking.DescribePlace(row.Place, _platformSliceCount)
        : string.Empty;

    public IReadOnlyList<UserRankLadderStep> RankLadderSteps => SelectedRow?.RankLadder?.Arrange(IsRankLadderExpanded) ?? [];

    public string RankLadderToggleText => IsRankLadderExpanded
        ? "Свернуть"
        : SelectedRow?.RankLadder?.ExpandText ?? string.Empty;

    public string BalanceToggleCaption => IsBalanceExpanded ? "Свернуть блок «Баланс»" : "Развернуть блок «Баланс»";

    public bool ShowExpandedSummary => IsSummaryExpanded && (!IsSummaryHeightConstrained || _summaryUserToggled);

    public string SummaryToggleCaption => ShowExpandedSummary ? "Скрыть сводку" : "Показать сводку";

    public string SummaryLine
    {
        get
        {
            var baseLine = $"{TotalUsers} участн. · {TotalMessages} сообщ. · {TotalPoints} баллов";
            if (!HasTotalBonus && !HasTotalPenalty)
            {
                return baseLine;
            }

            var extras = new List<string>(2);
            if (HasTotalBonus)
            {
                extras.Add($"+{TotalBonus} бонус");
            }

            if (HasTotalPenalty)
            {
                extras.Add($"−{TotalPenalty} штраф");
            }

            return $"{baseLine} · {string.Join(" · ", extras)}";
        }
    }

    public long AdjustmentAmount => ParseAdjustment(AdjustmentText).Amount;

    public string? AdjustmentError => ParseAdjustment(AdjustmentText).Error;

    public bool HasAdjustmentError => AdjustmentError is not null;

    public string ActionButtonText
    {
        get
        {
            var delta = AdjustmentAmount;
            var term = _userRankService.PointTerm;
            return delta switch
            {
                > 0 => $"Добавить {delta} {term.ForCount(delta)}",
                < 0 => $"Убрать {-delta} {term.ForCount(-delta)}",
                _ => "Изменить баланс",
            };
        }
    }

    internal bool NeedsPreloadWait => !_isHydrated && _preload is { IsCompleted: false };

    public bool TrySelectAt(int index)
    {
        if (index < 0 || index >= _rows.Count)
        {
            return false;
        }

        SelectedRow = _rows[index];

        return true;
    }

    public bool TrySelect(string? userId)
    {
        return TrySelect(new PlatformUserKey(StreamingPlatform.Twitch, userId ?? string.Empty));
    }

    public bool TrySelect(PlatformUserKey key)
    {
        if (key.UserId is not { Length: > 0 })
        {
            return false;
        }

        if (!_isHydrated && (_preload is null || _preload.IsCompleted))
        {
            Reload();
        }

        if (_allRows.All(row => !row.MatchesAccount(key)))
        {
            if (SelectedSliceOption.Slice.Kind == StatisticsSliceKind.AllChannels
                || _userStatistics.FindViewerById(StatisticsSlice.All(), key) is null)
            {
                return false;
            }

            ResetFilters();
        }

        if (_rows.All(row => !row.MatchesAccount(key)))
        {
            ResetFilters();
        }

        SelectedRow = _rows.FirstOrDefault(row => row.MatchesAccount(key));

        return SelectedRow is not null;
    }

    public async Task<bool> TrySelectAfterPreloadAsync(PlatformUserKey key)
    {
        if (key.UserId is not { Length: > 0 } || _isDisposed)
        {
            return false;
        }

        if (!_isHydrated && _preload is { IsCompleted: false })
        {
            await (_activeHydrationTask ??= UiAsync.RunGuardedAsync(HydrateAfterPreloadAsync, LogHydrationFailure));
        }

        if (_isDisposed)
        {
            return false;
        }

        if (_uiDispatcher is not null)
        {
            var selected = false;
            await _uiDispatcher.InvokeAsync(() => selected = TrySelect(key));
            return selected;
        }

        return TrySelect(key);
    }

    public void RequestAvatar(UserAvatarViewModel avatar)
    {
        _avatars.Request(avatar);
    }

    partial void OnSelectedRowChanged(UserStatisticsRowViewModel? value)
    {
        if (value is not null)
        {
            _avatars.Request(value.Avatar);
        }

        RebuildAdjustmentTargets(value);
    }

    partial void OnFilterTextChanged(string value)
    {
        if (!_suppressFilterRebuild)
        {
            RebuildView();
        }
    }

    partial void OnSelectedPlatformFilterChanged(PlatformFilterOption value)
    {
        if (_suppressFilterRebuild)
        {
            return;
        }

        var nextSlice = value?.Platform is { } platform
            ? _sliceOptions.FirstOrDefault(o =>
                o.Slice.Kind == StatisticsSliceKind.Platform && o.Slice.Platform == platform)
                ?? new UserStatisticsSliceOption(
                    $"platform:{platform}",
                    $"Площадка: {value.Label}",
                    StatisticsSlice.ForPlatform(platform))
            : _sliceOptions.FirstOrDefault() ?? UserStatisticsSliceOption.All;

        _suppressFilterRebuild = true;
        try
        {
            SelectedSliceOption = nextSlice;
        }
        finally
        {
            _suppressFilterRebuild = false;
        }

        Reload();
    }

    partial void OnSelectedSliceOptionChanged(UserStatisticsSliceOption value)
    {
        if (_suppressFilterRebuild || value is null)
        {
            return;
        }

        _suppressFilterRebuild = true;
        try
        {
            SelectedPlatformFilter = value.Slice.Kind == StatisticsSliceKind.Platform && value.Slice.Platform is { } platform
                ? PlatformFilterOption.AllOptions.FirstOrDefault(o => o.Platform == platform) ?? PlatformFilterOption.All
                : PlatformFilterOption.All;
        }
        finally
        {
            _suppressFilterRebuild = false;
        }

        Reload();
    }

    private void ApplySliceSummaryAndRebuild()
    {
        _platformSliceCount = _allRows.Count;

        long totalMessages = 0;
        long totalPoints = 0;
        long totalBonus = 0;
        long totalPenalty = 0;

        foreach (var row in _allRows)
        {
            totalMessages += (long)row.MessageCount;
            totalPoints += row.Points;
            totalBonus += (long)row.BonusPoints;
            totalPenalty += (long)row.PenaltyPoints;
        }

        TotalUsers = _allRows.Count.ToString("N0", UiCulture.Russian);
        TotalMessages = totalMessages.ToString("N0", UiCulture.Russian);
        TotalPoints = totalPoints.ToString("N0", UiCulture.Russian);
        TotalBonus = totalBonus > 0 ? string.Create(UiCulture.Russian, $"+{totalBonus:N0}") : "0";
        TotalPenalty = totalPenalty > 0 ? string.Create(UiCulture.Russian, $"−{totalPenalty:N0}") : "0";
        HasTotalBonus = totalBonus > 0;
        HasTotalPenalty = totalPenalty > 0;

        UserStatisticsRanking.AssignPlaces(_allRows);
        RebuildView(_allRows);
    }

    private void RebuildView(List<UserStatisticsRowViewModel>? sliceSource = null)
    {
        var source = sliceSource ?? _allRows;
        var selectedKey = SelectedRow?.Key;
        var trimmed = FilterText.Trim();
        var hasTextFilter = trimmed.Length > 0;
        var hasSliceFilter = SelectedSliceOption is not null
            && SelectedSliceOption.Slice.Kind != StatisticsSliceKind.AllChannels;
        var visible = new List<UserStatisticsRowViewModel>(source.Count);

        foreach (var row in source)
        {
            if (hasTextFilter && !row.MatchesQuery(trimmed))
            {
                continue;
            }

            visible.Add(row);
        }

        _rows.SyncTo(UserStatisticsRanking.Arrange(visible, SortKey, SortDescending));

        SelectedRow = selectedKey is { } key
            ? _rows.FirstOrDefault(row => row.MatchesAccount(key))
            : null;

        IsFilterActive = hasTextFilter || hasSliceFilter;
        OnPropertyChanged(nameof(HasUsers));
        OnPropertyChanged(nameof(EmptyHeading));
        OnPropertyChanged(nameof(EmptyDescription));
        OnPropertyChanged(nameof(VisibleCountText));
        OnPropertyChanged(nameof(SelectedPlaceText));
    }

    private TableSortIndicator IndicatorFor(UserStatisticsSortKey key)
    {
        if (SortKey != key)
        {
            return TableSortIndicator.None;
        }

        return SortDescending ? TableSortIndicator.Descending : TableSortIndicator.Ascending;
    }

    [RelayCommand]
    private void SortBy(UserStatisticsSortKey key)
    {
        if (key == UserStatisticsSortKey.None)
        {
            return;
        }

        if (SortKey == key)
        {
            SortDescending = !SortDescending;
        }
        else
        {
            SortKey = key;
            SortDescending = key != UserStatisticsSortKey.Name;
        }

        OnPropertyChanged(nameof(SortKey));
        RebuildView();
    }

    [RelayCommand]
    private void ClearFilter() => ResetFilters();

    private void ResetFilters()
    {
        var sliceChanged = SelectedPlatformFilter != PlatformFilterOption.All
            || SelectedSliceOption?.Slice.Kind != StatisticsSliceKind.AllChannels;
        var textChanged = FilterText.Length > 0;

        if (!sliceChanged && !textChanged)
        {
            return;
        }

        _suppressFilterRebuild = true;
        try
        {
            SelectedPlatformFilter = PlatformFilterOption.All;
            SelectedSliceOption = _sliceOptions.FirstOrDefault() ?? UserStatisticsSliceOption.All;
            FilterText = string.Empty;
        }
        finally
        {
            _suppressFilterRebuild = false;
        }

        if (sliceChanged)
        {
            Reload();
        }
        else
        {
            RebuildView();
        }
    }

    [RelayCommand]
    private void ToggleRankLadder() => IsRankLadderExpanded = !IsRankLadderExpanded;

    [RelayCommand]
    private void ToggleBalance() => IsBalanceExpanded = !IsBalanceExpanded;

    partial void OnIsBalanceExpandedChanged(bool value)
    {
        _settings.SetBool(SettingsKeys.UserBalanceExpanded, value);
    }

    [RelayCommand]
    private void ToggleSummary()
    {
        if (IsSummaryHeightConstrained && IsSummaryExpanded && !_summaryUserToggled)
        {
            _summaryUserToggled = true;
            OnPropertyChanged(nameof(ShowExpandedSummary));
            OnPropertyChanged(nameof(SummaryToggleCaption));
            return;
        }

        if (IsSummaryHeightConstrained && !IsSummaryExpanded)
        {
            _summaryUserToggled = true;
            IsSummaryExpanded = true;
            return;
        }

        IsSummaryExpanded = !IsSummaryExpanded;
        if (!IsSummaryExpanded)
        {
            _summaryUserToggled = false;
        }
    }

    partial void OnIsSummaryExpandedChanged(bool value)
    {
        _settings.SetBool(SettingsKeys.UserSummaryExpanded, value);
    }

    [RelayCommand]
    private void SetAdjustment(double amount) => SetAdjustmentAmount((long)amount);

    ICommand IPageRefresh.RefreshCommand => RefreshCommand;

    [RelayCommand]
    private void Refresh() => Reload();

    [RelayCommand(CanExecute = nameof(CanApplyAdjustment))]
    private async Task ApplyAdjustmentAsync()
    {
        if (SelectedRow is null)
        {
            return;
        }

        var row = SelectedRow;
        var (delta, error) = ParseAdjustment(AdjustmentText);

        if (delta == 0 || error is not null)
        {
            return;
        }

        IsBusy = true;

        try
        {
            bool updated;
            var adjustmentTarget = SelectedAdjustmentTarget;
            var targetPlatform = adjustmentTarget?.Target.Platform ?? row.Platform;
            var channel = adjustmentTarget?.Channel
                ?? (_platformRegistry is not null
                    ? _platformRegistry.GetConfiguredChannel(targetPlatform) ?? string.Empty
                    : _channelProvider.Channel);
            var targetUserKey = adjustmentTarget?.UserKey ?? row.Key;
            var targetUserName = adjustmentTarget?.UserName ?? row.Name;

            if (delta < 0)
            {
                var amount = (ulong)-delta;
                var notification = _pointsManagement.GetPunishmentNotification(targetUserName, amount);
                _dialogService.Info("Наказание", notification);
                updated = adjustmentTarget is not null
                    ? await _pointsManagement.PunishUserAsync(
                        adjustmentTarget.Target,
                        targetUserKey,
                        targetUserName,
                        amount,
                        channel,
                        AdjustmentSource)
                    : await _pointsManagement.PunishUserAsync(targetUserKey, targetUserName, amount, channel, AdjustmentSource);
            }
            else
            {
                var amount = (ulong)delta;
                var notification = _pointsManagement.GetRewardNotification(targetUserName, amount);
                _dialogService.Info("Поощрение", notification);
                updated = adjustmentTarget is not null
                    ? await _pointsManagement.RewardUserAsync(
                        adjustmentTarget.Target,
                        targetUserKey,
                        targetUserName,
                        amount,
                        channel,
                        AdjustmentSource)
                    : await _pointsManagement.RewardUserAsync(targetUserKey, targetUserName, amount, channel, AdjustmentSource);
            }

            if (!updated)
            {
                _dialogService.Warning("Ошибка", "Пользователь не найден в статистике.");
                return;
            }

            Reload();

            try
            {
                await _statisticsAutoSaver.SaveNowAsync();
            }
            catch (Exception ex)
            {
                _dialogService.Error("Ошибка сохранения", $"Не удалось сохранить статистику: {ex.Message}");
            }
        }
        catch (Exception ex)
        {
            _logger?.UserBalanceAdjustmentFailed(ex);
            var verb = delta < 0 ? "наказать" : "поощрить";
            _dialogService.Error("Ошибка", $"Не удалось {verb} пользователя: {ex.Message}");
        }
        finally
        {
            IsBusy = false;
        }
    }

    private bool CanApplyAdjustment() => SelectedRow is not null && AdjustmentAmount != 0 && !HasAdjustmentError && !IsBusy;

    [RelayCommand(CanExecute = nameof(CanIncreaseAdjustment))]
    private void IncreaseAdjustment() => SetAdjustmentAmount(AdjustmentAmount + 1);

    private bool CanIncreaseAdjustment() => !HasAdjustmentError && AdjustmentAmount < MaxAdjustment;

    [RelayCommand(CanExecute = nameof(CanDecreaseAdjustment))]
    private void DecreaseAdjustment() => SetAdjustmentAmount(AdjustmentAmount - 1);

    private bool CanDecreaseAdjustment() => !HasAdjustmentError && AdjustmentAmount > -MaxAdjustment;

    private void SetAdjustmentAmount(long amount)
    {
        AdjustmentText = amount == 0
            ? string.Empty
            : amount.ToString(CultureInfo.InvariantCulture);
    }

    private static (long Amount, string? Error) ParseAdjustment(string? text)
    {
        var normalized = new StringBuilder(text?.Length ?? 0);

        foreach (var symbol in text ?? string.Empty)
        {
            if (char.IsWhiteSpace(symbol))
            {
                continue;
            }

            normalized.Append(symbol == '−' ? '-' : symbol);
        }

        if (normalized.Length == 0)
        {
            return (0, null);
        }

        var candidate = normalized.ToString();
        var digits = candidate.TrimStart('+', '-');

        if (candidate is "+" or "-")
        {
            return (0, null);
        }

        var isInteger = candidate.Length - digits.Length <= 1 && digits.Length > 0 && digits.All(char.IsAsciiDigit);

        if (!isInteger)
        {
            return (0, "Введите целое число, например 50 или −20.");
        }

        if (!long.TryParse(candidate, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var amount)
            || amount is > MaxAdjustment or < -MaxAdjustment)
        {
            return (0, "За один раз можно изменить баланс не больше чем на 1 000 000.");
        }

        return (amount, null);
    }

    [RelayCommand]
    private async Task EditPointTermAsync()
    {
        var dialog = new PointTermDialogViewModel(_settingsManager.Current.Ranks.PointTerm);

        if (!await _dialogService.ShowAsync(dialog))
        {
            return;
        }

        try
        {
            var written = _settingsManager.Mutate(settings => settings.Ranks.PointTerm = dialog.BuildResult());
            OnPropertyChanged(nameof(ActionButtonText));
            Reload();

            if (!written)
            {
                _dialogService.Warning("Названия баллов",
                    "Названия применены и работают до перезапуска. В файл они не записаны: туда только что перенесены данные предыдущей версии. Перезапустите приложение и задайте названия ещё раз.");
            }
        }
        catch (Exception ex)
        {
            _dialogService.Error("Ошибка сохранения", $"Не удалось сохранить названия баллов: {ex.Message}");
        }
    }

    [RelayCommand]
    private async Task TransferPointsAsync()
    {
        PointsTransferSource? initialSource = SelectedSliceOption?.Slice.Kind switch
        {
            StatisticsSliceKind.Channel when SelectedSliceOption.Slice.Channel is { } channel
                => PointsTransferSource.ForChannel(channel),
            StatisticsSliceKind.UnattributedPlatform when SelectedSliceOption.Slice.Platform is { } platform
                => PointsTransferSource.ForUnattributed(platform),
            _ => null,
        };

        var filteredKeys = _rows
            .SelectMany(static r => r.Aggregate.Accounts.Select(static a => a.Key))
            .Distinct()
            .ToList();

        var selectedKeys = SelectedRow?.Aggregate.Accounts
            .Select(static a => a.Key)
            .ToList() ?? [];

        var previousSelectedKey = SelectedRow?.Key;

        var dialog = new TransferPointsDialogViewModel(
            _userStatistics,
            _pointsTransfer,
            _channelCatalog,
            _userRankService.PointTerm,
            _dialogService,
            initialSource,
            filteredKeys,
            selectedKeys,
            hasPageFilter: IsFilterActive,
            logger: _logger);

        await _dialogService.ShowAsync(dialog);

        if (dialog.ExecutionResult is not { Result: { TransferredCount: > 0 } result } execution)
        {
            return;
        }

        Reload();
        if (previousSelectedKey is { } key)
        {
            TrySelect(key);
        }

        var pointTerm = _userRankService.PointTerm;
        var pointsLabel = $"{result.TotalPoints.ToString("N0", UiCulture.Russian)} {pointTerm.ForCount(result.TotalPoints)}";
        var statusMessage = result.SkippedCount > 0
            ? $"Перенос баллов завершён: перенесено {result.TransferredCount} ({pointsLabel}), пропущено {result.SkippedCount}"
            : $"Перенос баллов завершён: перенесено {result.TransferredCount} ({pointsLabel})";

        if (!execution.PersistedToDisk)
        {
            StatusReported?.Invoke(
                $"{statusMessage}, но каталог не записан на диск",
                StatusSeverity.Warning);
            return;
        }

        StatusReported?.Invoke(statusMessage, StatusSeverity.Info);
    }

    [RelayCommand(CanExecute = nameof(CanManageLinks))]
    private async Task LinkAccountAsync()
    {
        if (_accountLinking is null || SelectedRow is not { } row)
        {
            return;
        }

        var excludedKeys = _userStatistics.GetLinkedKeys(row.Key);
        var knownAccounts = _userStatistics.GetKnownAccounts();
        var dialog = new LinkAccountDialogViewModel(row.Name, knownAccounts, excludedKeys);

        if (!await _dialogService.ShowAsync(dialog) || dialog.Result is not { } targetKey)
        {
            return;
        }

        var selectedKey = row.Key;
        var result = await _accountLinking.LinkManualAsync(
            [selectedKey, targetKey],
            new(AccountLinkAuthorKind.Streamer, ActorDisplayName: "streamer"));

        if (!result.Succeeded)
        {
            _dialogService.Warning("Привязка аккаунтов", DescribeLinkError(result.Status));
            return;
        }

        await TrySaveAfterLinkChangeAsync();
        Reload();
        TrySelect(selectedKey);
    }

    [RelayCommand]
    private async Task UnlinkAccountAsync(UserLinkedAccountRowViewModel? account)
    {
        if (account is null || _accountLinking is null || SelectedRow is not { } row || !account.CanUnlink)
        {
            return;
        }

        if (_dialogService is not null
            && !_dialogService.Confirm(
                "Отвязка аккаунта",
                $"Отвязать аккаунт {account.PlatformLabel} ({account.Name})? Накопленные счётчики сохранятся на своих каналах."))
        {
            return;
        }

        var selectedKey = row.Key;
        var result = await _accountLinking.UnlinkManualAsync(
            account.Key,
            new(AccountLinkAuthorKind.Streamer, ActorDisplayName: "streamer"));

        if (!result.Succeeded)
        {
            _dialogService?.Warning("Отвязка аккаунта", DescribeLinkError(result.Status));
            return;
        }

        await TrySaveAfterLinkChangeAsync();
        Reload();
        TrySelect(selectedKey);
    }

    public void Dispose()
    {
        _isDisposed = true;

        if (_channelCatalog is not null)
        {
            _channelCatalog.Changed -= OnChannelCatalogChanged;
        }

        foreach (var sub in _subscriptions)
        {
            sub.Dispose();
        }

        _subscriptions.Clear();
    }

    private async Task HydrateAfterPreloadAsync()
    {
        if (_preload is null)
        {
            return;
        }

        await _preload.EnsureLoadedAsync().ConfigureAwait(false);

        if (_isDisposed)
        {
            return;
        }

        if (_uiDispatcher is not null)
        {
            await _uiDispatcher.InvokeAsync(CompleteDeferredHydration);
        }
        else
        {
            CompleteDeferredHydration();
        }
    }

    private void CompleteDeferredHydration()
    {
        if (_isDisposed || _isHydrated)
        {
            return;
        }

        Reload();
    }

    private static void LogHydrationFailure(Exception exception)
    {
        Log.ForContext<UserStatisticsPageViewModel>()
            .Warning(exception, "Не удалось обновить страницу «Пользователи» после загрузки статистики");
    }

    private void Reload()
    {
        RefreshSliceOptions();

        var activeSlice = SelectedSliceOption?.Slice ?? StatisticsSlice.All();
        var aggregates = _userStatistics.QuerySlice(activeSlice);
        _allChannelsCount = activeSlice.Kind == StatisticsSliceKind.AllChannels
            ? aggregates.Count
            : _userStatistics.QuerySlice(StatisticsSlice.All()).Count;

        _allRows.Clear();

        var pointTerm = _userRankService.PointTerm;
        var ranks = _settingsManager.Current.Ranks.Ranks.ToArray();
        var trackedTwitchUserIds = new List<string>();

        foreach (var aggregate in aggregates)
        {
            var rank = _userRankService.GetRank(aggregate.Points);
            var standing = UserRankStanding.Create(aggregate.Points, _userRankService.GetRankDisplay(aggregate.Points), rank, ranks);

            var twitchAccount = aggregate.Accounts.FirstOrDefault(static a => a.Key.Platform == StreamingPlatform.Twitch);
            var avatar = twitchAccount is not null
                ? _avatars.For(twitchAccount.Key.UserId, twitchAccount.Name)
                : new UserAvatarViewModel(string.Empty, aggregate.DisplayName);

            if (twitchAccount is not null)
            {
                trackedTwitchUserIds.Add(twitchAccount.Key.UserId);
            }

            _allRows.Add(new UserStatisticsRowViewModel(aggregate, standing, pointTerm, _channelCatalog)
            {
                Avatar = avatar,
            });
        }

        _avatars.Track(trackedTwitchUserIds);
        ApplySliceSummaryAndRebuild();
        RebuildAdjustmentTargets(SelectedRow);

        if (_preload is null || _preload.IsCompleted)
        {
            _isHydrated = true;
        }
    }

    private void OnChannelCatalogChanged(object? sender, EventArgs e)
    {
        if (_isDisposed)
        {
            return;
        }

        if (_uiDispatcher is not null)
        {
            _ = UiAsync.RunGuardedAsync(async () =>
            {
                await _uiDispatcher.InvokeAsync(() =>
                {
                    if (!_isDisposed)
                    {
                        Reload();
                    }
                });
            }, LogHydrationFailure);
            return;
        }

        Reload();
    }

    private void RefreshSliceOptions()
    {
        var previousId = SelectedSliceOption?.Id ?? UserStatisticsSliceOption.All.Id;
        var nextOptions = new List<UserStatisticsSliceOption>
        {
            UserStatisticsSliceOption.All,
        };

        var knownChannels = UserStatisticsChannelCatalog.BuildChannels(
            _userStatistics,
            _channelCatalog,
            includeCatalogChannels: true,
            out var catalogSettings);

        foreach (var channel in knownChannels)
        {
            nextOptions.Add(new(
                $"channel:{channel.Key.Platform}:{channel.Key.ChannelId}",
                channel.FullLabel,
                StatisticsSlice.ForChannel(channel.Key)));
        }

        if (catalogSettings is not null)
        {
            foreach (var group in catalogSettings.Groups.OrderBy(static g => g.Name, StringComparer.OrdinalIgnoreCase))
            {
                if (string.IsNullOrWhiteSpace(group.Id) || string.IsNullOrWhiteSpace(group.Name))
                {
                    continue;
                }

                nextOptions.Add(new(
                    $"group:{group.Id}",
                    $"Группа: {group.Name}",
                    StatisticsSlice.ForGroup(group.Id, group.Name, ChannelCatalogStore.DeduplicateMembers(group.Channels))));
            }
        }

        foreach (var platformOption in PlatformFilterOption.AllOptions)
        {
            if (platformOption.Platform is not { } platform)
            {
                continue;
            }

            nextOptions.Add(new(
                $"platform:{platform}",
                $"Площадка: {platformOption.Label}",
                StatisticsSlice.ForPlatform(platform)));
        }

        foreach (var platform in _userStatistics.GetUnattributedPlatforms())
        {
            nextOptions.Add(new(
                $"unattributed:{platform}",
                $"{PlatformVisuals.GetDisplayName(platform)} · старые данные без канала",
                StatisticsSlice.Unattributed(platform)));
        }

        var wasSuppressed = _suppressFilterRebuild;
        _suppressFilterRebuild = true;
        try
        {
            _sliceOptions.SyncTo(nextOptions);
            var matched = _sliceOptions.FirstOrDefault(o => string.Equals(o.Id, previousId, StringComparison.Ordinal))
                ?? _sliceOptions[0];
            SelectedSliceOption = matched;
            SelectedPlatformFilter = matched.Slice.Kind == StatisticsSliceKind.Platform && matched.Slice.Platform is { } selectedPlatform
                ? PlatformFilterOption.AllOptions.FirstOrDefault(o => o.Platform == selectedPlatform) ?? PlatformFilterOption.All
                : PlatformFilterOption.All;
        }
        finally
        {
            _suppressFilterRebuild = wasSuppressed;
        }
    }

    private void RebuildAdjustmentTargets(UserStatisticsRowViewModel? row)
    {
        var previousTarget = SelectedAdjustmentTarget;
        var nextTargets = new List<AdjustmentTargetOption>();
        var candidates = new List<(AdjustmentTargetOption Option, bool IsAttributedChannel, DateTime BreakdownLastSeen, DateTime AccountLastSeen)>();

        if (row is not null)
        {
            foreach (var breakdown in row.ChannelBreakdowns)
            {
                var targetPlatform = breakdown.Target.Platform;
                var matchingAccounts = row.Aggregate.Accounts
                    .Where(a => a.Key.Platform == targetPlatform)
                    .ToList();

                if (matchingAccounts.Count == 0)
                {
                    continue;
                }

                var configuredChannel = (_platformRegistry is not null
                    ? _platformRegistry.GetConfiguredChannel(targetPlatform)
                    : _channelProvider.Channel) ?? string.Empty;

                var channel = breakdown.Target.AsChannelKey?.ChannelId ?? configuredChannel;

                foreach (var account in matchingAccounts)
                {
                    var label = matchingAccounts.Count > 1
                        ? $"{breakdown.Label} · {account.Name}"
                        : breakdown.Label;
                    var option = new AdjustmentTargetOption(breakdown.Target, account.Key, account.Name, channel, label);
                    nextTargets.Add(option);
                    candidates.Add((
                        option,
                        breakdown.Target.Kind == StatisticsCounterTargetKind.Channel,
                        breakdown.LastSeen,
                        account.LastSeen));
                }
            }
        }

        _adjustmentTargetOptions.SyncTo(nextTargets);
        OnPropertyChanged(nameof(HasMultipleAdjustmentTargets));

        if (nextTargets.Count == 0)
        {
            SelectedAdjustmentTarget = null;
            return;
        }

        SelectedAdjustmentTarget = nextTargets.FirstOrDefault(t =>
                previousTarget is not null
                && t.Target == previousTarget.Target
                && t.UserKey == previousTarget.UserKey)
            ?? nextTargets.FirstOrDefault(t =>
                SelectedSliceOption?.Slice.Kind == StatisticsSliceKind.Channel
                && SelectedSliceOption.Slice.Channel is { } sliceChannel
                && t.Target.AsChannelKey == sliceChannel)
            ?? candidates
                .OrderByDescending(static c => c.IsAttributedChannel)
                .ThenByDescending(static c => c.BreakdownLastSeen)
                .ThenByDescending(static c => c.AccountLastSeen)
                .Select(static c => c.Option)
                .FirstOrDefault()
            ?? nextTargets[0];
    }

    private async Task TrySaveAfterLinkChangeAsync()
    {
        if (_statisticsAutoSaver is null)
        {
            return;
        }

        try
        {
            await _statisticsAutoSaver.SaveNowAsync();
        }
        catch (Exception ex)
        {
            _dialogService?.Error("Ошибка сохранения", $"Не удалось сохранить статистику: {ex.Message}");
        }
    }

    private static string DescribeLinkError(AccountLinkOperationStatus status)
    {
        return status switch
        {
            AccountLinkOperationStatus.SameAccountRejected => "Нельзя привязать аккаунт к самому себе.",
            AccountLinkOperationStatus.AlreadyLinkedSameGroup => "Эти аккаунты уже связаны в одной группе.",
            AccountLinkOperationStatus.NotLinked => "У этого зрителя нет активной группы связей.",
            AccountLinkOperationStatus.TargetInAnotherGroup => "Один из выбранных аккаунтов уже входит в другую группу.",
            _ => "Не удалось обновить связь аккаунтов.",
        };
    }
}

public sealed record UserStatisticsSliceOption(
    string Id,
    string Label,
    StatisticsSlice Slice)
{
    public static UserStatisticsSliceOption All { get; } = new("all", "Все каналы", StatisticsSlice.All());
}

public sealed record AdjustmentTargetOption(
    StatisticsCounterTarget Target,
    PlatformUserKey UserKey,
    string UserName,
    string Channel,
    string Label);
