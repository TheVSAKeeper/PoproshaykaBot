using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PoproshaykaBot.Core.Platforms;
using PoproshaykaBot.Wpf.ViewModels.Settings;
using PoproshaykaBot.Wpf.ViewModels.Tiles;

namespace PoproshaykaBot.Wpf.ViewModels;

public enum MultistreamPlatformTab
{
    Channels = 0,
    Parameters = 1,
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
    private MultistreamSection _requestedSection;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SelectedPlatform), nameof(HasSelectedPlatform), nameof(IsPlatformSelected),
        nameof(SelectedPlatformCard), nameof(PlatformSettings), nameof(HasPlatformSettings),
        nameof(PlatformSettingsTitle), nameof(EditorTitle), nameof(ShowParametersButtonInHeader),
        nameof(ShowBotAuthButtonInHeader), nameof(HeaderSetupActionLabel),
        nameof(SelectedPlatformChannelCount), nameof(HasSelectedPlatformChannels))]
    private MultistreamPlatformCard? _selectedCard;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsPlatformSelected), nameof(SelectedPlatformCard), nameof(IsAutomationExpanded))]
    private bool _isAutomationSelected;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsChannelsTabSelected), nameof(IsParametersTabSelected))]
    private MultistreamPlatformTab _selectedPlatformTab = MultistreamPlatformTab.Channels;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsProfilesTabSelected), nameof(IsAutomationTabSelected), nameof(IsGroupsTabSelected))]
    private MultistreamAutomationTab _selectedAutomationTab = MultistreamAutomationTab.Profiles;

    public MultistreamPageViewModel(
        SettingsPageViewModel settingsPage,
        MultistreamTileViewModel status,
        IEnumerable<IMultistreamPlatformControls> platformControls)
    {
        ArgumentNullException.ThrowIfNull(settingsPage);
        ArgumentNullException.ThrowIfNull(status);
        ArgumentNullException.ThrowIfNull(platformControls);
        Settings = settingsPage;
        Status = status;

        status.ApplyPlatformControls(platformControls);
        var controls = platformControls.ToDictionary(item => item.Platform);
        Cards = [.. status.Rows.Select(row => new MultistreamPlatformCard(row, controls.GetValueOrDefault(row.Platform)))];
        Catalog = Cards.Count > 0 ? settingsPage.GetSectionContent<ChannelCatalogSettingsSectionViewModel>() : null;
        if (Catalog is not null)
        {
            Catalog.PropertyChanged += OnCatalogPropertyChanged;
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
    }

    internal event EventHandler? SectionRequested;

    public string PageTitle => "Мультистрим";

    public string? PageDescription => null;

    public SettingsPageViewModel Settings { get; }

    public MultistreamTileViewModel Status { get; }

    public ChannelCatalogSettingsSectionViewModel? Catalog { get; }

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

    public IReadOnlyList<SettingsSectionBlockDescriptor> AutomationSettings { get; }

    public IReadOnlyList<SettingsSectionBlockDescriptor> ProfilesBlocks { get; }

    public IReadOnlyList<SettingsSectionBlockDescriptor> LifecycleAndBroadcastBlocks { get; }

    public IReadOnlyList<SettingsSectionBlockDescriptor> AutomationBlocks => LifecycleAndBroadcastBlocks;

    public IReadOnlyList<SettingsSectionBlockDescriptor> ChannelGroupsBlocks { get; }

    public string? PlatformSettingsTitle => SelectedCard is { } card ? $"Параметры {card.Status.DisplayName}" : null;

    public string? EditorTitle => SelectedCard is { } card ? $"Каналы {card.Status.DisplayName}" : null;

    public int SelectedPlatformChannelCount => Catalog?.VisibleChannelCount ?? 0;

    public bool HasSelectedPlatformChannels => SelectedPlatformChannelCount > 0;

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

        Settings.ReloadDraft();
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
        if (!HasPlatformSettings && SelectedPlatformTab != MultistreamPlatformTab.Channels)
        {
            SelectedPlatformTab = MultistreamPlatformTab.Channels;
        }
    }

    [RelayCommand]
    public void SelectPlatformTab(MultistreamPlatformTab tab)
    {
        SelectedPlatformTab = tab == MultistreamPlatformTab.Parameters && !HasPlatformSettings
            ? MultistreamPlatformTab.Channels
            : tab;
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
        }

        if (!HasPlatformSettings && SelectedPlatformTab != MultistreamPlatformTab.Channels)
        {
            SelectedPlatformTab = MultistreamPlatformTab.Channels;
        }

        SyncCardSelection();
    }

    private void OnSelectedCardStatusChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (string.IsNullOrEmpty(e.PropertyName)
            || e.PropertyName is nameof(MultistreamPlatformRowViewModel.ShowConnectionAction)
                or nameof(MultistreamPlatformRowViewModel.NeedsBotAuthorization))
        {
            NotifyHeaderActionsChanged();
        }
    }

    private void OnSelectedCardControlsChanged(object? sender, EventArgs e) => NotifyHeaderActionsChanged();

    private void OnCatalogPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (string.IsNullOrEmpty(e.PropertyName)
            || e.PropertyName is nameof(ChannelCatalogSettingsSectionViewModel.VisibleChannelCount)
                or nameof(ChannelCatalogSettingsSectionViewModel.HasChannels))
        {
            OnPropertyChanged(nameof(SelectedPlatformChannelCount));
            OnPropertyChanged(nameof(HasSelectedPlatformChannels));
        }
    }

    private void NotifyHeaderActionsChanged()
    {
        OnPropertyChanged(nameof(ShowParametersButtonInHeader));
        OnPropertyChanged(nameof(ShowBotAuthButtonInHeader));
        OnPropertyChanged(nameof(HeaderSetupActionLabel));
    }

    partial void OnIsAutomationSelectedChanged(bool value) => SyncCardSelection();

    partial void OnSelectedPlatformTabChanged(MultistreamPlatformTab value)
    {
        if (value == MultistreamPlatformTab.Parameters && !HasPlatformSettings)
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
    public void DiscardUnsavedChanges() => OnEnter();
}
