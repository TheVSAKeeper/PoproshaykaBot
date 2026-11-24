using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PoproshaykaBot.Core.Broadcast.Profiles;
using PoproshaykaBot.Core.Chat;
using PoproshaykaBot.Core.Platforms;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace PoproshaykaBot.Wpf.ViewModels.Tiles;

public sealed partial class BroadcastProfileItemViewModel : ObservableObject
{
    private readonly Func<BroadcastProfileItemViewModel, Task> _applyAsync;
    private readonly Func<BroadcastProfileItemViewModel, int, Task> _adjustNumberAsync;
    private readonly Func<BroadcastProfileItemViewModel, Task> _editAsync;
    private readonly Action<BroadcastProfileItemViewModel> _duplicate;
    private readonly Action<BroadcastProfileItemViewModel> _delete;

    public BroadcastProfileItemViewModel(
        BroadcastProfile profile,
        bool isActive,
        bool hasDrift,
        Func<BroadcastProfileItemViewModel, Task> applyAsync,
        Func<BroadcastProfileItemViewModel, int, Task> adjustNumberAsync,
        Func<BroadcastProfileItemViewModel, Task> editAsync,
        Action<BroadcastProfileItemViewModel> duplicate,
        Action<BroadcastProfileItemViewModel> delete,
        IReadOnlyList<BroadcastProfileTarget> targetPlatforms,
        BroadcastProfileYouTubeMetadata? youtubeMetadata = null,
        BroadcastProfileSyncSummary? syncSummary = null,
        Func<StreamingPlatform, string>? getPlatformName = null)
    {
        Profile = profile;
        _isActive = isActive;
        _syncState = ResolveInitialSyncState(isActive, hasDrift, syncSummary);
        _hasDrift = isActive && _syncState == BroadcastProfileSyncState.Drifted;
        DriftedPlatformNames = isActive && syncSummary is not null
            ? syncSummary.DriftedPlatforms.Select(p => getPlatformName?.Invoke(p) ?? p.ToString()).ToArray()
            : [];
        PendingPlatformNames = isActive && syncSummary is not null
            ? syncSummary.PendingPlatforms.Select(p => getPlatformName?.Invoke(p) ?? p.ToString()).ToArray()
            : [];
        _applyAsync = applyAsync;
        _adjustNumberAsync = adjustNumberAsync;
        _editAsync = editAsync;
        _duplicate = duplicate;
        _delete = delete;

        Name = profile.Name;

        var hasNumber = MessageTemplate.For(profile.Title).Contains("n")
            || MessageTemplate.For(youtubeMetadata?.TitleOverride ?? string.Empty).Contains("n")
            || MessageTemplate.For(youtubeMetadata?.Description ?? string.Empty).Contains("n");
        HasNumberPlaceholder = hasNumber;
        NumberBadge = hasNumber ? $"#{profile.CurrentNumber}" : string.Empty;

        var displayTitle = !string.IsNullOrWhiteSpace(profile.Title)
            ? profile.Title
            : youtubeMetadata?.TitleOverride ?? string.Empty;
        TitleDisplay = string.IsNullOrWhiteSpace(displayTitle)
            ? string.Empty
            : $"«{displayTitle}»";
        HasTitleDisplay = !string.IsNullOrWhiteSpace(displayTitle);

        var isYouTubeOnly = targetPlatforms.Any(static t => t.Platform == StreamingPlatform.YouTube)
            && !targetPlatforms.Any(static t => t.Platform == StreamingPlatform.Twitch);

        var parts = new List<string>();
        if (!string.IsNullOrWhiteSpace(profile.GameName))
        {
            parts.Add(profile.GameName);
        }
        else if (isYouTubeOnly)
        {
            var privacy = YouTubeBroadcastPrivacyStatus.Normalize(youtubeMetadata?.PrivacyStatus);
            if (!string.IsNullOrWhiteSpace(privacy))
            {
                parts.Add(YouTubeBroadcastPrivacyStatus.GetDisplayName(privacy));
            }
            else if (!string.IsNullOrWhiteSpace(youtubeMetadata?.Description))
            {
                parts.Add("Описание YouTube");
            }
            else
            {
                parts.Add("YouTube");
            }
        }

        if (profile.Tags.Count > 0)
            parts.Add(string.Join(" ", profile.Tags.Select(t => "#" + t)));
        if (!string.IsNullOrWhiteSpace(profile.ObsSceneName))
            parts.Add(profile.ObsSceneName);
        if (!isYouTubeOnly && !string.IsNullOrWhiteSpace(profile.BroadcasterLanguage))
            parts.Add(profile.BroadcasterLanguage.ToUpperInvariant());
        MetaLine = string.Join("  •  ", parts);
        HasMetaLine = parts.Count > 0;

        TargetPlatforms = targetPlatforms;
    }

