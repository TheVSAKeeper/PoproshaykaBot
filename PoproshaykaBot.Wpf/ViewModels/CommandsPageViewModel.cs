using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using PoproshaykaBot.Core.Chat;
using PoproshaykaBot.Core.Chat.Commands;
using PoproshaykaBot.Core.Platforms;
using PoproshaykaBot.Core.Platforms.Auth;
using PoproshaykaBot.Core.Settings;
using PoproshaykaBot.Core.Settings.Stores;
using PoproshaykaBot.Core.Statistics;
using PoproshaykaBot.Core.Twitch;
using PoproshaykaBot.Core.Twitch.Chat;
using PoproshaykaBot.Wpf.Bootstrap;
using PoproshaykaBot.Wpf.Infrastructure;
using PoproshaykaBot.Wpf.Infrastructure.Platforms;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows.Input;

namespace PoproshaykaBot.Wpf.ViewModels;

public sealed partial class CommandsPageViewModel : ObservableObject, IPageHeader, IPageRefresh, IUnsavedChangesPage
{
    private static readonly PlatformFilterOption GeneralScope = new(null, "Общие");
    private static readonly PlatformEnablementOption InheritEnablement = new("Как в общих", null);
    private static readonly PlatformEnablementOption EnableOnPlatform = new("Включена", true);
    private static readonly PlatformEnablementOption DisableOnPlatform = new("Выключена", false);

    private static string FormatWhisperReauthorizationNotice(string platformDisplayName) =>
        $"Пока у бота нет разрешения на личные сообщения, он отвечает в общем чате. Чтобы включить шёпот, откройте «Настройки → Площадки → {platformDisplayName}» и войдите заново в строке «Бот» нужного канала.";

    private const string NotWrittenNotice = "Параметры применены и работают до перезапуска. В файл они не записаны: туда только что перенесены данные предыдущей версии. Перезапустите приложение и сохраните параметры ещё раз.";

    private readonly CommandSettingsStore _settingsStore;
    private readonly SettingsManager _settingsManager;
    private readonly CommandUsageRepository _usageRepository;
    private readonly IUnsavedChangesPrompt _unsavedChangesPrompt;
    private readonly AccountsStore _accountsStore;
    private readonly TwitchChannelAuthorization? _authorization;
    private readonly IChatPlatformRegistry? _platformRegistry;
    private readonly ILogger<CommandsPageViewModel> _logger;
    private readonly IUiTimer? _usageTimer;
    private readonly List<CommandRowViewModel> _allRows = [];
    private readonly ObservableCollection<CommandRowViewModel> _rows = [];
    private readonly Dictionary<string, IChatCommand> _commands = new(StringComparer.OrdinalIgnoreCase);

    private bool _applying;
    private bool _restoringRow;
    private bool _rebuildingView;
    private CommandRowViewModel? _parametersRow;

