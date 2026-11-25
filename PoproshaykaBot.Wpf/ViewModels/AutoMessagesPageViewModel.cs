using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using KeepShell.Services;
using Microsoft.Extensions.Logging;
using PoproshaykaBot.Core.Broadcast;
using PoproshaykaBot.Core.Chat;
using PoproshaykaBot.Core.Infrastructure.Events;
using PoproshaykaBot.Core.Infrastructure.Events.Broadcasting;
using PoproshaykaBot.Core.Infrastructure.Events.Lifecycle;
using PoproshaykaBot.Core.Platforms;
using PoproshaykaBot.Core.Settings;
using PoproshaykaBot.Core.Settings.Stores;
using PoproshaykaBot.Wpf.Infrastructure;
using PoproshaykaBot.Wpf.Infrastructure.Platforms;

namespace PoproshaykaBot.Wpf.ViewModels;

public sealed record BotMessageOption(string Key, string Group, string Title);

public sealed partial class AutoMessagesPageViewModel : ObservableObject, IUnsavedChangesPage, IDisposable
{
    private readonly Dictionary<string, BotMessageRule> _rules;
    private readonly IUnsavedChangesPrompt _prompt;
    private readonly IChatPlatformRegistry _platforms;
    private readonly IBroadcastPlatformTargetsProvider _targets;
    private readonly BroadcastProfilesStore _profiles;
    private readonly BroadcastScheduler _scheduler;
    private readonly IBroadcastState _broadcastState;
    private readonly IDialogService _dialogs;
    private readonly BotMessagingNavigation _navigation;
    private readonly IEventBus _events;
    private readonly ILogger<AutoMessagesPageViewModel> _logger;
    private readonly IDisposable[] _subscriptions;
    private BotMessageValues _saved = new(false, string.Empty);
    private BotMessageOption _activeOption;
    private bool _restoringSelection;
    private bool _loading;
    private Guid? _effectiveProfileId;

    [ObservableProperty]
    private BotMessageOption _selectedOption;

    [ObservableProperty]
    private bool _enabled;

    [ObservableProperty]
    private string _template = string.Empty;

    [ObservableProperty]
    private string _interval = string.Empty;

    [ObservableProperty]
    private string? _notice;

    [ObservableProperty]
    private string _audienceText = string.Empty;

    [ObservableProperty]
    private string? _availabilityText;

    [ObservableProperty]
    private string _broadcastStatus = string.Empty;

    [ObservableProperty]
    private string? _lastDispatch;

    public AutoMessagesPageViewModel(
        SettingsManager settings, PollsStore polls, IUnsavedChangesPrompt prompt, IChatPlatformRegistry platforms,
        IBroadcastPlatformTargetsProvider targets, BroadcastProfilesStore profiles, BroadcastScheduler scheduler,
        BotMessagingNavigation navigation, IEventBus events, ILogger<AutoMessagesPageViewModel> logger,
        IBroadcastState broadcastState, IDialogService dialogs)
    {
        _rules = BotMessageRule.Create(settings, polls).ToDictionary(rule => rule.Key, StringComparer.Ordinal);
        _prompt = prompt;
        _platforms = platforms;
        _targets = targets;
        _profiles = profiles;
        _scheduler = scheduler;
        _broadcastState = broadcastState;
        _dialogs = dialogs;
        _navigation = navigation;
        _events = events;
        _logger = logger;
        Options = [.. _rules.Values.Select(rule => new BotMessageOption(rule.Key, rule.Group, rule.Title))];
        _activeOption = _selectedOption = Options[0];

        _subscriptions =
        [
            events.SubscribeOnUi<PlatformConnectionChanged>(_ => RefreshRuntime()),
            events.SubscribeOnUi<BroadcastSchedulerStateChanged>(_ => RefreshRuntime()),
            events.SubscribeOnUi<BroadcastProfileApplied>(_ => RefreshRuntime()),
            events.SubscribeOnUi<BroadcastProfilesChanged>(_ => RefreshRuntime()),
        ];
        Load();
    }

    public IReadOnlyList<BotMessageOption> Options { get; }

    private BotMessageRule Rule => _rules[_activeOption.Key];

