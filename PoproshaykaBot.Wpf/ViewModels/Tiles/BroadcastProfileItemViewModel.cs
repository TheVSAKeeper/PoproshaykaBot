using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PoproshaykaBot.Core.Broadcast.Profiles;
using PoproshaykaBot.Core.Chat;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace PoproshaykaBot.Wpf.ViewModels.Tiles;

public sealed partial class BroadcastProfileItemViewModel : ObservableObject
{
    private readonly Func<BroadcastProfileItemViewModel, Task> _applyAsync;
    private readonly Func<BroadcastProfileItemViewModel, int, Task> _adjustNumberAsync;

    public BroadcastProfileItemViewModel(
        BroadcastProfile profile,
        bool isActive,
        bool hasDrift,
        Func<BroadcastProfileItemViewModel, Task> applyAsync,
        Func<BroadcastProfileItemViewModel, int, Task> adjustNumberAsync)
    {
        Profile = profile;
        _isActive = isActive;
        _hasDrift = hasDrift;
        _applyAsync = applyAsync;
        _adjustNumberAsync = adjustNumberAsync;

        Name = profile.Name;

        var hasNumber = MessageTemplate.For(profile.Title).Contains("n");
        HasNumberPlaceholder = hasNumber;
        NumberBadge = hasNumber ? $"#{profile.CurrentNumber}" : string.Empty;

        TitleDisplay = string.IsNullOrWhiteSpace(profile.Title)
            ? string.Empty
            : $"«{profile.Title}»";
        HasTitleDisplay = !string.IsNullOrWhiteSpace(profile.Title);

        var parts = new List<string>();
        if (!string.IsNullOrWhiteSpace(profile.GameName))
            parts.Add(profile.GameName);
        if (profile.Tags.Count > 0)
            parts.Add(string.Join(" ", profile.Tags.Select(t => "#" + t)));
        if (!string.IsNullOrWhiteSpace(profile.ObsSceneName))
            parts.Add(profile.ObsSceneName);
        if (!string.IsNullOrWhiteSpace(profile.BroadcasterLanguage))
            parts.Add(profile.BroadcasterLanguage.ToUpperInvariant());
        MetaLine = string.Join("  •  ", parts);
        HasMetaLine = parts.Count > 0;
    }

    public BroadcastProfile Profile { get; }
    public string Name { get; }
    public string TitleDisplay { get; }
    public bool HasTitleDisplay { get; }
    public string MetaLine { get; }
    public bool HasMetaLine { get; }
    public string NumberBadge { get; }
    public bool HasNumberPlaceholder { get; }

    [ObservableProperty]
    private bool _isApplyInFlight;

    [ObservableProperty]
    private bool _isActive;

    [ObservableProperty]
    private bool _hasDrift;

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
    private void Edit()
    {
        // TODO: not implemented yet – open BroadcastProfileEditDialog when Step 4 dialogs are implemented
    }

    [RelayCommand]
    private void Duplicate()
    {
        // TODO: not implemented yet – duplicate profile when Step 4 dialogs are implemented
    }

    [RelayCommand]
    private void Delete()
    {
        // TODO: not implemented yet – confirm and delete profile when Step 4 dialogs are implemented
    }
}