    public event EventHandler? FarewellRuleRequested;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsPlatformScope))]
    private PlatformFilterOption _commandScope = GeneralScope;

    [ObservableProperty]
    private CommandRowViewModel? _selectedRow;

    [ObservableProperty]
    private string _filterText = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(EmptyActionCommand))]
    private bool _isFilterActive;

    [ObservableProperty]
    private CommandResponseTargetOption _defaultTargetOption = CommandResponseTargetOption.Both;

    [ObservableProperty]
    private string _summaryLine = string.Empty;

    [ObservableProperty]
    private bool _hasUsage;

    [ObservableProperty]
    private string? _notice;

    [ObservableProperty]
    private string? _whisperNotice;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasParameters))]
    private CommandParametersViewModel? _parameters;

    [ObservableProperty]
    private string? _parametersNotice;

    [ObservableProperty]
    private CommandNoticeTone _parametersNoticeTone = CommandNoticeTone.Info;

    public CommandsPageViewModel(
        ChatCommandProcessor processor,
        CommandSettingsStore settingsStore,
        SettingsManager settingsManager,
        CommandUsageRepository usageRepository,
        IUnsavedChangesPrompt unsavedChangesPrompt,
        AccountsStore accountsStore,
        ILogger<CommandsPageViewModel> logger,
        IChatPlatformRegistry? platformRegistry = null,
        TwitchChannelAuthorization? authorization = null,
        IUiDispatcher? uiDispatcher = null)
    {
        ArgumentNullException.ThrowIfNull(processor);

        _accountsStore = accountsStore;
        _authorization = authorization;
        _settingsStore = settingsStore;
        _settingsManager = settingsManager;
        _usageRepository = usageRepository;
        _unsavedChangesPrompt = unsavedChangesPrompt;
        _platformRegistry = platformRegistry;
        _logger = logger;
        _usageTimer = uiDispatcher?.CreateTimer(TimeSpan.FromSeconds(5), RefreshUsage);

        CommandScopes = PlatformFilterOption.ForRegistry(platformRegistry, GeneralScope);

        var byName = StringComparer.Create(UiCulture.Russian, ignoreCase: true);
        var capabilities = CapabilitiesFor(ProbePlatform);

        foreach (var command in processor.GetAllCommands().OrderBy(command => command.Canonical, byName))
        {
            var row = new CommandRowViewModel(command, processor.Prefix, ProbeCodeLevel(command, ProbePlatform, capabilities));
            row.EnabledChanged += OnRowEnabledChanged;
            row.TargetChanged += OnRowTargetChanged;
            row.AccessChanged += OnRowAccessChanged;

            _allRows.Add(row);
            _commands[command.Canonical] = command;
        }

        Reload();
    }

    public IReadOnlyList<PlatformFilterOption> CommandScopes { get; }

    public bool IsPlatformScope => CommandScope.Platform is not null;

    public IReadOnlyList<PlatformEnablementOption> PlatformEnablementOptions { get; } =
        [InheritEnablement, EnableOnPlatform, DisableOnPlatform];

    public PlatformEnablementOption SelectedPlatformEnablement
    {
        get => SelectedRow?.PlatformEnabled switch
        {
            true => EnableOnPlatform,
            false => DisableOnPlatform,
            null => InheritEnablement,
        };
        set
        {
            if (value is null || CommandScope.Platform is not { } platform || SelectedRow is not { } row
                || value.Enabled == row.PlatformEnabled)
            {
                return;
            }

            if (TryMutate(settings =>
                {
                    GetOrAddOverride(settings, row.Canonical).SetPlatformEnabled(platform, value.Enabled);
                    DropRedundantOverride(settings, row.Canonical);
                }))
            {
                row.ApplyPlatformEnablement(value.Enabled);
                UpdateSummaries();

                if (_logger.IsEnabled(LogLevel.Information))
                {
                    _logger.CommandEnabledChanged(row.Invocation, $"{CommandScope.Label}: {value.Title}");
                }
            }

            NotifyPlatformEnablementChanged();
        }
    }

    public bool CanSetPlatformEnablement => SelectedRow is { IsClosedByCode: false };

    public string? PlatformEnablementStatus => SelectedRow is not { } row ? null
        : row.IsClosedByCode ? row.AccessHint
        : row.IsEnabledInScope ? "Команда включена на площадке" : "Команда выключена на площадке";

    public string? ParametersHeading => IsPlatformScope && Parameters is { } parameters
        ? $"{parameters.Heading} · общие для площадок" : Parameters?.Heading;

    public string? ResponseSource => SelectedRow is { } row ? _commands.GetValueOrDefault(row.Canonical) switch
    {
        HelloCommand => "Команда отвечает «Привет, имя!». Она работает независимо от автоприветствия.",
        ByeCommand => "Команда использует общий шаблон прощания. Если прощание выключено, ответа не будет.",
        TrumpCommand => "Ответ зависит от списка пользователей и данных о покупке.",
        _ => null,
    } : null;

    public bool UsesFarewellRule => SelectedRow is { } row && _commands.GetValueOrDefault(row.Canonical) is ByeCommand;

    public string? PlatformSummary
    {
        get
        {
            if (SelectedRow is not { } row || _platformRegistry is null)
            {
                return null;
            }

            var settings = _settingsStore.Load();
            var command = _commands[row.Canonical];
            return string.Join(" · ", _platformRegistry.All.Select(platform =>
            {
                var enabled = settings.IsEnabled(row.Canonical, platform.Id)
                    && ProbeCodeLevel(command, platform.Id, platform.Capabilities) is not null;
                return $"{platform.DisplayName}: {(enabled ? "вкл." : "выкл.")}";
            }));
        }
    }

    [RelayCommand]
    private void OpenFarewellRule() => FarewellRuleRequested?.Invoke(this, EventArgs.Empty);

    private StreamingPlatform ProbePlatform =>
        CommandScope.Platform ?? (_platformRegistry?.All is { Count: > 0 } all ? all[0].Id : StreamingPlatform.Twitch);

    public string PageTitle => "Команды";

    public string? PageDescription => "Права, цель ответа, параметры и статистика вызовов";

    public ObservableCollection<CommandRowViewModel> Rows => _rows;

    public IReadOnlyList<CommandResponseTargetOption> CommandTargetOptions => CommandResponseTargetOption.ForCommand;

    public IReadOnlyList<CommandResponseTargetOption> DefaultTargetOptions => CommandResponseTargetOption.ForDefault;

    public IReadOnlyList<CommandAccessOption> AccessOptions => CommandAccessOption.All;

    public bool HasCommands => _rows.Count > 0;

    public bool HasSelectedRow => SelectedRow is not null;

    public bool HasParameters => Parameters is not null;

    public string EmptyHeading => IsFilterActive ? "Ничего не найдено" : "Команд нет";

    public string EmptyDescription => IsFilterActive
        ? "Ни одна команда не подходит под поиск. Очистите его, чтобы увидеть весь список."
        : "Ни одной команды чата не зарегистрировано.";

    public string? EmptyActionText => "Сбросить поиск";

    public IRelayCommand? EmptyActionCommand => IsFilterActive ? ClearFilterCommand : null;

    public string UsageHint => HasUsage
        ? string.Empty
        : "Счётчики вызовов читаются при подключении бота – до него они пустые";

    public bool HasUnsavedChanges => Parameters is { IsDirty: true };

    public string UnsavedChangesSubject => _parametersRow is { } row
        ? $"Параметры команды {row.Invocation} изменены, но не сохранены."
        : "Параметры команды изменены, но не сохранены.";

    public void OnEnter()
    {
        if (HasUnsavedChanges)
        {
            ReloadRows();
            return;
        }

        Reload();
    }

    public Task<bool> TrySaveUnsavedChangesAsync()
    {
        return Task.FromResult(TrySaveParameters());
    }

    internal bool TrySaveDraft() => TrySaveParameters();

    public void DiscardUnsavedChanges()
    {
        ReloadParameters();
    }

    public void SetUsagePollingActive(bool isActive)
    {
        if (isActive)
        {
            RefreshUsage();
            _usageTimer?.Start();
        }
        else
        {
            _usageTimer?.Stop();
        }
    }

    public void RefreshUsage()
    {
        var usage = _usageRepository
            .GetSnapshot()
            .ToDictionary(record => record.Canonical, StringComparer.OrdinalIgnoreCase);

        foreach (var row in _allRows)
        {
            row.ApplyUsage(usage.GetValueOrDefault(row.Canonical));
            row.RefreshLastUsedText();
        }

        HasUsage = usage.Count > 0;
        OnPropertyChanged(nameof(UsageHint));

        UpdateSummaries();
    }

    public bool TrySelect(string canonical)
    {
        var row = _allRows.FirstOrDefault(item => string.Equals(item.Canonical, canonical, StringComparison.OrdinalIgnoreCase)
            || _commands[item.Canonical].Aliases.Contains(canonical, StringComparer.OrdinalIgnoreCase));

        if (row is null)
        {
            return false;
        }

        if (!_rows.Contains(row))
        {
            FilterText = string.Empty;
        }

        SelectedRow = row;
        return ReferenceEquals(SelectedRow, row);
    }

    private static CommandContext CreateProbe(StreamingPlatform platform, bool isModerator = false, bool isBroadcaster = false)
    {
        return new()
        {
            Platform = platform,
            Username = isBroadcaster ? "broadcaster" : isModerator ? "moderator" : "viewer",
            DisplayName = isBroadcaster ? "broadcaster" : isModerator ? "moderator" : "viewer",
            IsModerator = isModerator,
            IsBroadcaster = isBroadcaster,
        };
    }

    private PlatformCapabilities CapabilitiesFor(StreamingPlatform platform)
    {
        if (_platformRegistry?.Find(platform) is { } found)
        {
            return found.Capabilities;
        }

        return platform switch
        {
            StreamingPlatform.Twitch => PlatformCapabilities.Reply
                | PlatformCapabilities.Whisper
                | PlatformCapabilities.NativePolls
                | PlatformCapabilities.ChannelMetadataEdit
                | PlatformCapabilities.EmotePositions,
            StreamingPlatform.YouTube => PlatformCapabilities.ServerChatHistory,
            _ => PlatformCapabilities.None,
        };
    }

    private static CommandAccessLevel? ProbeCodeLevel(
        IChatCommand command,
        StreamingPlatform platform,
        PlatformCapabilities capabilities)
    {
        if ((capabilities & command.RequiredCapabilities) != command.RequiredCapabilities)
        {
            return null;
        }

        if (command.CanExecute(CreateProbe(platform)))
        {
            return CommandAccessLevel.Everyone;
        }

        if (command.CanExecute(CreateProbe(platform, isModerator: true)))
        {
            return CommandAccessLevel.Moderators;
        }

        return command.CanExecute(CreateProbe(platform, isBroadcaster: true))
            ? CommandAccessLevel.Broadcaster
            : null;
    }

    partial void OnCommandScopeChanged(PlatformFilterOption value)
    {
        var platform = value.Platform ?? ProbePlatform;
        var capabilities = CapabilitiesFor(platform);
        var settings = _settingsStore.Load();

        var probePlatformDisplayName = value.Platform is { } scopedPlatform
            ? PlatformVisuals.GetDisplayName(scopedPlatform, _platformRegistry)
            : null;

        foreach (var row in _allRows)
        {
            row.ApplyPlatformEnablement(value.Platform is { } id ? settings.ReadPlatformEnabled(row.Canonical, id) : null);
            if (_commands.TryGetValue(row.Canonical, out var command))
            {
                row.UpdateCodeAvailability(ProbeCodeLevel(command, platform, capabilities), value.Platform, probePlatformDisplayName);
            }
        }

        RefreshWhisperNotice();
        UpdateSummaries();
        OnPropertyChanged(nameof(ParametersHeading));
        NotifyPlatformEnablementChanged();
    }

    private void NotifyPlatformEnablementChanged()
    {
        OnPropertyChanged(nameof(SelectedPlatformEnablement));
        OnPropertyChanged(nameof(PlatformEnablementStatus));
        OnPropertyChanged(nameof(CanSetPlatformEnablement));
    }

    private static CommandOverride GetOrAddOverride(CommandSettings settings, string canonical)
    {
        if (settings.Commands.TryGetValue(canonical, out var existing) && existing is not null)
        {
            return existing;
        }

        var created = new CommandOverride();
        settings.Commands[canonical] = created;

        return created;
    }

    private static void DropRedundantOverride(CommandSettings settings, string canonical)
    {
        if (settings.Commands.TryGetValue(canonical, out var existing)
            && existing is { Enabled: true, ResponseTarget: null, Access: null }
            && (existing.EnabledByPlatform is null or { Count: 0 }))
        {
            settings.Commands.Remove(canonical);
        }
    }

    private void Reload()
    {
        ReloadRows();
        ReloadParameters();
    }

    private void ReloadRows()
    {
        var settings = _settingsStore.Load();

        _applying = true;

        try
        {
            DefaultTargetOption = CommandResponseTargetOption.ForDefaultTarget(settings.DefaultResponseTarget);

            foreach (var row in _allRows)
            {
                row.ApplySettings(settings, CommandScope.Platform);
            }
        }
        finally
        {
            _applying = false;
        }

        RefreshUsage();
        RefreshWhisperNotice();

        RebuildView();
    }

    partial void OnFilterTextChanged(string value)
    {
        RebuildView();
    }

    partial void OnSelectedRowChanged(CommandRowViewModel? value)
    {
        if (_restoringRow || _rebuildingView)
        {
            return;
        }

        OnPropertyChanged(nameof(HasSelectedRow));
        NotifyPlatformEnablementChanged();

        if (!ConfirmParametersDraft("переключением на другую команду"))
        {
            RestoreParametersRow();
            return;
        }

        ReloadParameters();
    }

    partial void OnParametersChanging(CommandParametersViewModel? value)
    {
        if (Parameters is not null)
        {
            Parameters.PropertyChanged -= OnParametersPropertyChanged;
        }
    }

    partial void OnParametersChanged(CommandParametersViewModel? value)
    {
        if (value is not null)
        {
            value.PropertyChanged += OnParametersPropertyChanged;
        }

        OnPropertyChanged(nameof(HasUnsavedChanges));
        OnPropertyChanged(nameof(ParametersHeading));
        SaveParametersCommand.NotifyCanExecuteChanged();
    }

    partial void OnDefaultTargetOptionChanged(CommandResponseTargetOption value)
    {
        if (_applying || value.Target is not { } target)
        {
            return;
        }

        if (!TryMutate(settings => settings.DefaultResponseTarget = target))
        {
            return;
        }

        _logger.CommandDefaultTargetChanged(value.Title);

        RefreshEffectiveTargets();
    }

    private void OnParametersPropertyChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (string.Equals(args.PropertyName, nameof(CommandParametersViewModel.IsDirty), StringComparison.Ordinal))
        {
            OnPropertyChanged(nameof(HasUnsavedChanges));
            SaveParametersCommand.NotifyCanExecuteChanged();
        }
    }

    private void ReloadParameters()
    {
        OnPropertyChanged(nameof(ResponseSource));
        OnPropertyChanged(nameof(UsesFarewellRule));
        OnPropertyChanged(nameof(PlatformSummary));
        ParametersNotice = null;
        ParametersNoticeTone = CommandNoticeTone.Info;
        _parametersRow = SelectedRow;

        if (SelectedRow is null || !_commands.TryGetValue(SelectedRow.Canonical, out var command))
        {
            Parameters = null;
            return;
        }

        var parameters = CommandParametersViewModel.TryCreate(command);

        parameters?.Load(_settingsManager.Current);

        Parameters = parameters;
    }

    private bool ConfirmParametersDraft(string action)
    {
        if (!HasUnsavedChanges)
        {
            return true;
        }

        return _unsavedChangesPrompt.Ask(UnsavedChangesSubject, action) switch
        {
            UnsavedChangesDecision.Save => TrySaveParameters(),
            UnsavedChangesDecision.Discard => true,
            _ => false,
        };
    }

    private void RestoreParametersRow()
    {
        _restoringRow = true;

        try
        {
            SelectedRow = _parametersRow;
        }
        finally
        {
            _restoringRow = false;
        }

        OnPropertyChanged(nameof(HasSelectedRow));
        NotifyPlatformEnablementChanged();
    }

    private void OnRowEnabledChanged(object? sender, EventArgs args)
    {
        if (sender is not CommandRowViewModel row)
        {
            return;
        }

        var enabled = row.IsEnabled;

        if (!TryMutate(settings =>
            {
                GetOrAddOverride(settings, row.Canonical).Enabled = enabled;
                DropRedundantOverride(settings, row.Canonical);
            }))
        {
            return;
        }

        _logger.CommandEnabledChanged(row.Invocation, enabled ? "включена" : "выключена");

        UpdateSummaries();
        NotifyPlatformEnablementChanged();
    }

    private void OnRowTargetChanged(object? sender, EventArgs args)
    {
        if (sender is not CommandRowViewModel row)
        {
            return;
        }

        var target = row.TargetOption.Target;

        if (!TryMutate(settings =>
            {
                GetOrAddOverride(settings, row.Canonical).ResponseTarget = target;
                DropRedundantOverride(settings, row.Canonical);
            }))
        {
            return;
        }

        _logger.CommandTargetChanged(row.Invocation, row.TargetOption.Title);

        RefreshEffectiveTargets();
    }

    private void OnRowAccessChanged(object? sender, EventArgs args)
    {
        if (sender is not CommandRowViewModel row)
        {
            return;
        }

        var access = row.AccessOption.Level;

        if (!TryMutate(settings =>
            {
                GetOrAddOverride(settings, row.Canonical).Access = access;
                DropRedundantOverride(settings, row.Canonical);
            }))
        {
            return;
        }

        _logger.CommandAccessChanged(row.Invocation, row.AccessOption.Title);

        UpdateSummaries();
    }

    private bool TryMutate(Action<CommandSettings> mutator)
    {
        try
        {
            _settingsStore.Mutate(mutator);
            Notice = null;

            return true;
        }
        catch (Exception exception)
        {
            _logger.CommandSettingsSaveFailed(exception);
            Notice = "Настройки команд не сохранились – правка отменена. Подробности в журнале.";
            ReloadRows();

            return false;
        }
    }

    private void RefreshEffectiveTargets()
    {
        var settings = _settingsStore.Load();

        _applying = true;

        try
        {
            foreach (var row in _allRows)
            {
                row.ApplySettings(settings, CommandScope.Platform);
            }
        }
        finally
        {
            _applying = false;
        }

        RefreshWhisperNotice();
    }

    private void RefreshWhisperNotice()
    {
        var platform = ProbePlatform;
        if (!CapabilitiesFor(platform).HasFlag(PlatformCapabilities.Whisper))
        {
            WhisperNotice = null;
            return;
        }

        var whispers = _allRows.Any(row => row.EffectiveTarget.WhispersToCaller());
        if (!whispers)
        {
            WhisperNotice = null;
            return;
        }

        var adapter = _platformRegistry?.Find(platform);
        bool hasPermission;
        if (adapter?.HasWhisperPermission is { } adapterPermission)
        {
            hasPermission = adapterPermission;
        }
        else if (platform == StreamingPlatform.Twitch)
        {
            var bot = _authorization is null ? _accountsStore.LoadBot() : _authorization.GetAccount(PlatformAuthRole.Bot);
            hasPermission = bot is not null && TwitchScopes.IsGranted(bot, TwitchScopes.UserManageWhispers);
        }
        else
        {
            hasPermission = true;
        }

        WhisperNotice = hasPermission
            ? null
            : FormatWhisperReauthorizationNotice(PlatformVisuals.GetDisplayName(platform, _platformRegistry));
    }

    private void RebuildView()
    {
        var selected = SelectedRow?.Canonical;
        var query = FilterText.Trim();
        var hasFilter = query.Length > 0;

        _rebuildingView = true;

        try
        {
            _rows.Clear();

            foreach (var row in _allRows)
            {
                if (hasFilter && !row.Matches(query) && !IsEditedRow(row))
                {
                    continue;
                }

                row.RefreshLastUsedText();
                _rows.Add(row);
            }

            SelectedRow = _rows.FirstOrDefault(row => string.Equals(row.Canonical, selected, StringComparison.OrdinalIgnoreCase))
                          ?? _rows.FirstOrDefault();
        }
        finally
        {
            _rebuildingView = false;
        }

        OnPropertyChanged(nameof(HasSelectedRow));
        NotifyPlatformEnablementChanged();

        if (!ReferenceEquals(SelectedRow, _parametersRow))
        {
            ReloadParameters();
        }

        IsFilterActive = hasFilter;

        UpdateSummaries();

        OnPropertyChanged(nameof(HasCommands));
        OnPropertyChanged(nameof(EmptyHeading));
        OnPropertyChanged(nameof(EmptyDescription));
    }

    private bool IsEditedRow(CommandRowViewModel row)
    {
        return HasUnsavedChanges && ReferenceEquals(row, _parametersRow);
    }

    public sealed record PlatformEnablementOption(string Title, bool? Enabled);

    private void UpdateSummaries()
    {
        OnPropertyChanged(nameof(PlatformSummary));
        var enabled = _allRows.Count(row => row.IsEnabledInScope);
        var total = _allRows.Sum(row => row.TotalCount).ToString("N0", UiCulture.Russian);
        var stream = _allRows.Sum(row => row.StreamCount).ToString("N0", UiCulture.Russian);
        var visible = IsFilterActive ? $" · показано {_rows.Count}" : string.Empty;

        SummaryLine = $"Включено {enabled} из {_allRows.Count} · вызовов {total}, за текущий стрим {stream}{visible}";
    }

    private bool CanSaveParameters()
    {
        return Parameters is { IsDirty: true };
    }

    [RelayCommand(CanExecute = nameof(CanSaveParameters))]
    private void SaveParameters()
    {
        TrySaveParameters();
    }

    private bool TrySaveParameters()
    {
        if (Parameters is not { } parameters || _parametersRow is not { } row)
        {
            return true;
        }

        if (!parameters.Validate())
        {
            ShowParametersNotice("Проверьте выделенные поля – параметры не сохранены.", CommandNoticeTone.Error);

            return false;
        }

        bool written;

        try
        {
            written = _settingsManager.Mutate(parameters.Apply);
        }
        catch (Exception exception)
        {
            _logger.CommandParametersSaveFailed(exception, row.Invocation);
            ShowParametersNotice("Параметры не сохранились. Подробности в журнале.", CommandNoticeTone.Error);

            return false;
        }

        parameters.Load(_settingsManager.Current);

        if (written)
        {
            _logger.CommandParametersSaved(row.Invocation);
            ShowParametersNotice("Параметры сохранены", CommandNoticeTone.Info);
        }
        else
        {
            _logger.CommandParametersNotWritten(row.Invocation);
            ShowParametersNotice(NotWrittenNotice, CommandNoticeTone.Warning);
        }

        SaveParametersCommand.NotifyCanExecuteChanged();

        return true;
    }

    private void ShowParametersNotice(string text, CommandNoticeTone tone)
    {
        ParametersNoticeTone = tone;
        ParametersNotice = text;
    }

    [RelayCommand]
    private void ClearFilter()
    {
        FilterText = string.Empty;
    }

    ICommand IPageRefresh.RefreshCommand => RefreshCommand;

    [RelayCommand]
    private void Refresh()
    {
        if (!ConfirmParametersDraft("перечитыванием"))
        {
            return;
        }

        Reload();
    }
}