    public BroadcastProfile Profile { get; }
    public string Name { get; }
    public string TitleDisplay { get; }
    public bool HasTitleDisplay { get; }
    public string MetaLine { get; }
    public bool HasMetaLine { get; }
    public string NumberBadge { get; }
    public bool HasNumberPlaceholder { get; }
    public IReadOnlyList<BroadcastProfileTarget> TargetPlatforms { get; }
    public IReadOnlyList<string> DriftedPlatformNames { get; }
    public IReadOnlyList<string> PendingPlatformNames { get; }

    [ObservableProperty]
    private bool _isApplyInFlight;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ActiveStatusText), nameof(SyncToolTip), nameof(HasSyncToolTip))]
    private bool _isActive;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ActiveStatusText), nameof(SyncToolTip), nameof(HasSyncToolTip))]
    private bool _hasDrift;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ActiveStatusText), nameof(SyncToolTip), nameof(HasSyncToolTip))]
    private BroadcastProfileSyncState _syncState;

    public string ActiveStatusText
    {
        get
        {
            if (!IsActive)
            {
                return string.Empty;
            }

            return SyncState switch
            {
                BroadcastProfileSyncState.Drifted when DriftedPlatformNames.Count > 0 =>
                    $"Применён, расходится: {string.Join(", ", DriftedPlatformNames)}",
                BroadcastProfileSyncState.Drifted => "Применён, расходится с каналом",
                BroadcastProfileSyncState.Pending when PendingPlatformNames.Count > 0 =>
                    $"Применён, ожидает данные: {string.Join(", ", PendingPlatformNames)}",
                BroadcastProfileSyncState.Pending => "Применён, ожидает данные площадок",
                _ => "Применён",
            };
        }
    }

    public string? SyncToolTip
    {
        get
        {
            if (!IsActive)
            {
                return null;
            }

            return SyncState switch
            {
                BroadcastProfileSyncState.Drifted when DriftedPlatformNames.Count > 0 =>
                    $"Расходится: {string.Join(", ", DriftedPlatformNames)}",
                BroadcastProfileSyncState.Drifted => "Расходится с каналом",
                BroadcastProfileSyncState.Pending when PendingPlatformNames.Count > 0 =>
                    $"Ожидает данные: {string.Join(", ", PendingPlatformNames)}",
                BroadcastProfileSyncState.Pending => "Ожидает данные площадок",
                BroadcastProfileSyncState.Synced => "Синхронизирован со всеми целевыми площадками",
                _ => null,
            };
        }
    }

    public bool HasSyncToolTip => !string.IsNullOrEmpty(SyncToolTip);

    partial void OnIsActiveChanged(bool value)
    {
        var nextState = !value
            ? BroadcastProfileSyncState.None
            : SyncState == BroadcastProfileSyncState.None
                ? (HasDrift ? BroadcastProfileSyncState.Drifted : BroadcastProfileSyncState.Synced)
                : SyncState;

        if (_syncState != nextState)
        {
            _syncState = nextState;
            OnPropertyChanged(nameof(SyncState));
        }
    }

    partial void OnHasDriftChanged(bool value)
    {
        if (!IsActive)
        {
            return;
        }

        var nextState = value ? BroadcastProfileSyncState.Drifted : BroadcastProfileSyncState.Synced;
        if (_syncState != nextState)
        {
            _syncState = nextState;
            OnPropertyChanged(nameof(SyncState));
        }
    }

    private static BroadcastProfileSyncState ResolveInitialSyncState(
        bool isActive,
        bool hasDrift,
        BroadcastProfileSyncSummary? syncSummary)
    {
        if (!isActive)
        {
            return BroadcastProfileSyncState.None;
        }

        if (syncSummary is not null && syncSummary.State != BroadcastProfileSyncState.None)
        {
            return syncSummary.State;
        }

        return hasDrift ? BroadcastProfileSyncState.Drifted : BroadcastProfileSyncState.Synced;
    }

    [RelayCommand]
    private async Task ApplyAsync()
    {
        await _applyAsync(this);
    }

    [RelayCommand]
    private async Task IncrementNumberAsync()
    {
        await _adjustNumberAsync(this, +1);
    }

    [RelayCommand(CanExecute = nameof(CanDecrementNumber))]
    private async Task DecrementNumberAsync()
    {
        await _adjustNumberAsync(this, -1);
    }

    private bool CanDecrementNumber() => HasNumberPlaceholder && Profile.CurrentNumber > 1;

    [RelayCommand]
    private async Task EditAsync()
    {
        await _editAsync(this);
    }

    [RelayCommand]
    private void Duplicate()
    {
        _duplicate(this);
    }

    [RelayCommand]
    private void Delete()
    {
        _delete(this);
    }
}

public sealed record BroadcastProfileTarget(StreamingPlatform Platform, string DisplayName);