    public string Trigger => Rule.Trigger;
    public string ToggleLabel => Rule.ToggleLabel;
    public bool HasInterval => Rule.IntervalLabel is not null;
    public string IntervalLabel => Rule.IntervalLabel ?? string.Empty;
    public bool IsBroadcast => Rule.Audience == BotMessageAudience.ProfileTargets;
    public bool IsFarewell => _activeOption.Key == "farewell";
    public bool CanRestoreDefaults => Rule.DefaultEnabled.HasValue;
    public string ApplyHint => IsBroadcast || HasInterval
        ? Rule.BlankHint : $"{Rule.BlankHint} Правка действует со следующего события.";
    public string? IntervalHint => _activeOption.Key switch
    {
        "broadcast" => "Новый интервал действует со следующего запуска таймера. Текст применяется при следующей отправке.",
        "poll-progress" => "От 15 до 3600 секунд. Изменение действует при следующем обновлении голосов.",
        _ => null,
    };
    public string? Placeholders
    {
        get
        {
            var examples = Rule.ExampleValues;
            return examples.Count > 0 ? string.Join(", ", examples.Keys.Select(key => $"{{{key}}}")) : null;
        }
    }
    public string Preview
    {
        get
        {
            var text = string.IsNullOrWhiteSpace(Template) && Template != _saved.Text
                ? Rule.DefaultForBlank ?? string.Empty
                : Template;
            var template = MessageTemplate.For(text);
            foreach (var (key, example) in Rule.ExampleValues)
            {
                template = template.With(key, example);
            }

            var result = template.Render();
            return string.IsNullOrWhiteSpace(result) ? "Публикация будет пропущена." : result;
        }
    }

    public bool HasUnsavedChanges => Enabled != _saved.Enabled || Template != _saved.Text
        || Interval != FormatInterval(_saved.Interval);

    public string UnsavedChangesSubject => $"Изменены параметры «{_activeOption.Title}».";

    public void OnEnter()
    {
        if (HasUnsavedChanges)
        {
            RefreshRuntime();
            return;
        }

        Load();
    }

    public bool TryOpen(string key)
    {
        var option = Options.FirstOrDefault(item => item.Key == key);
        if (option is null)
        {
            return false;
        }

        SelectedOption = option;
        return ReferenceEquals(_activeOption, option);
    }

    partial void OnSelectedOptionChanged(BotMessageOption value)
    {
        if (_restoringSelection || ReferenceEquals(_activeOption, value))
        {
            return;
        }

        if (value is null || !ConfirmDraft("переходом к другому сообщению"))
        {
            _restoringSelection = true;
            SelectedOption = _activeOption;
            _restoringSelection = false;
            return;
        }

        _activeOption = value;
        Load();
    }

    private bool ConfirmDraft(string action)
    {
        return !HasUnsavedChanges || _prompt.Ask(UnsavedChangesSubject, action) switch
        {
            UnsavedChangesDecision.Save => SaveDraft(),
            UnsavedChangesDecision.Discard => true,
            _ => false,
        };
    }

    private void Load()
    {
        _loading = true;
        _saved = Rule.Read();
        Enabled = _saved.Enabled;
        Template = _saved.Text;
        Interval = FormatInterval(_saved.Interval);
        Notice = null;
        _loading = false;

        OnPropertyChanged(nameof(Trigger));
        OnPropertyChanged(nameof(ToggleLabel));
        OnPropertyChanged(nameof(ApplyHint));
        OnPropertyChanged(nameof(CanRestoreDefaults));
        OnPropertyChanged(nameof(IntervalLabel));
        OnPropertyChanged(nameof(IntervalHint));
        OnPropertyChanged(nameof(HasInterval));
        OnPropertyChanged(nameof(IsBroadcast));
        OnPropertyChanged(nameof(IsFarewell));
        OnPropertyChanged(nameof(Placeholders));
        OnPropertyChanged(nameof(Preview));
        RefreshRuntime();
        NotifyDraft();
    }

    private void RefreshRuntime()
    {
        var configured = _platforms.All.Where(platform => platform.IsConnected).Select(platform => platform.Id).ToHashSet();
        switch (Rule.Audience)
        {
            case BotMessageAudience.ProfileTargets:
                var profiles = _profiles.Load();
                var profile = profiles.Profiles.FirstOrDefault(item => item.Id == profiles.LastAppliedProfileId);
                _effectiveProfileId = profile?.Id;
                var targets = _targets.GetTargetPlatforms();
                var source = profile is null ? "Профиль не применён" : $"Применён профиль «{profile.Name}»";
                AudienceText = $"{source}. Цели: {FormatPlatforms(targets)}.";
                AvailabilityText = FormatUnavailable(targets.Where(platform => !configured.Contains(platform)));
                break;
            case BotMessageAudience.Twitch:
                AudienceText = "Только чат Twitch.";
                AvailabilityText = FormatUnavailable(configured.Contains(StreamingPlatform.Twitch) ? [] : [StreamingPlatform.Twitch]);
                break;
            default:
                AudienceText = Rule.Audience == BotMessageAudience.EventChat
                    ? "Чат площадки, на которой произошло событие."
                    : "Все подключённые чаты.";
                AvailabilityText = configured.Count == 0 ? "Сейчас нет подключённых чатов." : $"Подключены: {FormatPlatforms(configured)}.";
                break;
        }

        BroadcastStatus = (_scheduler.IsActive, _saved.Enabled) switch
        {
            (true, _) => $"Таймер: {_scheduler.Interval.TotalMinutes:0} мин. Следующая отправка в {_scheduler.NextBroadcastTime:HH:mm:ss}.",
            (false, true) => "Таймер остановлен. Автоматический режим включён.",
            _ => "Таймер остановлен. Запустить его можно на плитке «Рассылка».",
        };
        LastDispatch = BroadcastDeliveryText.Format(_scheduler.GetLastDispatch(), _platforms);
        SendNowCommand.NotifyCanExecuteChanged();
    }

