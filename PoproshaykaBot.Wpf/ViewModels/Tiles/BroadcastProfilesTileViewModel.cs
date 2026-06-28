using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MahApps.Metro.IconPacks;
using Microsoft.Extensions.Logging;
using PoproshaykaBot.Core.Broadcast.Profiles;
using PoproshaykaBot.Core.Infrastructure.Events;
using PoproshaykaBot.Core.Infrastructure.Events.Broadcasting;
using PoproshaykaBot.Core.Infrastructure.Events.Streaming;
using PoproshaykaBot.Core.Settings.Stores;
using PoproshaykaBot.Core.Streaming;
using PoproshaykaBot.Wpf.Infrastructure;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using PoproshaykaBot.Wpf.Bootstrap;
using System.Threading.Tasks;

namespace PoproshaykaBot.Wpf.ViewModels.Tiles;

public sealed partial class BroadcastProfilesTileViewModel : DashboardTileViewModel, IDisposable
{
    private static readonly TimeSpan TitleMatchTimeout = TimeSpan.FromMilliseconds(100);

    private readonly BroadcastProfilesManager _manager;
    private readonly BroadcastProfilesStore _profiles;
    private readonly IStreamStatus _stream;
    private readonly ILogger<BroadcastProfilesTileViewModel> _logger;
    private readonly List<IDisposable> _subs = [];

