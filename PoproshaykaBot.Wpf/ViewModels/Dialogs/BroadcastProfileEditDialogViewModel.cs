using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using PoproshaykaBot.Core.Broadcast.Profiles;
using PoproshaykaBot.Core.Chat;
using PoproshaykaBot.Core.Obs;
using PoproshaykaBot.Core.Platforms;
using PoproshaykaBot.Core.Settings.Stores;
using PoproshaykaBot.Wpf.Bootstrap;
using PoproshaykaBot.Wpf.ViewModels.Controls;
using System.Collections.ObjectModel;
using System.Globalization;

namespace PoproshaykaBot.Wpf.ViewModels.Dialogs;

public sealed partial class BroadcastProfileEditDialogViewModel : ObservableObject, IDialogViewModel, IAcceptableDialog, IDisposable
{
    private const string YouTubeWriteReadyHint = "Права записи YouTube активны – название и описание обновятся автоматически";
    private const string YouTubeWriteMissingHint = "Нет прав записи владельца YouTube – при применении обновится только Twitch, а YouTube покажет частичный статус";

    private readonly ObsIntegrationService _obsIntegration;
    private readonly ObsIntegrationStore _obsIntegrationStore;
    private readonly IChatPlatformRegistry _platformRegistry;
    private readonly ChannelCatalogStore? _catalogStore;
    private readonly YouTubeAccountsStore? _youtubeAccountsStore;
    private readonly ILogger<BroadcastProfileEditDialogViewModel> _logger;

