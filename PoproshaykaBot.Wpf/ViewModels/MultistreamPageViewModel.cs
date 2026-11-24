using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.DependencyInjection;
using PoproshaykaBot.Core.Platforms;
using PoproshaykaBot.Core.Platforms.Auth;
using PoproshaykaBot.Core.Settings.Stores;
using PoproshaykaBot.Core.Streaming;
using PoproshaykaBot.Wpf.Infrastructure;
using PoproshaykaBot.Wpf.ViewModels.Dialogs;
using PoproshaykaBot.Wpf.ViewModels.Settings;
using PoproshaykaBot.Wpf.ViewModels.Tiles;

namespace PoproshaykaBot.Wpf.ViewModels;

public enum MultistreamPlatformTab
{
    Channels = 0,
    Accounts = 1,
    Parameters = 2,
}

public enum MultistreamAutomationTab
{
    Profiles = 0,
    Automation = 1,
    Groups = 2,
}

internal enum MultistreamSection
{
    None = 0,
    Channels = 1,
    Automation = 2,
    Platforms = 3,
    BotAuthorization = 4,
    Parameters = 5,
    Accounts = 6,
}

public enum MultistreamGuidedNextAction
{
    None = 0,
    PlatformSetup = 1,
    AddChannel = 2,
    AssignAccount = 3,
    RevisitSetup = 4,
}

public sealed partial class MultistreamPlatformCard : ObservableObject
{
    public MultistreamPlatformCard(MultistreamPlatformRowViewModel status, IMultistreamPlatformControls? controls)
    {
        Status = status;
        Controls = controls;
        status.PropertyChanged += (_, e) =>
        {
            if (string.IsNullOrEmpty(e.PropertyName)
                || e.PropertyName == nameof(MultistreamPlatformRowViewModel.AutomationSummary))
            {
                OnPropertyChanged(nameof(AutomationSummary));
            }
        };
    }

    public MultistreamPlatformRowViewModel Status { get; }

    public IMultistreamPlatformControls? Controls { get; }

    public string AutomationSummary => Status.AutomationSummary;

    [ObservableProperty]
    private bool _isSelected;
}

