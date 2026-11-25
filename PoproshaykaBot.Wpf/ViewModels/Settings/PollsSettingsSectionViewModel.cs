using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PoproshaykaBot.Core.Settings.Stores;

namespace PoproshaykaBot.Wpf.ViewModels.Settings;

public sealed partial class PollsSettingsSectionViewModel : ObservableObject
{
    public const string NotWrittenNotice = "Настройки опросов применены и работают до перезапуска. В файл они не записаны: туда только что перенесены данные предыдущей версии. Перезапустите приложение и сохраните их ещё раз.";

    private readonly PollsStore _store;
    private readonly TimeProvider _timeProvider;
    private bool _isLoading;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SaveCommand))]
    private bool _hasChanges;

    [ObservableProperty]
    private string? _notice;

    [ObservableProperty]
    private double _historyMaxItems;

    [ObservableProperty]
    private bool _killSwitchToday;

    public PollsSettingsSectionViewModel(PollsStore store, TimeProvider timeProvider, BotMessagingNavigation? messaging = null)
    {
        _store = store;
        _timeProvider = timeProvider;
        MessageLinks = new(
            ("Начало опроса", () => messaging?.OpenRule("poll-start")),
            ("Ход опроса", () => messaging?.OpenRule("poll-progress")),
            ("Результат опроса", () => messaging?.OpenRule("poll-end")),
            ("Досрочное завершение", () => messaging?.OpenRule("poll-terminated")),
            ("Архивирование", () => messaging?.OpenRule("poll-archived")));
        LoadFromStore();
    }

    public string TabTitle => "Опросы";
    public BotMessagingLinksViewModel MessageLinks { get; }

    public bool SaveChanges()
    {
        var written = _store.Mutate(draft =>
        {
            draft.HistoryMaxItems = (int)HistoryMaxItems;
            draft.AutoTriggerKillSwitchDateUtc = KillSwitchToday
                ? _timeProvider.GetUtcNow().UtcDateTime.Date
                : null;
        });
        HasChanges = false;
        Notice = written ? null : NotWrittenNotice;

        return written;
    }

    [RelayCommand(CanExecute = nameof(HasChanges))]
    private void Save()
    {
        SaveChanges();
    }

    [RelayCommand]
    private void Revert()
    {
        LoadFromStore();
    }

    [RelayCommand]
    private void IncreaseHistoryMax()
    {
        HistoryMaxItems = Math.Min(HistoryMaxItems + 1, 10000);
    }

    [RelayCommand]
    private void DecreaseHistoryMax()
    {
        HistoryMaxItems = Math.Max(HistoryMaxItems - 1, 10);
    }

    partial void OnHistoryMaxItemsChanged(double value) => MarkChanged();
    partial void OnKillSwitchTodayChanged(bool value) => MarkChanged();

    private void MarkChanged()
    {
        if (!_isLoading)
        {
            HasChanges = true;
        }
    }

    private void LoadFromStore()
    {
        _isLoading = true;

        try
        {
            var settings = _store.Load();
            HistoryMaxItems = Math.Clamp(settings.HistoryMaxItems, 10, 10000);

            var todayUtc = _timeProvider.GetUtcNow().UtcDateTime.Date;
            KillSwitchToday = settings.AutoTriggerKillSwitchDateUtc?.Date == todayUtc;
        }
        finally
        {
            _isLoading = false;
        }

        HasChanges = false;
    }
}