    private bool _nameRequired = true;
    private bool _isCurrentSettingsMode;
    private bool _sceneBindingEnabled = true;
    private string _originalLanguage = "ru";

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ConfirmCommand))]
    [NotifyPropertyChangedFor(nameof(ConfirmButtonText))]
    [NotifyPropertyChangedFor(nameof(IsPlatformsSelectionEnabled))]
    private string _name = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Preview))]
    [NotifyPropertyChangedFor(nameof(HasPreview))]
    private string _profileTitle = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Preview))]
    [NotifyPropertyChangedFor(nameof(HasPreview))]
    private int _currentNumber = 1;

    [ObservableProperty]
    private string _tags = string.Empty;

    [ObservableProperty]
    private string _language = "ru";

    [ObservableProperty]
    private string _obsSceneName = string.Empty;

    [ObservableProperty]
    private ObservableCollection<string> _obsScenes = [];

    [ObservableProperty]
    private string _youTubeTitleOverride = string.Empty;

    [ObservableProperty]
    private string _youTubeDescription = string.Empty;

    [ObservableProperty]
    private string _youTubePrivacyStatus = YouTubeBroadcastPrivacyStatus.Unchanged;

    [ObservableProperty]
    private string _youTubeWriteAccessHint = YouTubeWriteMissingHint;

    [ObservableProperty]
    private bool _hasYouTubeWriteAccessWarning = true;

    public BroadcastProfileEditDialogViewModel(
        GameAutocompleteViewModel game,
        ObsIntegrationService obsIntegration,
        ObsIntegrationStore obsIntegrationStore,
        ILogger<BroadcastProfileEditDialogViewModel> logger,
        IChatPlatformRegistry platformRegistry,
        ChannelCatalogStore? catalogStore = null,
        YouTubeAccountsStore? youtubeAccountsStore = null)
    {
        Game = game;
        _obsIntegration = obsIntegration;
        _obsIntegrationStore = obsIntegrationStore;
        _logger = logger;
        _platformRegistry = platformRegistry;
        _catalogStore = catalogStore;
        _youtubeAccountsStore = youtubeAccountsStore;
        InitializeTargetPlatforms(null);
        RefreshYouTubeWriteAccessState();
    }

    public event EventHandler<bool>? RequestClose;

    public string Title { get; private set; } = "Редактировать профиль";
    public string ConfirmButtonText => _isCurrentSettingsMode
        ? NewProfileName.Length > 0 ? "Сохранить и применить" : "Применить"
        : "OK";
    public string NameLabel { get; private set; } = "Имя:";
    public string? NamePlaceholder { get; private set; }
    public bool IsObsSceneVisible { get; private set; } = true;

    public bool IsPlatformsSelectionEnabled => !_isCurrentSettingsMode || NewProfileName.Length > 0;

    public bool IsTwitchSelected =>
        TargetPlatforms.Count == 0
        || TargetPlatforms.Any(static option => option.Platform == StreamingPlatform.Twitch && option.IsSelected);

    public bool IsYouTubeSelected =>
        TargetPlatforms.Any(static option => option.Platform == StreamingPlatform.YouTube && option.IsSelected);

    public ObservableCollection<ProfilePlatformOptionViewModel> TargetPlatforms { get; } = [];

    public GameAutocompleteViewModel Game { get; }

    public IReadOnlyList<string> LanguageOptions { get; } = ["ru", "en"];

    public IReadOnlyList<YouTubePrivacyStatusOption> YouTubePrivacyOptions { get; } =
    [
        new(YouTubeBroadcastPrivacyStatus.Unchanged, "Не менять"),
        new(YouTubeBroadcastPrivacyStatus.Public, "Открытый"),
        new(YouTubeBroadcastPrivacyStatus.Unlisted, "По ссылке"),
        new(YouTubeBroadcastPrivacyStatus.Private, "Ограниченный"),
    ];

    public string? Preview
    {
        get
        {
            var template = MessageTemplate.For(ProfileTitle);
            if (!template.Contains("n"))
                return null;
            return $"Текущая серия в эфире: {template.With("n", CurrentNumber.ToString(CultureInfo.InvariantCulture)).Render()}";
        }
    }

    public bool HasPreview => Preview is not null;

    public string NewProfileName => Name.Trim();

    public void LoadFrom(
        BroadcastProfile profile,
        IReadOnlySet<StreamingPlatform>? platforms = null,
        BroadcastProfileYouTubeMetadata? youtubeMetadata = null)
    {
        Name = profile.Name;
        ProfileTitle = profile.Title;
        CurrentNumber = Math.Clamp(profile.CurrentNumber, 1, 1_000_000);
        Game.SetSelected(profile.GameId ?? string.Empty, profile.GameName ?? string.Empty);
        Tags = string.Join(", ", profile.Tags);
        ObsSceneName = profile.ObsSceneName;
        _originalLanguage = profile.BroadcasterLanguage ?? "ru";
        Language = _originalLanguage;
        YouTubeTitleOverride = youtubeMetadata?.TitleOverride ?? string.Empty;
        YouTubeDescription = youtubeMetadata?.Description ?? string.Empty;
        YouTubePrivacyStatus = YouTubeBroadcastPrivacyStatus.Normalize(youtubeMetadata?.PrivacyStatus)
            ?? YouTubeBroadcastPrivacyStatus.Unchanged;
        InitializeTargetPlatforms(platforms);
        RefreshYouTubeWriteAccessState();
    }

    public IReadOnlyCollection<StreamingPlatform> GetSelectedPlatforms()
    {
        return TargetPlatforms
            .Where(static option => option.IsSelected)
            .Select(static option => option.Platform)
            .ToArray();
    }

    public BroadcastProfile BuildProfile()
    {
        var profile = new BroadcastProfile();
        WriteTo(profile);
        return profile;
    }

    public BroadcastProfileYouTubeMetadata BuildYouTubeMetadata()
    {
        return new BroadcastProfileYouTubeMetadata
        {
            TitleOverride = string.IsNullOrWhiteSpace(YouTubeTitleOverride) ? null : YouTubeTitleOverride.Trim(),
            Description = string.IsNullOrWhiteSpace(YouTubeDescription) ? null : YouTubeDescription.Trim(),
            PrivacyStatus = YouTubeBroadcastPrivacyStatus.Normalize(YouTubePrivacyStatus),
        };
    }

    private void InitializeTargetPlatforms(IReadOnlySet<StreamingPlatform>? selected)
    {
        foreach (var existing in TargetPlatforms)
        {
            existing.PropertyChanged -= OnTargetPlatformPropertyChanged;
        }

        var registered = _platformRegistry.All.Select(static platform => platform.Id);
        var platforms = (selected is null ? registered : registered.Union(selected)).Order().ToArray();

        TargetPlatforms.Clear();
        foreach (var platform in platforms)
        {
            var isSelected = selected?.Contains(platform) ?? platform == platforms[0];
            var option = new ProfilePlatformOptionViewModel(platform, _platformRegistry.GetDisplayName(platform), isSelected);
            option.PropertyChanged += OnTargetPlatformPropertyChanged;
            TargetPlatforms.Add(option);
        }

        OnPropertyChanged(nameof(IsTwitchSelected));
        OnPropertyChanged(nameof(IsYouTubeSelected));
        ConfirmCommand.NotifyCanExecuteChanged();
    }

    private void OnTargetPlatformPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ProfilePlatformOptionViewModel.IsSelected))
        {
            OnPropertyChanged(nameof(IsTwitchSelected));
            OnPropertyChanged(nameof(IsYouTubeSelected));
            RefreshYouTubeWriteAccessState();
            ConfirmCommand.NotifyCanExecuteChanged();
        }
    }

    private void RefreshYouTubeWriteAccessState()
    {
        var hasWriteAccess = YouTubeBroadcastWriteAccess.CanUpdateBroadcastMetadata(_catalogStore, _youtubeAccountsStore);
        HasYouTubeWriteAccessWarning = !hasWriteAccess;
        YouTubeWriteAccessHint = hasWriteAccess ? YouTubeWriteReadyHint : YouTubeWriteMissingHint;
    }

    public void ConfigureCurrentSettingsMode()
    {
        Title = "Текущие настройки эфира";
        _nameRequired = false;
        Name = string.Empty;
        NameLabel = "Сохранить как:";
        NamePlaceholder = "имя нового профиля (необязательно)";
        _isCurrentSettingsMode = true;
        IsObsSceneVisible = false;
        _sceneBindingEnabled = false;
        OnPropertyChanged(nameof(IsPlatformsSelectionEnabled));
    }

    public void WriteTo(BroadcastProfile profile)
    {
        if (_nameRequired)
            profile.Name = Name.Trim();

        profile.Title = ProfileTitle.Trim();
        profile.CurrentNumber = CurrentNumber;

        if (Game.Selected is { } selected)
        {
            profile.GameId = selected.Id;
            profile.GameName = selected.Name;
        }

        profile.Tags = Tags
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .ToList();

        profile.BroadcasterLanguage = string.IsNullOrEmpty(Language) ? _originalLanguage : Language;

        if (_sceneBindingEnabled)
            profile.ObsSceneName = ObsSceneName.Trim();
    }

    public async Task LoadObsScenesAsync()
    {
        if (!_sceneBindingEnabled)
            return;

        var settings = _obsIntegrationStore.Load();
        if (!settings.Enabled)
            return;

        try
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            var scenes = await _obsIntegration.ListScenesAsync(settings, cts.Token);

            if (scenes.Count == 0)
                return;

            var current = ObsSceneName;
            ObsScenes = [.. scenes];
            ObsSceneName = current;
        }
        catch (Exception ex)
        {
            _logger.ObsScenesLoadFailed(ex);
        }
    }

    public bool TryAccept()
    {
        if (!CanConfirm())
            return false;

        RequestClose?.Invoke(this, true);
        return true;
    }

    public void Dispose()
    {
        foreach (var option in TargetPlatforms)
        {
            option.PropertyChanged -= OnTargetPlatformPropertyChanged;
        }

        Game.Dispose();
    }

    private bool CanConfirm() =>
        (!_nameRequired || Name.Trim().Length > 0)
        && (!IsPlatformsSelectionEnabled || TargetPlatforms.Any(static option => option.IsSelected));

    [RelayCommand(CanExecute = nameof(CanConfirm))]
    private void Confirm()
    {
        RequestClose?.Invoke(this, true);
    }

    [RelayCommand]
    private void Cancel()
    {
        RequestClose?.Invoke(this, false);
    }

    [RelayCommand]
    private void IncreaseNumber()
    {
        if (CurrentNumber < 1_000_000)
            CurrentNumber++;
    }

    [RelayCommand]
    private void DecreaseNumber()
    {
        if (CurrentNumber > 1)
            CurrentNumber--;
    }
}

public sealed record YouTubePrivacyStatusOption(string Value, string DisplayName);

public sealed partial class ProfilePlatformOptionViewModel : ObservableObject
{
    public ProfilePlatformOptionViewModel(StreamingPlatform platform, string displayName, bool isSelected)
    {
        Platform = platform;
        DisplayName = displayName;
        _isSelected = isSelected;
    }

    public StreamingPlatform Platform { get; }
    public string DisplayName { get; }

    [ObservableProperty]
    private bool _isSelected;
}