    private Guid? _activeProfileId;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasProfiles))]
    private ObservableCollection<BroadcastProfileItemViewModel> _items = [];

    [ObservableProperty]
    private string _statusMessage = string.Empty;

    [ObservableProperty]
    private bool _hasStatusMessage;

    [ObservableProperty]
    private bool _isStatusError;

    public bool HasProfiles => Items.Count > 0;

    public BroadcastProfilesTileViewModel(
        BroadcastProfilesManager manager,
        BroadcastProfilesStore profiles,
        IStreamStatus stream,
        IEventBus bus,
        ILogger<BroadcastProfilesTileViewModel> logger)
        : base("broadcast-profiles", "Профили рассылки", maxWidth: 500)
    {
        _manager = manager;
        _profiles = profiles;
        _stream = stream;
        _logger = logger;

        HeaderActions.Add(new ToolbarItemViewModel(PackIconLucideKind.Plus, AddCommand, toolTip: "Добавить профиль"));
        HeaderActions.Add(new ToolbarItemViewModel(PackIconLucideKind.Pen, EditCurrentCommand, toolTip: "Из текущих настроек"));

        _subs.Add(bus.SubscribeOnUi<BroadcastProfilesChanged>(_ => ReloadItems()));
        _subs.Add(bus.SubscribeOnUi<BroadcastProfileApplying>(OnProfileApplying));
        _subs.Add(bus.SubscribeOnUi<BroadcastProfileApplied>(OnProfileApplied));
        _subs.Add(bus.SubscribeOnUi<BroadcastProfileApplyFailed>(OnProfileApplyFailed));
        _subs.Add(bus.SubscribeOnUi<ChannelInformationPatched>(_ => ClearInFlightStates("✓ Применено", false)));
        _subs.Add(bus.SubscribeOnUi<ChannelInformationPatchFailed>(e => ShowStatus($"✗ {e.ErrorMessage}", true)));
        _subs.Add(bus.SubscribeOnUi<StreamWentOnline>(_ => ReloadItems()));
        _subs.Add(bus.SubscribeOnUi<StreamWentOffline>(_ => ReloadItems()));
        _subs.Add(bus.SubscribeOnUi<StreamMetadataResolved>(_ => ReloadItems()));
        _subs.Add(bus.SubscribeOnUi<ChannelUpdated>(_ => ReloadItems()));

        _activeProfileId = profiles.Load().LastAppliedProfileId;
        ReloadItems();
    }

    [RelayCommand]
    private void Add()
    {
        // TODO: not implemented yet – open BroadcastProfileEditDialog when Step 4 dialogs are implemented
        _logger.BroadcastProfileAddNotImplemented();
    }

    [RelayCommand]
    private void EditCurrent()
    {
        // TODO: not implemented yet – fetch current channel settings and open dialog when Step 4 dialogs are implemented
        _logger.BroadcastProfileEditNotImplemented();
    }

    private void ReloadItems()
    {
        var allProfiles = _manager.GetAll();
        var activeId = _activeProfileId ?? _profiles.Load().LastAppliedProfileId;

        if (activeId.HasValue && allProfiles.All(p => p.Id != activeId.Value))
        {
            _activeProfileId = null;
            activeId = null;
        }

        var currentStream = _stream.CurrentStream;

        Items = new ObservableCollection<BroadcastProfileItemViewModel>(
            allProfiles.Select(p =>
            {
                var isActive = activeId.HasValue && p.Id == activeId.Value;
                var hasDrift = isActive && ProfileDivergesFromStream(p, currentStream);
                return new BroadcastProfileItemViewModel(p, isActive, hasDrift, ApplyProfileAsync, AdjustNumberAsync);
            }));
    }

    private async Task ApplyProfileAsync(BroadcastProfileItemViewModel item)
    {
        ShowStatus($"Применяется «{item.Profile.Name}»…", false);
        try
        {
            await _manager.ApplyAsync(item.Profile.Id, CancellationToken.None);
        }
        catch (Exception ex)
        {
            item.IsApplyInFlight = false;
            ShowStatus($"✗ {ex.Message}", true);
        }
    }

    private async Task AdjustNumberAsync(BroadcastProfileItemViewModel item, int delta)
    {
        var profile = item.Profile;
        var newNumber = profile.CurrentNumber + delta;
        if (newNumber < 1)
        {
            return;
        }

        var wasActive = item.IsActive;
        var copy = CloneProfile(profile);
        copy.CurrentNumber = newNumber;

        try
        {
            _manager.Upsert(copy);
        }
        catch (InvalidOperationException ex)
        {
            ShowStatus($"✗ {ex.Message}", true);
            return;
        }

        if (!wasActive)
        {
            return;
        }

        ShowStatus($"Применяется «{copy.Name}»…", false);

        try
        {
            await _manager.ApplyAsync(copy.Id, CancellationToken.None);
        }
        catch (Exception ex)
        {
            ShowStatus($"✗ {ex.Message}", true);
        }
    }

    private void OnProfileApplying(BroadcastProfileApplying @event)
    {
        SetApplyInFlight(@event.Profile.Id, true);
        ShowStatus($"Применяется «{@event.Profile.Name}»…", false);
    }

    private void OnProfileApplied(BroadcastProfileApplied @event)
    {
        _activeProfileId = @event.Profile.Id;
        ClearInFlightStates($"✓ Применён профиль «{@event.Profile.Name}»", false);
        ReloadItems();
    }

    private void OnProfileApplyFailed(BroadcastProfileApplyFailed @event)
    {
        ClearInFlightStates($"✗ {@event.ErrorMessage}", true);
    }

    private void SetApplyInFlight(Guid profileId, bool value)
    {
        foreach (var item in Items)
        {
            if (item.Profile.Id == profileId)
            {
                item.IsApplyInFlight = value;
                return;
            }
        }
    }

    private void ClearInFlightStates(string message, bool isError)
    {
        foreach (var item in Items)
        {
            item.IsApplyInFlight = false;
        }

        ShowStatus(message, isError);
    }

    private void ShowStatus(string message, bool isError)
    {
        StatusMessage = message;
        IsStatusError = isError;
        HasStatusMessage = !string.IsNullOrEmpty(message);
    }

    private static bool ProfileDivergesFromStream(BroadcastProfile profile, StreamInfo? stream)
    {
        if (stream is null)
        {
            return false;
        }

        if (!TitleMatches(profile.Title?.Trim() ?? string.Empty, stream.Title?.Trim() ?? string.Empty))
        {
            return true;
        }

        return !string.Equals(profile.GameId ?? string.Empty, stream.GameId ?? string.Empty, StringComparison.Ordinal);
    }

    private static bool TitleMatches(string profileTitle, string streamTitle)
    {
        const string Placeholder = "{n}";

        if (!profileTitle.Contains(Placeholder, StringComparison.Ordinal))
        {
            return string.Equals(profileTitle, streamTitle, StringComparison.Ordinal);
        }

        var pattern = "^" + Regex.Escape(profileTitle).Replace(Regex.Escape(Placeholder), "\\d+") + "$";
        return Regex.IsMatch(streamTitle, pattern, RegexOptions.None, TitleMatchTimeout);
    }

    private static BroadcastProfile CloneProfile(BroadcastProfile source)
    {
        return new()
        {
            Id = source.Id,
            Name = source.Name,
            Title = source.Title,
            GameId = source.GameId,
            GameName = source.GameName,
            BroadcasterLanguage = source.BroadcasterLanguage,
            Tags = source.Tags.ToList(),
            ObsSceneName = source.ObsSceneName,
            CurrentNumber = source.CurrentNumber,
            LastApplyAt = source.LastApplyAt,
            LastAutoAdvanceAt = source.LastAutoAdvanceAt,
        };
    }

    public void Dispose()
    {
        foreach (var sub in _subs)
        {
            sub.Dispose();
        }

        _subs.Clear();
    }
}
