using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PoproshaykaBot.Core.Platforms;
using PoproshaykaBot.Wpf.ViewModels.Settings;
using PoproshaykaBot.Wpf.ViewModels.Tiles;

namespace PoproshaykaBot.Wpf.ViewModels;

internal enum MultistreamSection
{
    None = 0,
    Channels = 1,
    Automation = 2,
    Platforms = 3,
    BotAuthorization = 4,
}

public sealed partial class MultistreamPlatformCard(MultistreamPlatformRowViewModel status, IMultistreamPlatformControls? controls)
    : ObservableObject
{
    public MultistreamPlatformRowViewModel Status { get; } = status;
    public IMultistreamPlatformControls? Controls { get; } = controls;

    [ObservableProperty]
    private bool _isSelected;
}

public sealed partial class MultistreamPageViewModel : ObservableObject, IPageHeader, IUnsavedChangesPage
{
    private readonly IReadOnlyDictionary<StreamingPlatform, IReadOnlyList<SettingsSectionBlockDescriptor>> _platformSettings;
    private MultistreamSection _requestedSection;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SelectedPlatform), nameof(HasSelectedPlatform), nameof(PlatformSettings),
        nameof(HasPlatformSettings), nameof(PlatformSettingsTitle), nameof(EditorTitle))]
    private MultistreamPlatformCard? _selectedCard;

    [ObservableProperty]
    private bool _isAutomationExpanded;

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

        var controls = platformControls.ToDictionary(item => item.Platform);
        Cards = [.. status.Rows.Select(row => new MultistreamPlatformCard(row, controls.GetValueOrDefault(row.Platform)))];
        Catalog = Cards.Count > 0 ? settingsPage.GetSectionContent<ChannelCatalogSettingsSectionViewModel>() : null;

        var presentations = settingsPage.SectionPresentations
            .Where(presentation => presentation.Section.Key == PlatformSettingsPages.SectionKey)
            .ToArray();
        _platformSettings = presentations
            .Select(presentation => (Platform: PlatformSettingsPages.GetPlatform(presentation.Group), presentation.Blocks))
            .Where(presentation => presentation.Platform.HasValue)
            .ToDictionary(
                presentation => presentation.Platform.GetValueOrDefault(),
                presentation => WithoutCatalog(presentation.Blocks));
        AutomationSettings = presentations.FirstOrDefault(p => p.Group?.Key == PlatformSettingsPages.MultistreamKey)?.Blocks ?? [];
        SelectedCard = Cards.Count > 0 ? Cards[0] : null;
    }

    internal event EventHandler? SectionRequested;

    public string PageTitle => "Мультистрим";

    public string? PageDescription => null;

    public SettingsPageViewModel Settings { get; }

    public MultistreamTileViewModel Status { get; }

    public ChannelCatalogSettingsSectionViewModel? Catalog { get; }

    public IReadOnlyList<MultistreamPlatformCard> Cards { get; }

    public StreamingPlatform? SelectedPlatform => SelectedCard?.Status.Platform;

    public bool HasSelectedPlatform => SelectedCard is not null;

    public IReadOnlyList<SettingsSectionBlockDescriptor> PlatformSettings =>
        SelectedPlatform is { } platform ? _platformSettings.GetValueOrDefault(platform) ?? [] : [];

    public bool HasPlatformSettings => PlatformSettings.Count > 0;

    public IReadOnlyList<SettingsSectionBlockDescriptor> AutomationSettings { get; }

    public string? PlatformSettingsTitle => SelectedCard is { } card ? $"Параметры {card.Status.DisplayName}" : null;

    public string? EditorTitle => SelectedCard is { } card ? $"Каналы {card.Status.DisplayName}" : null;

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
    public void OpenPlatform(StreamingPlatform platform)
    {
        if (Settings.IsSaving)
        {
            return;
        }

        var card = Cards.FirstOrDefault(candidate => candidate.Status.Platform == platform);
        if (card is not null)
        {
            SelectedCard = card;
            Catalog?.SelectActiveChannel();
            Reveal(card.Controls?.RequiresCardSetup == true || card.Status.IsBlockedByDebugSession
                ? MultistreamSection.Platforms
                : card.Status.NeedsBotAuthorization
                    ? MultistreamSection.BotAuthorization
                    : MultistreamSection.Channels);
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

        IsAutomationExpanded = true;
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
            oldValue.IsSelected = false;
        }

        if (newValue is not null)
        {
            newValue.IsSelected = true;
            if (Catalog is not null)
            {
                Catalog.SelectedPlatform = newValue.Status.Platform;
            }
        }
    }

    public Task<bool> TrySaveUnsavedChangesAsync() => Settings.TrySaveUnsavedChangesAsync();

    [RelayCommand]
    public void DiscardUnsavedChanges() => OnEnter();
}
