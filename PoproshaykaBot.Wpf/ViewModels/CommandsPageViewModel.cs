using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using PoproshaykaBot.Core.Chat;
using PoproshaykaBot.Core.Chat.Commands;
using PoproshaykaBot.Core.Settings.Stores;
using PoproshaykaBot.Core.Statistics;
using PoproshaykaBot.Wpf.Bootstrap;
using PoproshaykaBot.Wpf.Infrastructure;
using System.Collections.ObjectModel;

namespace PoproshaykaBot.Wpf.ViewModels;

public sealed partial class CommandsPageViewModel : ObservableObject, IPageHeader
{
    private static readonly CommandContext ViewerProbe = new()
    {
        Username = "viewer",
        DisplayName = "viewer",
    };

    private static readonly CommandContext BroadcasterProbe = new()
    {
        Username = "broadcaster",
        DisplayName = "broadcaster",
        IsBroadcaster = true,
        IsModerator = true,
    };

    private readonly CommandSettingsStore _settingsStore;
    private readonly CommandUsageRepository _usageRepository;
    private readonly ILogger<CommandsPageViewModel> _logger;
    private readonly List<CommandRowViewModel> _allRows = [];
    private readonly ObservableCollection<CommandRowViewModel> _rows = [];

    private bool _applying;

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

    public CommandsPageViewModel(
        ChatCommandProcessor processor,
        CommandSettingsStore settingsStore,
        CommandUsageRepository usageRepository,
        ILogger<CommandsPageViewModel> logger)
    {
        ArgumentNullException.ThrowIfNull(processor);

        _settingsStore = settingsStore;
        _usageRepository = usageRepository;
        _logger = logger;

        var byName = StringComparer.Create(UiCulture.Russian, ignoreCase: true);

        foreach (var command in processor.GetAllCommands().OrderBy(command => command.Canonical, byName))
        {
            var row = new CommandRowViewModel(command, processor.Prefix, ProbeAccess(command));
            row.EnabledChanged += OnRowEnabledChanged;
            row.TargetChanged += OnRowTargetChanged;
            _allRows.Add(row);
        }

        Reload();
    }

    public string PageTitle => "Команды";

    public string? PageDescription => "Включение, цель ответа и статистика вызовов";

    public ObservableCollection<CommandRowViewModel> Rows => _rows;

    public IReadOnlyList<CommandResponseTargetOption> CommandTargetOptions => CommandResponseTargetOption.ForCommand;

    public IReadOnlyList<CommandResponseTargetOption> DefaultTargetOptions => CommandResponseTargetOption.ForDefault;

    public bool HasCommands => _rows.Count > 0;

    public bool HasSelectedRow => SelectedRow is not null;

    public string EmptyHeading => IsFilterActive ? "Ничего не найдено" : "Команд нет";

    public string EmptyDescription => IsFilterActive
        ? "Ни одна команда не подходит под поиск. Очистите его, чтобы увидеть весь список."
        : "Ни одной команды чата не зарегистрировано.";

    public string? EmptyActionText => "Сбросить поиск";

    public IRelayCommand? EmptyActionCommand => IsFilterActive ? ClearFilterCommand : null;

    public string UsageHint => HasUsage
        ? string.Empty
        : "Счётчики вызовов читаются при подключении бота – до него они пустые";

    public void OnEnter()
    {
        Reload();
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

    private void Reload()
    {
        var settings = _settingsStore.Load();

        _applying = true;

        try
        {
            DefaultTargetOption = CommandResponseTargetOption.ForDefaultTarget(settings.DefaultResponseTarget);

            foreach (var row in _allRows)
            {
                row.ApplySettings(settings);
            }
        }
        finally
        {
            _applying = false;
        }

        RefreshUsage();

        RebuildView();
    }

    partial void OnFilterTextChanged(string value)
    {
        RebuildView();
    }

    partial void OnSelectedRowChanged(CommandRowViewModel? value)
    {
        OnPropertyChanged(nameof(HasSelectedRow));
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

    private static CommandAccess ProbeAccess(IChatCommand command)
    {
        if (command.IsRestrictedToAllowedUsers)
        {
            return CommandAccess.AllowedUsers;
        }

        return command.CanExecute(ViewerProbe)
            ? CommandAccess.Everyone
            : command.CanExecute(BroadcasterProbe)
                ? CommandAccess.Moderators
                : CommandAccess.None;
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
            && existing is { Enabled: true, ResponseTarget: null })
        {
            settings.Commands.Remove(canonical);
        }
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
            Reload();

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
                row.ApplySettings(settings);
            }
        }
        finally
        {
            _applying = false;
        }
    }

    private void RebuildView()
    {
        var selected = SelectedRow?.Canonical;
        var query = FilterText.Trim();
        var hasFilter = query.Length > 0;

        _rows.Clear();

        foreach (var row in _allRows)
        {
            if (hasFilter && !row.Matches(query))
            {
                continue;
            }

            row.RefreshLastUsedText();
            _rows.Add(row);
        }

        SelectedRow = _rows.FirstOrDefault(row => string.Equals(row.Canonical, selected, StringComparison.OrdinalIgnoreCase))
                      ?? _rows.FirstOrDefault();

        IsFilterActive = hasFilter;

        UpdateSummaries();

        OnPropertyChanged(nameof(HasCommands));
        OnPropertyChanged(nameof(EmptyHeading));
        OnPropertyChanged(nameof(EmptyDescription));
    }

    private void UpdateSummaries()
    {
        var enabled = _allRows.Count(row => row.IsEnabled);
        var total = _allRows.Sum(row => row.TotalCount).ToString("N0", UiCulture.Russian);
        var stream = _allRows.Sum(row => row.StreamCount).ToString("N0", UiCulture.Russian);
        var visible = IsFilterActive ? $" · показано {_rows.Count}" : string.Empty;

        SummaryLine = $"Включено {enabled} из {_allRows.Count} · вызовов {total}, за текущий стрим {stream}{visible}";
    }

    [RelayCommand]
    private void ClearFilter()
    {
        FilterText = string.Empty;
    }

    [RelayCommand]
    private void Refresh()
    {
        Reload();
    }
}