    private string FormatPlatforms(IEnumerable<StreamingPlatform> ids)
    {
        var names = ids.Order().Select(id => PlatformVisuals.GetDisplayName(id, _platforms));
        return string.Join(", ", names) is { Length: > 0 } result ? result : "нет";
    }

    private string? FormatUnavailable(IEnumerable<StreamingPlatform> ids)
    {
        var unavailable = ids.ToArray();
        return unavailable.Length > 0 ? $"Чат недоступен: {FormatPlatforms(unavailable)}." : null;
    }

    private static string FormatInterval(int? interval) => interval?.ToString(CultureInfo.InvariantCulture) ?? string.Empty;

    partial void OnEnabledChanged(bool value) => NotifyDraft();
    partial void OnTemplateChanged(string value)
    {
        OnPropertyChanged(nameof(Preview));
        NotifyDraft();
    }
    partial void OnIntervalChanged(string value) => NotifyDraft();

    private void NotifyDraft()
    {
        if (_loading)
        {
            return;
        }

        OnPropertyChanged(nameof(HasUnsavedChanges));
        SaveCommand.NotifyCanExecuteChanged();
        RevertCommand.NotifyCanExecuteChanged();
        SendNowCommand.NotifyCanExecuteChanged();
    }

    private bool CanSave() => HasUnsavedChanges;

    [RelayCommand(CanExecute = nameof(CanSave))]
    private void Save() => SaveDraft();

    [RelayCommand(CanExecute = nameof(CanSave))]
    private void Revert() => Load();

    [RelayCommand]
    private void RestoreDefaults()
    {
        if (Rule.DefaultEnabled is { } enabled && Rule.DefaultForBlank is { } text)
        {
            Enabled = enabled;
            Template = text;
        }
    }

    private bool SaveDraft()
    {
        if (!HasUnsavedChanges)
        {
            return true;
        }

        int? interval = null;
        if (HasInterval)
        {
            if (!int.TryParse(Interval, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value)
                || value < Rule.MinimumInterval || value > Rule.MaximumInterval)
            {
                Notice = Rule.MaximumInterval == int.MaxValue
                    ? "Введите целое число от 1."
                    : $"Введите целое число от {Rule.MinimumInterval} до {Rule.MaximumInterval}.";
                return false;
            }

            interval = value;
        }

        var enablingAutomaticBroadcast = IsBroadcast && Enabled && !_saved.Enabled;
        if (enablingAutomaticBroadcast && _scheduler.IsActive && !_broadcastState.IsAnyLive
            && !_dialogs.Confirm("Переключение режима", "При включении автоматического режима рассылка остановится до начала эфира. Продолжить?"))
        {
            return false;
        }

        try
        {
            if (!Rule.Save(_saved, new(Enabled, Template, interval)))
            {
                Notice = "Файл настроек обновляется извне. Изменения не применились. После импорта перезапустите приложение.";
                return false;
            }
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "Не удалось сохранить правило публикации {RuleKey}", Rule.Key);
            Notice = "Изменения не сохранились. Проверьте доступ к файлу настроек и повторите.";
            return false;
        }

        if (enablingAutomaticBroadcast)
        {
            ApplyAutomaticMode();
        }

        Load();
        Notice = "Сохранено.";
        if (IsBroadcast)
        {
            _ = _events.PublishAsync(new BroadcastScheduleSettingsChanged());
        }

        return true;
    }

    private void ApplyAutomaticMode()
    {
        if (!_broadcastState.IsAnyLive)
        {
            _scheduler.Stop();
        }
        else if (!_scheduler.IsActive && BroadcastChannelSelection.Resolve(_broadcastState, _platforms) is { } channel)
        {
            _scheduler.Start(channel);
        }
    }

    private bool CanSendNow() => IsBroadcast && !HasUnsavedChanges && _scheduler.IsActive;

    [RelayCommand(CanExecute = nameof(CanSendNow))]
    private Task SendNowAsync() => _scheduler.ManualSendAsync();

    [RelayCommand]
    private void OpenProfileTargets() => _navigation.OpenProfileTargets(_effectiveProfileId);

    [RelayCommand]
    private void OpenFarewellCommand() => _navigation.OpenCommand("пока");

    public Task<bool> TrySaveUnsavedChangesAsync() => Task.FromResult(SaveDraft());

    internal bool TrySaveDraft() => SaveDraft();

    public void DiscardUnsavedChanges() => Load();

    public void Dispose()
    {
        foreach (var subscription in _subscriptions)
        {
            subscription.Dispose();
        }
    }
}