public sealed partial class MultistreamPageViewModel : ObservableObject, IPageHeader, IUnsavedChangesPage
{
    private readonly IReadOnlyDictionary<StreamingPlatform, IReadOnlyList<SettingsSectionBlockDescriptor>> _platformSettings;
    private readonly IDialogService? _dialogService;
    private readonly IServiceScopeFactory? _scopeFactory;
    private readonly YouTubeSettingsStore? _youtubeSettingsStore;
    private readonly ChannelCatalogStore? _catalogStore;
    private MultistreamSection _requestedSection;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SelectedPlatform), nameof(HasSelectedPlatform), nameof(IsPlatformSelected),
        nameof(SelectedPlatformCard), nameof(PlatformSettings), nameof(HasPlatformSettings),
        nameof(HasPlatformAccounts), nameof(HasPlatformTabs),
        nameof(PlatformSettingsTitle), nameof(EditorTitle), nameof(ShowParametersButtonInHeader),
        nameof(ShowBotAuthButtonInHeader), nameof(HeaderSetupActionLabel),
        nameof(SelectedPlatformChannelCount), nameof(HasSelectedPlatformChannels),
        nameof(SelectedPlatformAccountCount), nameof(HasSelectedPlatformAccounts))]
    [NotifyCanExecuteChangedFor(nameof(ExecuteGuidedPrimaryActionCommand), nameof(OpenPlatformSetupWizardCommand),
        nameof(OpenAddChannelWizardCommand), nameof(OpenAssignAccountWizardCommand))]
    private MultistreamPlatformCard? _selectedCard;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsPlatformSelected), nameof(SelectedPlatformCard), nameof(IsAutomationExpanded))]
    private bool _isAutomationSelected;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsChannelsTabSelected), nameof(IsAccountsTabSelected), nameof(IsParametersTabSelected))]
    private MultistreamPlatformTab _selectedPlatformTab = MultistreamPlatformTab.Channels;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsProfilesTabSelected), nameof(IsAutomationTabSelected), nameof(IsGroupsTabSelected))]
    private MultistreamAutomationTab _selectedAutomationTab = MultistreamAutomationTab.Profiles;

    [ObservableProperty]
    private string _platformReadinessText = "Площадка: не выбрана";

    [ObservableProperty]
    private StatusSeverity _platformReadinessSeverity;

    [ObservableProperty]
    private string _accountReadinessText = "Аккаунт: нужен вход бота";

    [ObservableProperty]
    private StatusSeverity _accountReadinessSeverity;

    [ObservableProperty]
    private string _channelReadinessText = "Канал: не добавлен";

    [ObservableProperty]
    private StatusSeverity _channelReadinessSeverity;

    [ObservableProperty]
    private string _guidedPrimaryActionLabel = "Настроить площадку";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowQuickPlatformSetupButton), nameof(ShowQuickAddChannelButton), nameof(ShowQuickAssignAccountButton))]
    private bool _isGuidedActionPrimary = true;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowQuickPlatformSetupButton), nameof(ShowQuickAddChannelButton), nameof(ShowQuickAssignAccountButton))]
    private MultistreamGuidedNextAction _guidedNextAction = MultistreamGuidedNextAction.PlatformSetup;

    public bool ShowQuickPlatformSetupButton =>
        GuidedNextAction is not (MultistreamGuidedNextAction.PlatformSetup or MultistreamGuidedNextAction.RevisitSetup);

    public bool ShowQuickAddChannelButton =>
        !(IsGuidedActionPrimary && GuidedNextAction == MultistreamGuidedNextAction.AddChannel);

    public bool ShowQuickAssignAccountButton =>
        !(IsGuidedActionPrimary && GuidedNextAction == MultistreamGuidedNextAction.AssignAccount);

    public MultistreamPageViewModel(
        SettingsPageViewModel settingsPage,
        MultistreamTileViewModel status,
        IEnumerable<IMultistreamPlatformControls> platformControls,
        PlatformAccountsSectionViewModel? accounts = null,
        IDialogService? dialogService = null,
        IServiceScopeFactory? scopeFactory = null,
        YouTubeSettingsStore? youtubeSettingsStore = null,
        ChannelCatalogStore? catalogStore = null)
    {
        ArgumentNullException.ThrowIfNull(settingsPage);
        ArgumentNullException.ThrowIfNull(status);
        ArgumentNullException.ThrowIfNull(platformControls);
        Settings = settingsPage;
        Status = status;
        Accounts = accounts;
        _dialogService = dialogService;
        _scopeFactory = scopeFactory;
        _youtubeSettingsStore = youtubeSettingsStore;
        _catalogStore = catalogStore;

        Settings.PropertyChanged += OnSettingsPropertyChanged;
        status.ApplyPlatformControls(platformControls);
        var controls = platformControls.ToDictionary(item => item.Platform);
        Cards = [.. status.Rows.Select(row => new MultistreamPlatformCard(row, controls.GetValueOrDefault(row.Platform)))];
        Catalog = Cards.Count > 0 ? settingsPage.GetSectionContent<ChannelCatalogSettingsSectionViewModel>() : null;
        if (Catalog is not null)
        {
            Catalog.PropertyChanged += OnCatalogPropertyChanged;
            Catalog.AccountsChanged += OnCatalogAccountsChanged;
            status.ChannelStatesRefreshed += RefreshChannelCards;
        }

        if (Accounts is not null)
        {
            var (twitchDraft, youtubeDraft) = settingsPage.GetCredentialDrafts();
            Accounts.SetCredentialDrafts(twitchDraft, youtubeDraft);
            Accounts.PropertyChanged += OnAccountsPropertyChanged;
            Accounts.AccountsChanged += OnAccountsSectionChanged;
        }

        var presentations = settingsPage.PlatformPresentations;
        _platformSettings = presentations
            .Select(presentation => (Platform: PlatformSettingsPages.GetPlatform(presentation.Group), presentation.Blocks))
            .Where(presentation => presentation.Platform.HasValue)
            .ToDictionary(
                presentation => presentation.Platform.GetValueOrDefault(),
                presentation => WithoutCatalog(presentation.Blocks));
        AutomationSettings = presentations.FirstOrDefault(p => p.Group?.Key == PlatformSettingsPages.MultistreamKey)?.Blocks ?? [];
        ProfilesBlocks = [.. AutomationSettings.Where(b => b.Key == "platforms/profiles")];
        LifecycleAndBroadcastBlocks = [.. AutomationSettings.Where(b => b.Key is "stream/lifecycle" or "stream/auto")];
        ChannelGroupsBlocks = [.. AutomationSettings.Where(b => b.Key == "channels/groups")];
        SelectedCard = Cards.Count > 0 ? Cards[0] : null;
        RefreshReadinessSummary();
    }

    internal event EventHandler? SectionRequested;

    public string PageTitle => "Мультистрим";

    public string? PageDescription => null;

    public SettingsPageViewModel Settings { get; }

    public MultistreamTileViewModel Status { get; }

    public ChannelCatalogSettingsSectionViewModel? Catalog { get; }

    public PlatformAccountsSectionViewModel? Accounts { get; }

    public IReadOnlyList<MultistreamPlatformCard> Cards { get; }

    public MultistreamPlatformCard? SelectedPlatformCard
    {
        get => IsAutomationSelected ? null : SelectedCard;
        set
        {
            if (value is not null)
            {
                SelectPlatform(value);
            }
        }
    }

    public StreamingPlatform? SelectedPlatform => SelectedCard?.Status.Platform;

    public bool HasSelectedPlatform => SelectedCard is not null;

    public bool IsPlatformSelected => !IsAutomationSelected && HasSelectedPlatform;

    public bool IsAutomationExpanded
    {
        get => IsAutomationSelected;
        set => IsAutomationSelected = value;
    }

    public bool IsChannelsTabSelected
    {
        get => SelectedPlatformTab == MultistreamPlatformTab.Channels;
        set
        {
            if (value)
            {
                SelectPlatformTab(MultistreamPlatformTab.Channels);
            }
        }
    }

    public bool IsAccountsTabSelected
    {
        get => SelectedPlatformTab == MultistreamPlatformTab.Accounts;
        set
        {
            if (value)
            {
                SelectPlatformTab(MultistreamPlatformTab.Accounts);
            }
        }
    }

    public bool IsParametersTabSelected
    {
        get => SelectedPlatformTab == MultistreamPlatformTab.Parameters;
        set
        {
            if (value)
            {
                SelectPlatformTab(MultistreamPlatformTab.Parameters);
            }
        }
    }

    public bool IsProfilesTabSelected
    {
        get => SelectedAutomationTab == MultistreamAutomationTab.Profiles;
        set
        {
            if (value)
            {
                SelectAutomationTab(MultistreamAutomationTab.Profiles);
            }
        }
    }

    public bool IsAutomationTabSelected
    {
        get => SelectedAutomationTab == MultistreamAutomationTab.Automation;
        set
        {
            if (value)
            {
                SelectAutomationTab(MultistreamAutomationTab.Automation);
            }
        }
    }

    public bool IsGroupsTabSelected
    {
        get => SelectedAutomationTab == MultistreamAutomationTab.Groups;
        set
        {
            if (value)
            {
                SelectAutomationTab(MultistreamAutomationTab.Groups);
            }
        }
    }

    public string HeaderSetupActionLabel => SelectedCard?.Controls?.SetupActionLabel ?? "Открыть параметры";

    public bool ShowParametersButtonInHeader =>
        SelectedCard is { } card
        && !card.Status.ShowConnectionAction
        && card.Controls?.RequiresCardSetup == true
        && HasPlatformSettings;

    public bool ShowBotAuthButtonInHeader =>
        SelectedCard is { } card
        && !card.Status.ShowConnectionAction
        && !ShowParametersButtonInHeader
        && card.Status.NeedsBotAuthorization;

    public IReadOnlyList<SettingsSectionBlockDescriptor> PlatformSettings =>
        SelectedPlatform is { } platform ? _platformSettings.GetValueOrDefault(platform) ?? [] : [];

    public bool HasPlatformSettings => PlatformSettings.Count > 0;

    public bool HasPlatformAccounts => Accounts?.SupportsAccountsFor(SelectedPlatform) == true;

    public bool HasPlatformTabs => HasPlatformSettings || HasPlatformAccounts;

    public IReadOnlyList<SettingsSectionBlockDescriptor> AutomationSettings { get; }

    public IReadOnlyList<SettingsSectionBlockDescriptor> ProfilesBlocks { get; }

    public IReadOnlyList<SettingsSectionBlockDescriptor> LifecycleAndBroadcastBlocks { get; }

    public IReadOnlyList<SettingsSectionBlockDescriptor> AutomationBlocks => LifecycleAndBroadcastBlocks;

    public IReadOnlyList<SettingsSectionBlockDescriptor> ChannelGroupsBlocks { get; }

    public string? PlatformSettingsTitle => SelectedCard is { } card ? $"Параметры {card.Status.DisplayName}" : null;

    public string? EditorTitle => SelectedCard is { } card ? $"Каналы {card.Status.DisplayName}" : null;

    public int SelectedPlatformChannelCount => Catalog?.VisibleChannelCount ?? 0;

    public bool HasSelectedPlatformChannels => SelectedPlatformChannelCount > 0;

    public int SelectedPlatformAccountCount => Accounts?.AccountCount ?? 0;

    public bool HasSelectedPlatformAccounts => SelectedPlatformAccountCount > 0;

    public bool HasUnsavedChanges => Settings.HasUnsavedChanges;

    public string UnsavedChangesSubject => "На странице «Мультистрим» есть несохранённые изменения.";

    public void OnEnter()
    {
        if (Settings.IsSaving)
        {
            return;
        }

        if (Catalog is not null && SelectedPlatform is { } platform)
        {
            Catalog.SelectedPlatform = platform;
        }

        if (Accounts is not null && SelectedPlatform is { } selectedPlatform)
        {
            Accounts.SelectedPlatform = selectedPlatform;
            Accounts.Load();
        }

        Settings.ReloadDraft();
        RefreshReadinessSummary();
    }

    [RelayCommand]
    public void SelectPlatform(MultistreamPlatformCard? card)
    {
        if (Settings.IsSaving || card is null)
        {
            return;
        }

        IsAutomationSelected = false;
        SelectedCard = card;
        NormalizeSelectedPlatformTab();
    }

    [RelayCommand]
    public void SelectPlatformTab(MultistreamPlatformTab tab)
    {
        var previous = SelectedPlatformTab;
        SelectedPlatformTab = tab switch
        {
            MultistreamPlatformTab.Parameters when !HasPlatformSettings => MultistreamPlatformTab.Channels,
            MultistreamPlatformTab.Accounts when !HasPlatformAccounts => MultistreamPlatformTab.Channels,
            _ => tab,
        };
        if (SelectedPlatformTab == MultistreamPlatformTab.Accounts && previous != MultistreamPlatformTab.Accounts)
        {
            Accounts?.Load();
        }
    }

    [RelayCommand]
    public void SelectAutomationTab(MultistreamAutomationTab tab) => SelectedAutomationTab = tab;

    [RelayCommand]
    public void OpenSelectedPlatformParameters()
    {
        if (Settings.IsSaving || !HasPlatformSettings)
        {
            return;
        }

        IsAutomationSelected = false;
        SelectedPlatformTab = MultistreamPlatformTab.Parameters;
        Reveal(MultistreamSection.Parameters);
    }

    [RelayCommand]
    public void OpenSelectedPlatformBotAuth()
    {
        if (Settings.IsSaving || SelectedCard is null)
        {
            return;
        }

        IsAutomationSelected = false;
        SelectedPlatformTab = MultistreamPlatformTab.Channels;
        Reveal(MultistreamSection.BotAuthorization);
    }

    [RelayCommand]
    public void OpenPlatform(StreamingPlatform platform)
    {
        if (Settings.IsSaving)
        {
            return;
        }

        var card = Cards.FirstOrDefault(candidate => candidate.Status.Platform == platform);
        if (card is not null)
        {
            IsAutomationSelected = false;
            SelectedCard = card;
            Catalog?.SelectActiveChannel();
            if (card.Controls?.RequiresCardSetup == true && HasPlatformSettings)
            {
                SelectedPlatformTab = MultistreamPlatformTab.Parameters;
                Reveal(MultistreamSection.Parameters);
            }
            else if (card.Status.IsBlockedByDebugSession)
            {
                SelectedPlatformTab = MultistreamPlatformTab.Channels;
                Reveal(MultistreamSection.Platforms);
            }
            else if (card.Status.NeedsBotAuthorization)
            {
                SelectedPlatformTab = MultistreamPlatformTab.Channels;
                Reveal(MultistreamSection.BotAuthorization);
            }
            else
            {
                SelectedPlatformTab = MultistreamPlatformTab.Channels;
                Reveal(MultistreamSection.Channels);
            }
        }
    }

    internal void OpenPlatformFromOverview(StreamingPlatform platform) => OpenPlatform(platform);

    [RelayCommand]
    public void OpenAutomation()
    {
        if (Settings.IsSaving)
        {
            return;
        }

        IsAutomationSelected = true;
        Reveal(MultistreamSection.Automation);
    }

    private bool CanOpenWizards() =>
        !Settings.IsSaving
        && SelectedPlatform is not null
        && _dialogService is not null
        && _scopeFactory is not null;

    private bool CanOpenAssignAccountWizard() => CanOpenWizards() && HasSelectedPlatformChannels;

    [RelayCommand(CanExecute = nameof(CanOpenWizards))]
    private Task ExecuteGuidedPrimaryActionAsync() => GuidedNextAction switch
    {
        MultistreamGuidedNextAction.AddChannel => OpenAddChannelWizardAsync(),
        MultistreamGuidedNextAction.AssignAccount => OpenAssignAccountWizardAsync(),
        _ => OpenPlatformSetupWizardAsync(),
    };

    [RelayCommand(CanExecute = nameof(CanOpenWizards))]
    private async Task OpenPlatformSetupWizardAsync()
    {
        if (SelectedPlatform is not { } platform || _dialogService is null || _scopeFactory is null)
        {
            return;
        }

        using var scope = _scopeFactory.CreateScope();
        var dialog = scope.ServiceProvider.GetRequiredService<MultistreamPlatformSetupDialogViewModel>();
        dialog.Initialize(platform);

        if (await _dialogService.ShowAsync(dialog).ConfigureAwait(true) || dialog.HasSavedChanges)
        {
            ReloadAfterWizard();
        }
    }

    [RelayCommand(CanExecute = nameof(CanOpenWizards))]
    private async Task OpenAddChannelWizardAsync()
    {
        if (SelectedPlatform is not { } platform || _dialogService is null || _scopeFactory is null)
        {
            return;
        }

        using var scope = _scopeFactory.CreateScope();
        var dialog = scope.ServiceProvider.GetRequiredService<MultistreamChannelWizardDialogViewModel>();
        dialog.InitializeForAdd(platform);

        if (await _dialogService.ShowAsync(dialog).ConfigureAwait(true) || dialog.HasSavedChanges)
        {
            ReloadAfterWizard();
        }
    }

    [RelayCommand(CanExecute = nameof(CanOpenAssignAccountWizard))]
    private async Task OpenAssignAccountWizardAsync()
    {
        if (SelectedPlatform is not { } platform || _dialogService is null || _scopeFactory is null)
        {
            return;
        }

        var preferredChannelId = Catalog?.SelectedChannel?.ChannelId
            ?? _catalogStore?.GetActiveChannel(platform)?.ChannelId;

        using var scope = _scopeFactory.CreateScope();
        var dialog = scope.ServiceProvider.GetRequiredService<MultistreamChannelWizardDialogViewModel>();
        dialog.InitializeForAssignAccount(platform, preferredChannelId);

        if (await _dialogService.ShowAsync(dialog).ConfigureAwait(true) || dialog.HasSavedChanges)
        {
            ReloadAfterWizard();
        }
    }

    private void ReloadAfterWizard()
    {
        Settings.ReloadDraft();
        if (SelectedPlatform is { } platform)
        {
            if (Catalog is not null)
            {
                Catalog.SelectedPlatform = platform;
                Catalog.SelectActiveChannel();
            }

            if (Accounts is not null)
            {
                Accounts.SelectedPlatform = platform;
                Accounts.Load();
            }
        }

        NotifyHeaderActionsChanged();
        RefreshReadinessSummary();
    }

    internal MultistreamSection TakeSectionRequest()
    {
        var requested = _requestedSection;
        _requestedSection = MultistreamSection.None;
        return requested;
    }

    private void Reveal(MultistreamSection section)
    {
        _requestedSection = section;
        SectionRequested?.Invoke(this, EventArgs.Empty);
    }

    private static IReadOnlyList<SettingsSectionBlockDescriptor> WithoutCatalog(IEnumerable<SettingsSectionBlockDescriptor> blocks) =>
        [.. blocks.Where(block => block.Key != PlatformSettingsPages.ChannelCatalogKey)];

    partial void OnSelectedCardChanged(MultistreamPlatformCard? oldValue, MultistreamPlatformCard? newValue)
    {
        if (oldValue is not null)
        {
            oldValue.Status.PropertyChanged -= OnSelectedCardStatusChanged;
            if (oldValue.Controls is not null)
            {
                oldValue.Controls.StateChanged -= OnSelectedCardControlsChanged;
            }
        }

        if (newValue is not null)
        {
            newValue.Status.PropertyChanged += OnSelectedCardStatusChanged;
            if (newValue.Controls is not null)
            {
                newValue.Controls.StateChanged += OnSelectedCardControlsChanged;
            }

            if (Catalog is not null)
            {
                Catalog.SelectedPlatform = newValue.Status.Platform;
            }

            if (Accounts is not null)
            {
                Accounts.SelectedPlatform = newValue.Status.Platform;
            }
        }

        NormalizeSelectedPlatformTab();
        SyncCardSelection();
        RefreshReadinessSummary();
    }

    private void NormalizeSelectedPlatformTab()
    {
        if ((SelectedPlatformTab == MultistreamPlatformTab.Parameters && !HasPlatformSettings)
            || (SelectedPlatformTab == MultistreamPlatformTab.Accounts && !HasPlatformAccounts))
        {
            SelectedPlatformTab = MultistreamPlatformTab.Channels;
        }
    }

    private void OnSettingsPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (string.IsNullOrEmpty(e.PropertyName) || e.PropertyName == nameof(SettingsPageViewModel.IsSaving))
        {
            NotifyWizardCommandsCanExecuteChanged();
        }
    }

    private void OnCatalogAccountsChanged(object? sender, StreamingPlatform platform)
    {
        Accounts?.RefreshIfPlatform(platform);
        RefreshReadinessSummary();
    }

    private void OnAccountsSectionChanged(object? sender, StreamingPlatform platform)
    {
        Catalog?.RefreshAccountOptions(platform);
        RefreshReadinessSummary();
    }

    private void OnAccountsPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (string.IsNullOrEmpty(e.PropertyName)
            || e.PropertyName is nameof(PlatformAccountsSectionViewModel.AccountCount)
                or nameof(PlatformAccountsSectionViewModel.HasAccounts))
        {
            OnPropertyChanged(nameof(SelectedPlatformAccountCount));
            OnPropertyChanged(nameof(HasSelectedPlatformAccounts));
            RefreshReadinessSummary();
        }
    }

    private void OnSelectedCardStatusChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (string.IsNullOrEmpty(e.PropertyName)
            || e.PropertyName is nameof(MultistreamPlatformRowViewModel.ShowConnectionAction)
                or nameof(MultistreamPlatformRowViewModel.NeedsBotAuthorization)
                or nameof(MultistreamPlatformRowViewModel.IsPlatformEnabled)
                or nameof(MultistreamPlatformRowViewModel.ChannelName)
                or nameof(MultistreamPlatformRowViewModel.ChannelCount))
        {
            NotifyHeaderActionsChanged();
            RefreshReadinessSummary();
        }
    }

    private void OnSelectedCardControlsChanged(object? sender, EventArgs e)
    {
        NotifyHeaderActionsChanged();
        RefreshReadinessSummary();
    }

    private void OnCatalogPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ChannelCatalogSettingsSectionViewModel.VisibleChannelCount))
        {
            RefreshChannelCards();
        }

        if (string.IsNullOrEmpty(e.PropertyName)
            || e.PropertyName is nameof(ChannelCatalogSettingsSectionViewModel.VisibleChannelCount)
                or nameof(ChannelCatalogSettingsSectionViewModel.HasChannels)
                or nameof(ChannelCatalogSettingsSectionViewModel.SelectedChannel))
        {
            OnPropertyChanged(nameof(SelectedPlatformChannelCount));
            OnPropertyChanged(nameof(HasSelectedPlatformChannels));
            OpenAssignAccountWizardCommand.NotifyCanExecuteChanged();
            RefreshReadinessSummary();
        }
    }

    private void RefreshChannelCards()
    {
        if (Catalog is null || SelectedCard?.Status is not { } platform || Catalog.SelectedPlatform != platform.Platform)
        {
            return;
        }

        foreach (var channel in Catalog.VisibleChannels)
        {
            var isActive = string.Equals(channel.ChannelId, platform.ActiveChannelId, StringComparison.Ordinal);
            var snapshot = platform.IsPlatformEnabled && !platform.IsChannelSwitchBlockedByDebug
                ? Status.GetChannelSnapshot(new(channel.Platform, channel.ChannelId))
                : null;
            var (status, stream, detail) = DescribeChannelBroadcast(platform, snapshot, isActive);
            var (text, severity) = status switch
            {
                StreamStatus.Online when stream?.ViewerCount > 0 =>
                    ($"В эфире · {stream.ViewerCount.ToString("N0", UiCulture.Russian)} зр.", StatusSeverity.Success),
                StreamStatus.Online => ("В эфире", StatusSeverity.Success),
                StreamStatus.Offline => ("Не в эфире", StatusSeverity.None),
                _ => ("Статус эфира неизвестен", StatusSeverity.Info),
            };
            channel.RuntimeStatus = new(text, severity, detail,
                isActive ? platform.ChatText : "Чат: не подключён",
                isActive ? platform.ChatSeverity : StatusSeverity.None);
        }
    }

    private static (StreamStatus Status, StreamInfo? Stream, string? Detail) DescribeChannelBroadcast(
        MultistreamPlatformRowViewModel platform, ChannelMonitoringSnapshot? snapshot, bool isActive)
    {
        if (platform.IsChannelSwitchBlockedByDebug || !platform.IsPlatformEnabled)
        {
            return (StreamStatus.Unknown, null, platform.IsChannelSwitchBlockedByDebug
                ? "Площадка работает в режиме отладки"
                : "Площадка выключена");
        }

        if (snapshot is { Availability: ChannelMonitoringAvailability.Active or ChannelMonitoringAvailability.Degraded,
            Status: not StreamStatus.Unknown })
        {
            return (snapshot.Status, snapshot.Stream, snapshot.Stream?.Title);
        }

        if (snapshot is null && isActive)
        {
            var current = platform.Source.StreamStatus.GetSnapshot();
            return (current.Status, current.Stream, current.Stream?.Title);
        }

        return (StreamStatus.Unknown, null, snapshot?.Detail
            ?? MultistreamPlatformRowViewModel.DescribeUnavailableReason(snapshot?.Reason));
    }

    private void NotifyHeaderActionsChanged()
    {
        OnPropertyChanged(nameof(ShowParametersButtonInHeader));
        OnPropertyChanged(nameof(ShowBotAuthButtonInHeader));
        OnPropertyChanged(nameof(HeaderSetupActionLabel));
    }

    private void NotifyWizardCommandsCanExecuteChanged()
    {
        ExecuteGuidedPrimaryActionCommand.NotifyCanExecuteChanged();
        OpenPlatformSetupWizardCommand.NotifyCanExecuteChanged();
        OpenAddChannelWizardCommand.NotifyCanExecuteChanged();
        OpenAssignAccountWizardCommand.NotifyCanExecuteChanged();
    }

    internal void RefreshReadinessSummary()
    {
        if (SelectedCard is not { } card)
        {
            PlatformReadinessText = "Площадка: не выбрана";
            PlatformReadinessSeverity = StatusSeverity.None;
            AccountReadinessText = "Аккаунт: –";
            AccountReadinessSeverity = StatusSeverity.None;
            ChannelReadinessText = "Канал: –";
            ChannelReadinessSeverity = StatusSeverity.None;
            GuidedNextAction = MultistreamGuidedNextAction.PlatformSetup;
            GuidedPrimaryActionLabel = "Настроить площадку";
            IsGuidedActionPrimary = false;
            NotifyWizardCommandsCanExecuteChanged();
            return;
        }

        var platform = card.Status.Platform;
        var platformEnabled = ResolvePlatformEnabled(card);
        PlatformReadinessText = platformEnabled ? "Площадка: включена" : "Площадка: отключена";
        PlatformReadinessSeverity = platformEnabled ? StatusSeverity.Success : StatusSeverity.Warning;

        var (hasBotReady, hasBroadcasterReady, hasBroadcasterScopeWarning) = ResolveAccountReadiness(card);
        if (!hasBotReady)
        {
            AccountReadinessText = "Аккаунт: нужен вход бота";
            AccountReadinessSeverity = StatusSeverity.Warning;
        }
        else if (hasBroadcasterScopeWarning)
        {
            AccountReadinessText = "Аккаунт: владельцу нужны права записи";
            AccountReadinessSeverity = StatusSeverity.Warning;
        }
        else if (hasBroadcasterReady)
        {
            AccountReadinessText = "Аккаунт: бот и владелец";
            AccountReadinessSeverity = StatusSeverity.Success;
        }
        else
        {
            AccountReadinessText = "Аккаунт: бот готов";
            AccountReadinessSeverity = StatusSeverity.Success;
        }

        var (hasChannels, channelReady, channelText, channelSeverity) = ResolveChannelReadiness(platform);
        ChannelReadinessText = channelText;
        ChannelReadinessSeverity = channelSeverity;

        if (!platformEnabled || !hasBotReady)
        {
            GuidedNextAction = MultistreamGuidedNextAction.PlatformSetup;
            GuidedPrimaryActionLabel = "Настроить площадку";
            IsGuidedActionPrimary = true;
        }
        else if (!hasChannels)
        {
            GuidedNextAction = MultistreamGuidedNextAction.AddChannel;
            GuidedPrimaryActionLabel = "Добавить канал";
            IsGuidedActionPrimary = true;
        }
        else if (!channelReady)
        {
            GuidedNextAction = MultistreamGuidedNextAction.AssignAccount;
            GuidedPrimaryActionLabel = "Назначить аккаунт";
            IsGuidedActionPrimary = true;
        }
        else
        {
            GuidedNextAction = MultistreamGuidedNextAction.RevisitSetup;
            GuidedPrimaryActionLabel = "Мастер настройки";
            IsGuidedActionPrimary = false;
        }

        NotifyWizardCommandsCanExecuteChanged();
    }

    private bool ResolvePlatformEnabled(MultistreamPlatformCard card)
    {
        var platform = card.Status.Platform;
        if (platform == StreamingPlatform.Twitch)
        {
            return true;
        }

        if (platform == StreamingPlatform.YouTube)
        {
            if (card.Controls is not null)
            {
                return !card.Controls.RequiresCardSetup;
            }

            if (_youtubeSettingsStore is not null)
            {
                return _youtubeSettingsStore.Load().Enabled;
            }
        }

        return card.Status.IsPlatformEnabled;
    }

    private (bool HasBotReady, bool HasBroadcasterReady, bool HasBroadcasterScopeWarning) ResolveAccountReadiness(MultistreamPlatformCard card)
    {
        var platform = card.Status.Platform;
        if (card.Status.NeedsBotAuthorization)
        {
            return (false, false, false);
        }

        if (Accounts is not null && Accounts.SupportsAccountsFor(platform) && Accounts.SelectedPlatform == platform)
        {
            var hasBot = Accounts.Accounts.Any(static account =>
                account.HasBotRole && account.BotBadgeSeverity is StatusSeverity.Success or StatusSeverity.Info);
            var hasBroadcaster = Accounts.Accounts.Any(static account =>
                account.HasBroadcasterRole && account.BroadcasterBadgeSeverity is StatusSeverity.Success or StatusSeverity.Info);
            var hasBroadcasterScopeWarning = false;
            if (platform == StreamingPlatform.YouTube)
            {
                var activeYouTubeChannel = _catalogStore?.GetActiveChannel(StreamingPlatform.YouTube);
                var activeBroadcasterId = activeYouTubeChannel?.GetAccountAssignment(PlatformAuthRole.Broadcaster)?.Account.AccountId;
                var activeBroadcasterContextId = activeYouTubeChannel?.BroadcasterContextId;
                var relevantAccounts = !string.IsNullOrWhiteSpace(activeBroadcasterId)
                    ? Accounts.Accounts.Where(a => string.Equals(a.AccountId, activeBroadcasterId, StringComparison.Ordinal))
                    : Accounts.Accounts;

                hasBroadcasterScopeWarning = relevantAccounts.Any(account =>
                    account.Grants.Any(grant =>
                        grant.Role == PlatformAuthRole.Broadcaster
                        && grant.TokenStatusSeverity == StatusSeverity.Warning
                        && (string.IsNullOrWhiteSpace(activeBroadcasterContextId)
                            || string.Equals(grant.ContextId, activeBroadcasterContextId, StringComparison.Ordinal))));
            }
            return (hasBot, hasBroadcaster, hasBroadcasterScopeWarning);
        }

        var activeChannel = Catalog?.VisibleChannels.FirstOrDefault(static item => item.IsActive)
            ?? Catalog?.VisibleChannels.FirstOrDefault();
        var hasBroadcasterAssigned = activeChannel is not null && activeChannel.Roles[1].HasAssignedAccount;
        return (true, hasBroadcasterAssigned, false);
    }

    private (bool HasChannels, bool IsChannelReady, string Text, StatusSeverity Severity) ResolveChannelReadiness(StreamingPlatform platform)
    {
        var channels = Catalog?.VisibleChannels;
        if (channels is null || channels.Count == 0)
        {
            var stored = _catalogStore?.GetActiveChannel(platform);
            if (stored is null)
            {
                return (false, false, "Канал: не добавлен", StatusSeverity.Warning);
            }

            var storedName = string.IsNullOrWhiteSpace(stored.DisplayName) ? stored.ChannelId : stored.DisplayName;
            var botAssignment = stored.GetAccountAssignment(PlatformAuthRole.Bot);
            if (botAssignment is null)
            {
                return (true, false, $"Канал: {storedName} · нет аккаунта", StatusSeverity.Warning);
            }

            return botAssignment.IsVerified
                ? (true, true, $"Канал: {storedName} · доступ ОК", StatusSeverity.Success)
                : (true, false, $"Канал: {storedName} · нужна проверка", StatusSeverity.Warning);
        }

        var active = channels.FirstOrDefault(static item => item.IsActive) ?? channels[0];
        var botRole = active.Roles[0];
        if (!botRole.CanManageAccount)
        {
            return (true, true, $"Канал: {active.DisplayName}", StatusSeverity.Info);
        }

        if (!botRole.HasAssignedAccount)
        {
            return (true, false, $"Канал: {active.DisplayName} · нет аккаунта", StatusSeverity.Warning);
        }

        if (botRole.StatusSeverity == StatusSeverity.Error)
        {
            return (true, false, $"Канал: {active.DisplayName} · ошибка доступа", StatusSeverity.Error);
        }

        if (botRole.NeedsVerification)
        {
            return (true, false, $"Канал: {active.DisplayName} · нужна проверка", StatusSeverity.Warning);
        }

        return (true, true, $"Канал: {active.DisplayName} · доступ ОК", StatusSeverity.Success);
    }

    partial void OnIsAutomationSelectedChanged(bool value) => SyncCardSelection();

    partial void OnSelectedPlatformTabChanged(MultistreamPlatformTab value)
    {
        if ((value == MultistreamPlatformTab.Parameters && !HasPlatformSettings)
            || (value == MultistreamPlatformTab.Accounts && !HasPlatformAccounts))
        {
            SelectedPlatformTab = MultistreamPlatformTab.Channels;
        }
    }

    private void SyncCardSelection()
    {
        foreach (var card in Cards)
        {
            card.IsSelected = !IsAutomationSelected && ReferenceEquals(card, SelectedCard);
        }
    }

    public Task<bool> TrySaveUnsavedChangesAsync() => Settings.TrySaveUnsavedChangesAsync();

    [RelayCommand]
    public void DiscardUnsavedChanges()
    {
        Accounts?.DiscardUnsavedManualEdits();
        OnEnter();
    }
}
