using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MahApps.Metro.IconPacks;
using Microsoft.Extensions.DependencyInjection;
using PoproshaykaBot.Core.Broadcast.Profiles;
using PoproshaykaBot.Core.Infrastructure.Events;
using PoproshaykaBot.Core.Infrastructure.Events.Broadcasting;
using PoproshaykaBot.Core.Infrastructure.Events.Streaming;
using PoproshaykaBot.Core.Settings.Stores;
using PoproshaykaBot.Core.Streaming;
using PoproshaykaBot.Core.Twitch;
using PoproshaykaBot.Core.Twitch.Helix;
using PoproshaykaBot.Wpf.Infrastructure;
using PoproshaykaBot.Wpf.ViewModels.Dialogs;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace PoproshaykaBot.Wpf.ViewModels.Tiles;

public sealed partial class BroadcastProfilesTileViewModel : DashboardTileViewModel, IDisposable
{
    private static readonly TimeSpan TitleMatchTimeout = TimeSpan.FromMilliseconds(100);

    private readonly BroadcastProfilesManager _manager;
    private readonly BroadcastProfilesStore _profiles;
    private readonly IStreamStatus _stream;
    private readonly IChannelInformationApplier _applier;
    private readonly ITwitchChannelsApi _channelsApi;
    private readonly IBroadcasterIdProvider _broadcasterId;
    private readonly IDialogService _dialogService;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly List<IDisposable> _subs = [];

    private Guid? _activeProfileId;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasProfiles))]
    [NotifyPropertyChangedFor(nameof(GrowsWithSpace))]
    private ObservableCollection<BroadcastProfileItemViewModel> _items = [];

    [ObservableProperty]
    private string _statusMessage = string.Empty;

    [ObservableProperty]
    private bool _hasStatusMessage;

    [ObservableProperty]
    private bool _isStatusError;

    public bool HasProfiles => Items.Count > 0;

    public override bool GrowsWithSpace => HasProfiles;

    public BroadcastProfilesTileViewModel(
        BroadcastProfilesManager manager,
        BroadcastProfilesStore profiles,
        IStreamStatus stream,
        IChannelInformationApplier applier,
        ITwitchChannelsApi channelsApi,
        IBroadcasterIdProvider broadcasterId,
        IDialogService dialogService,
        IServiceScopeFactory scopeFactory,
        IEventBus bus)
        : base("broadcast-profiles", "Профили рассылки", maxWidth: 500, maxHeight: 320, minHeight: 130)
    {
        _manager = manager;
        _profiles = profiles;
        _stream = stream;
        _applier = applier;
        _channelsApi = channelsApi;
        _broadcasterId = broadcasterId;
        _dialogService = dialogService;
        _scopeFactory = scopeFactory;

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
    private async Task AddAsync()
    {
        var profile = new BroadcastProfile { Name = $"Новый профиль {DateTime.Now:HH:mm:ss}" };

        if (await EditInDialogAsync(profile))
        {
            Persist(profile);
        }
    }

    [RelayCommand]
    private async Task EditCurrentAsync()
    {
        ShowStatus("Загружаем текущие настройки…", false);

        BroadcastProfile draft;

        try
        {
            var broadcasterId = await _broadcasterId.GetAsync(CancellationToken.None);

            if (string.IsNullOrEmpty(broadcasterId))
            {
                ShowStatus("✗ Не удалось определить канал", true);
                return;
            }

            var info = await _channelsApi.GetChannelInformationAsync(broadcasterId, CancellationToken.None);

            if (info is null)
            {
                ShowStatus("✗ Не удалось загрузить настройки канала", true);
                return;
            }

            draft = new()
            {
                Id = Guid.Empty,
                Name = "(текущие настройки)",
                Title = info.Title,
                GameId = info.GameId,
                GameName = info.GameName,
                BroadcasterLanguage = string.IsNullOrEmpty(info.BroadcasterLanguage) ? "ru" : info.BroadcasterLanguage,
                Tags = info.Tags.ToList(),
            };

            ShowStatus(string.Empty, false);
        }
        catch (Exception ex)
        {
            ShowStatus($"✗ {HelixErrorMessages.SafeMessage(ex)}", true);
            return;
        }

        using var scope = _scopeFactory.CreateScope();
        var dialog = scope.ServiceProvider.GetRequiredService<BroadcastProfileEditDialogViewModel>();
        dialog.LoadFrom(draft);
        dialog.ConfigureCurrentSettingsMode();

        if (!await _dialogService.ShowAsync(dialog))
        {
            return;
        }

        dialog.WriteTo(draft);

        var newProfileName = dialog.NewProfileName;

        if (string.IsNullOrEmpty(newProfileName))
        {
            ShowStatus("Применяется текущая конфигурация…", false);
            await ApplyGuardedAsync(() => _applier.ApplyAsync(draft, CancellationToken.None));
            return;
        }

        var saved = new BroadcastProfile
        {
            Name = newProfileName,
            Title = draft.Title,
            GameId = draft.GameId,
            GameName = draft.GameName,
            BroadcasterLanguage = draft.BroadcasterLanguage,
            Tags = draft.Tags.ToList(),
        };

        try
        {
            _manager.Upsert(saved);
        }
        catch (InvalidOperationException ex)
        {
            ShowStatus(ex.Message, true);
            return;
        }

        ShowStatus($"Применяется «{saved.Name}»…", false);
        await ApplyGuardedAsync(() => _manager.ApplyAsync(saved.Id, CancellationToken.None));
    }

    private async Task EditProfileAsync(BroadcastProfileItemViewModel item)
    {
        var copy = CloneProfile(item.Profile);

        if (await EditInDialogAsync(copy))
        {
            Persist(copy);
        }
    }

    private void DuplicateProfile(BroadcastProfileItemViewModel item)
    {
        var source = item.Profile;

        Persist(new()
        {
            Name = source.Name + " (копия)",
            Title = source.Title,
            GameId = source.GameId,
            GameName = source.GameName,
            BroadcasterLanguage = source.BroadcasterLanguage,
            Tags = source.Tags.ToList(),
        });
    }

    private void DeleteProfile(BroadcastProfileItemViewModel item)
    {
        var profile = item.Profile;

        var confirmed = _dialogService.ConfirmWarning(
            "Удаление профиля",
            $"Удалить профиль «{profile.Name}»?");

        if (!confirmed)
        {
            return;
        }

        _manager.Remove(profile.Id);
    }

    private async Task<bool> EditInDialogAsync(BroadcastProfile profile)
    {
        using var scope = _scopeFactory.CreateScope();
        var dialog = scope.ServiceProvider.GetRequiredService<BroadcastProfileEditDialogViewModel>();
        dialog.LoadFrom(profile);

        if (!await _dialogService.ShowAsync(dialog))
        {
            return false;
        }

        dialog.WriteTo(profile);
        return true;
    }

    private void Persist(BroadcastProfile profile)
    {
        try
        {
            _manager.Upsert(profile);
        }
        catch (InvalidOperationException ex)
        {
            ShowStatus(ex.Message, true);
        }
    }

    private async Task ApplyGuardedAsync(Func<Task> apply)
    {
        try
        {
            await apply();
        }
        catch (Exception ex)
        {
            ShowStatus($"✗ {HelixErrorMessages.SafeMessage(ex)}", true);
        }
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
                return new BroadcastProfileItemViewModel(
                    p,
                    isActive,
                    hasDrift,
                    ApplyProfileAsync,
                    AdjustNumberAsync,
                    EditProfileAsync,
                    DuplicateProfile,
                    DeleteProfile);
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
