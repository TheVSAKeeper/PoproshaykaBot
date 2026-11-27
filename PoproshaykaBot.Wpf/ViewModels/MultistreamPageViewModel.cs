using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using KeepShell.ViewModels;
using MahApps.Metro.IconPacks;
using PoproshaykaBot.Core.Platforms;
using PoproshaykaBot.Core.Platforms.Auth;
using PoproshaykaBot.Core.Settings.Stores;
using PoproshaykaBot.Core.Streaming;
using PoproshaykaBot.Wpf.Infrastructure;
using PoproshaykaBot.Wpf.ViewModels.Settings;
using PoproshaykaBot.Wpf.ViewModels.Tiles;

namespace PoproshaykaBot.Wpf.ViewModels;

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

public sealed record MultistreamAutomationTabOption(
    MultistreamAutomationTab Tab,
    SegmentOption Option,
    string AutomationId)
{
    public PackIconLucideKind Kind => Option.Kind;

    public string Text => Option.Text;

    public string? Tooltip => Option.Tooltip;

    public override string ToString() => Option.ToString();
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
    private readonly ChannelCatalogStore? _catalogStore;
    private MultistreamSection _requestedSection;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SelectedPlatform), nameof(HasSelectedPlatform), nameof(IsPlatformSelected),
        nameof(SelectedPlatformCard), nameof(PlatformSettings), nameof(HasPlatformSettings),
        nameof(HasPlatformAccounts), nameof(HasStep1), nameof(HasStep2),
        nameof(PlatformSettingsTitle), nameof(EditorTitle), nameof(ShowParametersButtonInHeader),
        nameof(ShowBotAuthButtonInHeader), nameof(HeaderSetupActionLabel),
        nameof(SelectedPlatformChannelCount), nameof(HasSelectedPlatformChannels),
        nameof(SelectedPlatformAccountCount), nameof(HasSelectedPlatformAccounts))]
    private MultistreamPlatformCard? _selectedCard;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsPlatformSelected), nameof(SelectedPlatformCard), nameof(IsAutomationExpanded))]
    private bool _isAutomationSelected;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsProfilesTabSelected), nameof(IsAutomationTabSelected), nameof(IsGroupsTabSelected))]
    private MultistreamAutomationTab _selectedAutomationTab = MultistreamAutomationTab.Profiles;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsStep1Expanded), nameof(IsStep1CollapsedSummaryVisible), nameof(IsStep1BodyVisible), nameof(Step1HeaderActionLabel))]
    private bool _isStep1Complete;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsStep1Expanded), nameof(IsStep1CollapsedSummaryVisible), nameof(IsStep1BodyVisible), nameof(Step1HeaderActionLabel))]
    private bool _isStep1UserExpanded;

    [ObservableProperty]
    private string _step1SummaryText = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsStep2Expanded), nameof(IsStep2CollapsedSummaryVisible), nameof(IsStep2BodyVisible), nameof(Step2HeaderActionLabel))]
    private bool _isStep2Complete;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsStep2Expanded), nameof(IsStep2CollapsedSummaryVisible), nameof(IsStep2BodyVisible), nameof(Step2HeaderActionLabel))]
    private bool _isStep2UserExpanded;

    [ObservableProperty]
    private string _step2SummaryText = string.Empty;

    public MultistreamPageViewModel(
        SettingsPageViewModel settingsPage,
        MultistreamTileViewModel status,
        IEnumerable<IMultistreamPlatformControls> platformControls,
        PlatformAccountsSectionViewModel? accounts = null,
        YouTubeSettingsStore? youtubeSettingsStore = null,
        ChannelCatalogStore? catalogStore = null)
    {
        ArgumentNullException.ThrowIfNull(settingsPage);
        ArgumentNullException.ThrowIfNull(status);
        ArgumentNullException.ThrowIfNull(platformControls);
        Settings = settingsPage;
        Status = status;
        Accounts = accounts;
        _catalogStore = catalogStore;

        Settings.PropertyChanged += OnSettingsPropertyChanged;
        Settings.SettingsSaved += OnSettingsSaved;
        var (twitchDraft, youtubeDraft) = settingsPage.GetCredentialDrafts();
        if (twitchDraft is not null)
        {
            twitchDraft.SettingChanged += OnCredentialDraftChanged;
        }

        if (youtubeDraft is not null)
        {
            youtubeDraft.SettingChanged += OnCredentialDraftChanged;
        }

        status.ApplyPlatformControls(platformControls);
        var controls = platformControls.ToDictionary(item => item.Platform);
        Cards = [.. status.Rows.Select(row => new MultistreamPlatformCard(row, controls.GetValueOrDefault(row.Platform)))];
        Catalog = Cards.Count > 0 ? settingsPage.FindSectionContent<ChannelCatalogSettingsSectionViewModel>() : null;
        if (Catalog is not null)
        {
            Catalog.PropertyChanged += OnCatalogPropertyChanged;
            Catalog.AccountsChanged += OnCatalogAccountsChanged;
            status.ChannelStatesRefreshed += RefreshChannelCards;
        }

        if (Accounts is not null)
        {
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
        RefreshStepStates();
    }

    internal event EventHandler? SectionRequested;

    public string PageTitle => "Каналы и профили";

    public string? PageDescription => null;

    public SettingsPageViewModel Settings { get; }

    public MultistreamTileViewModel Status { get; }

    public ChannelCatalogSettingsSectionViewModel? Catalog { get; }

    public PlatformAccountsSectionViewModel? Accounts { get; }

    public IReadOnlyList<MultistreamPlatformCard> Cards { get; }

    public IReadOnlyList<MultistreamAutomationTabOption> AutomationTabOptions { get; } =
    [
        new(MultistreamAutomationTab.Profiles, new SegmentOption(PackIconLucideKind.ListChecks, "Профили трансляции"), "MultistreamAutoTabProfiles"),
        new(MultistreamAutomationTab.Automation, new SegmentOption(PackIconLucideKind.Bot, "Автоматика"), "MultistreamAutoTabAutomation"),
        new(MultistreamAutomationTab.Groups, new SegmentOption(PackIconLucideKind.Users, "Группы каналов"), "MultistreamAutoTabGroups"),
    ];

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

    public bool HasStep1 => HasPlatformSettings;

    public bool HasStep2 => HasPlatformAccounts;

    public bool IsStep1Expanded => HasStep1 && (!IsStep1Complete || IsStep1UserExpanded);

    public bool IsStep1CollapsedSummaryVisible => HasStep1 && IsStep1Complete && !IsStep1Expanded;

    public bool IsStep1BodyVisible => HasStep1 && IsStep1Expanded;

    public string Step1HeaderActionLabel => IsStep1Expanded ? "Свернуть" : "Изменить";

    public bool IsStep2Expanded => HasStep2 && (!IsStep2Complete || IsStep2UserExpanded);

    public bool IsStep2CollapsedSummaryVisible => HasStep2 && IsStep2Complete && !IsStep2Expanded;

    public bool IsStep2BodyVisible => HasStep2 && IsStep2Expanded;

    public string Step2HeaderActionLabel => IsStep2Expanded ? "Свернуть" : "Изменить";

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

    public string UnsavedChangesSubject => "На странице «Каналы и профили» есть несохранённые изменения.";

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
        Accounts?.RefreshCredentialDependency();
        IsStep1UserExpanded = false;
        IsStep2UserExpanded = false;
        RefreshStepStates();
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
    }

    [RelayCommand]
    public void ToggleStep1()
    {
        if (IsStep1Complete)
        {
            IsStep1UserExpanded = !IsStep1UserExpanded;
        }
    }

    [RelayCommand]
    public void ToggleStep2()
    {
        if (IsStep2Complete)
        {
            IsStep2UserExpanded = !IsStep2UserExpanded;
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
        IsStep1UserExpanded = true;
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
        if (HasPlatformAccounts)
        {
            IsStep2UserExpanded = true;
            Reveal(MultistreamSection.Accounts);
        }
        else
        {
            Reveal(MultistreamSection.BotAuthorization);
        }
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
            if (HasStep1 && !IsStep1Complete)
            {
                IsStep1UserExpanded = true;
                Reveal(MultistreamSection.Parameters);
            }
            else if (card.Status.IsBlockedByDebugSession)
            {
                Reveal(MultistreamSection.Platforms);
            }
            else if (HasStep2 && (!IsStep2Complete || card.Status.NeedsBotAuthorization))
            {
                IsStep2UserExpanded = true;
                Reveal(MultistreamSection.Accounts);
            }
            else if (card.Status.NeedsBotAuthorization)
            {
                Reveal(MultistreamSection.BotAuthorization);
            }
            else
            {
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

    internal void OpenProfileTargets(Guid? profileId)
    {
        OpenAutomation();
        SelectAutomationTab(MultistreamAutomationTab.Profiles);
        if (profileId is { } id
            && ProfilesBlocks is [{ Content: BroadcastProfilesSettingsSectionViewModel section }])
        {
            section.Profiles.OpenEditor(id);
        }
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

        IsStep1UserExpanded = false;
        IsStep2UserExpanded = false;

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
                Accounts.Load();
            }
        }

        SyncCardSelection();
        RefreshStepStates();
    }

    private void OnSettingsPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (string.IsNullOrEmpty(e.PropertyName) || e.PropertyName == nameof(SettingsPageViewModel.HasChanges))
        {
            RefreshStepStates();
        }
    }

    private void OnSettingsSaved(object? sender, EventArgs e)
    {
        Accounts?.RefreshCredentialDependency();
        IsStep1UserExpanded = false;
        RefreshStepStates();
    }

    private void OnCredentialDraftChanged(object? sender, EventArgs e)
    {
        RefreshStepStates();
    }

    private void OnCatalogAccountsChanged(object? sender, StreamingPlatform platform)
    {
        Accounts?.RefreshIfPlatform(platform);
        RefreshStepStates();
    }

    private void OnAccountsSectionChanged(object? sender, StreamingPlatform platform)
    {
        Catalog?.RefreshAccountOptions(platform);
        RefreshStepStates();
    }

    private void OnAccountsPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (string.IsNullOrEmpty(e.PropertyName)
            || e.PropertyName is nameof(PlatformAccountsSectionViewModel.AccountCount)
                or nameof(PlatformAccountsSectionViewModel.HasAccounts)
                or nameof(PlatformAccountsSectionViewModel.Step1DependencyNotice))
        {
            OnPropertyChanged(nameof(SelectedPlatformAccountCount));
            OnPropertyChanged(nameof(HasSelectedPlatformAccounts));
            RefreshStepStates();
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
            RefreshStepStates();
        }
    }

    private void OnSelectedCardControlsChanged(object? sender, EventArgs e)
    {
        NotifyHeaderActionsChanged();
        RefreshStepStates();
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
            RefreshStepStates();
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

    internal void RefreshStepStates()
    {
        if (SelectedCard is not { } card)
        {
            IsStep1Complete = false;
            Step1SummaryText = string.Empty;
            IsStep2Complete = false;
            Step2SummaryText = string.Empty;
            if (Catalog is not null)
            {
                Catalog.StepDependencyNotice = null;
            }

            return;
        }

        var platform = card.Status.Platform;
        var (step1Ready, step1Summary, step1Missing) = HasStep1
            ? Settings.EvaluateStep1Readiness(platform)
            : (true, null, null);
        if (HasStep1 && card.Controls?.RequiresCardSetup == true && step1Ready)
        {
            step1Ready = false;
            step1Missing = "Сохраните включение и параметры площадки в Шаге 1 («Доступ площадки») выше.";
        }

        IsStep1Complete = step1Ready;
        Step1SummaryText = step1Summary ?? "Доступ настроен";

        var (hasBotReady, hasBroadcasterScopeWarning, step2Summary) = ResolveAccountReadiness(card);
        var step2Ready = !HasStep2 || (step1Ready && hasBotReady && !hasBroadcasterScopeWarning);
        IsStep2Complete = step2Ready;
        Step2SummaryText = step2Summary;

        if (Catalog is not null && Catalog.SelectedPlatform == platform)
        {
            if (HasStep1 && !step1Ready)
            {
                Catalog.StepDependencyNotice = step1Missing
                    ?? Accounts?.Step1DependencyNotice
                    ?? "Сначала заполните и сохраните Шаг 1 («Доступ площадки») выше, чтобы добавлять каналы и назначать роли.";
            }
            else if (HasStep2 && !step2Ready)
            {
                Catalog.StepDependencyNotice = hasBroadcasterScopeWarning
                    ? "Проверьте права стримера на Шаге 2 («Аккаунты и проверка прав») выше."
                    : "Сначала войдите в аккаунт на Шаге 2 («Аккаунты и проверка прав») выше, чтобы назначать роли и проверять доступ.";
            }
            else
            {
                Catalog.StepDependencyNotice = null;
            }
        }
    }

    private (bool HasBotReady, bool HasBroadcasterScopeWarning, string Summary) ResolveAccountReadiness(
        MultistreamPlatformCard card)
    {
        var platform = card.Status.Platform;
        if (card.Status.NeedsBotAuthorization)
        {
            return (false, false, string.Empty);
        }

        if (Accounts is not null && Accounts.SupportsAccountsFor(platform) && Accounts.SelectedPlatform == platform)
        {
            var botAccount = Accounts.Accounts.FirstOrDefault(static account =>
                account.HasBotRole && account.BotBadgeSeverity is StatusSeverity.Success or StatusSeverity.Info);
            var broadcasterAccount = Accounts.Accounts.FirstOrDefault(static account =>
                account.HasBroadcasterRole && account.BroadcasterBadgeSeverity is StatusSeverity.Success or StatusSeverity.Info);
            var hasBot = botAccount is not null;
            var hasBroadcaster = broadcasterAccount is not null;
            var activePlatformChannel = _catalogStore?.GetActiveChannel(platform);
            var activeBroadcasterId = activePlatformChannel?.GetAccountAssignment(PlatformAuthRole.Broadcaster)?.Account.AccountId;
            var activeBroadcasterContextId = activePlatformChannel?.BroadcasterContextId;
            var relevantAccounts = !string.IsNullOrWhiteSpace(activeBroadcasterId)
                ? Accounts.Accounts.Where(a => string.Equals(a.AccountId, activeBroadcasterId, StringComparison.Ordinal))
                : Accounts.Accounts;

            var hasBroadcasterScopeWarning = relevantAccounts.Any(account =>
                account.Grants.Any(grant =>
                    grant.Role == PlatformAuthRole.Broadcaster
                    && grant.TokenStatusSeverity == StatusSeverity.Warning
                    && (string.IsNullOrWhiteSpace(activeBroadcasterContextId)
                        || string.Equals(grant.ContextId, activeBroadcasterContextId, StringComparison.Ordinal))));

            var summary = hasBroadcaster && broadcasterAccount is not null && botAccount is not null
                ? ReferenceEquals(botAccount, broadcasterAccount)
                    ? $"Бот и стример: {botAccount.DisplayName}"
                    : $"Бот: {botAccount.DisplayName}, стример: {broadcasterAccount.DisplayName}"
                : botAccount is not null
                    ? $"Бот: {botAccount.DisplayName}"
                    : "Аккаунты готовы";
            return (hasBot, hasBroadcasterScopeWarning, summary);
        }

        return (true, false, "Аккаунты готовы");
    }

    partial void OnIsAutomationSelectedChanged(bool value) => SyncCardSelection();

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
