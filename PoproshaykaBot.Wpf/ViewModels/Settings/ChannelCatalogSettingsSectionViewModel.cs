using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using KeepShell.Services.Platform;
using KeepShell.ViewModels;
using Microsoft.Extensions.Logging;
using PoproshaykaBot.Core.Infrastructure.Events;
using PoproshaykaBot.Core.Platforms;
using PoproshaykaBot.Core.Platforms.Auth;
using PoproshaykaBot.Core.Settings;
using PoproshaykaBot.Core.Settings.Stores;
using PoproshaykaBot.Core.Streaming;
using PoproshaykaBot.Core.Twitch.Auth;
using PoproshaykaBot.Core.Twitch.Chat;
using PoproshaykaBot.Core.YouTube.Auth;
using PoproshaykaBot.Wpf.Bootstrap;
using PoproshaykaBot.Wpf.Infrastructure;
using PoproshaykaBot.Wpf.Infrastructure.Platforms;

namespace PoproshaykaBot.Wpf.ViewModels.Settings;

public sealed record ChannelSwitchModeOption(ChannelSwitchMode Mode, string DisplayName);

public sealed record PreselectedNextChannelOption(string? ChannelId, string DisplayName);

public sealed partial class ChannelCatalogSettingsSectionViewModel : ObservableObject
{
    private static readonly ChannelSwitchModeOption[] AllSwitchModeOptions =
    [
        new(ChannelSwitchMode.Manual, "Ручное переключение"),
        new(ChannelSwitchMode.PreselectedNext, "Заранее выбранный следующий канал"),
        new(ChannelSwitchMode.AutoDiscover, "Автопоиск старта на наблюдаемых каналах"),
    ];

    private readonly ChannelCatalogStore _catalogStore;
    private readonly YouTubeAccountsStore? _youtubeAccountsStore;
    private readonly SettingsManager? _settingsManager;
    private readonly YouTubeSettingsStore? _youtubeSettingsStore;
    private readonly ILogger<ChannelCatalogSettingsSectionViewModel>? _logger;
    private readonly IChatPlatformRegistry? _platformRegistry;
    private readonly IEventBus? _eventBus;
    private readonly Dictionary<StreamingPlatform, IPlatformChannelAdapter> _adapters;
    private readonly Dictionary<StreamingPlatform, IPlatformAccountDirectory> _accountDirectories;
    private readonly Dictionary<StreamingPlatform, IPlatformAccountLogin> _accountLogins;
    private readonly Dictionary<StreamingPlatform, Func<string?>> _unsavedCredentialChecks = [];
    private readonly List<ChannelCatalogItemViewModel> _allChannels = [];
    private readonly HashSet<ChannelKey> _removedChannels = [];
    private readonly Dictionary<StreamingPlatform, ChannelSwitchMode> _switchModes = [];
    private readonly Dictionary<StreamingPlatform, ChannelSwitchMode> _initialSwitchModes = [];
    private readonly Dictionary<StreamingPlatform, string?> _initialActiveChannels = [];
    private readonly Dictionary<StreamingPlatform, string?> _draftActiveChannels = [];
    private readonly Dictionary<StreamingPlatform, string?> _initialPreselectedNext = [];
    private readonly Dictionary<StreamingPlatform, string?> _draftPreselectedNext = [];
    private bool _suppressUpdates;
    private int _draftGeneration;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanResolveChannel), nameof(SupportsOwnChannelAuthorization), nameof(SupportsDefaultBotAuthorization),
        nameof(PlatformDisplayName), nameof(PlatformChannelsTitle), nameof(SelectedSwitchModeDescription))]
    [NotifyCanExecuteChangedFor(nameof(AddChannelCommand), nameof(AuthorizeOwnChannelCommand), nameof(AuthorizeDefaultBotCommand))]
    private StreamingPlatform _selectedPlatform = StreamingPlatform.Twitch;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(AuthorizeOwnChannelCommand), nameof(AuthorizeDefaultBotCommand))]
    private bool _isAuthorizingAccount;

    [ObservableProperty]
    private string _newChannelInput = string.Empty;

    [ObservableProperty]
    private string? _statusNotice;

    [ObservableProperty]
    private StatusSeverity _statusNoticeSeverity;

    [ObservableProperty]
    private ChannelCatalogItemViewModel? _selectedChannel;

    public ChannelCatalogSettingsSectionViewModel(
        ChannelCatalogStore catalogStore,
        IEnumerable<IPlatformChannelAdapter>? adapters = null,
        AccountsStore? twitchAccountsStore = null,
        YouTubeAccountsStore? youtubeAccountsStore = null,
        ITwitchOAuthService? twitchOAuth = null,
        IYouTubeOAuthService? youtubeOAuth = null,
        SettingsManager? settingsManager = null,
        YouTubeSettingsStore? youtubeSettingsStore = null,
        IShellLauncher? shellLauncher = null,
        TimeProvider? timeProvider = null,
        IEmbeddedTwitchAuthDialog? embeddedTwitchAuth = null,
        ILogger<ChannelCatalogSettingsSectionViewModel>? logger = null,
        IChatPlatformRegistry? platformRegistry = null,
        IEventBus? eventBus = null,
        IEnumerable<IPlatformAccountDirectory>? accountDirectories = null,
        IEnumerable<IPlatformAccountLogin>? accountLogins = null)
    {
        ArgumentNullException.ThrowIfNull(catalogStore);
        _catalogStore = catalogStore;
        _youtubeAccountsStore = youtubeAccountsStore;
        _settingsManager = settingsManager;
        _youtubeSettingsStore = youtubeSettingsStore;
        _logger = logger;
        _platformRegistry = platformRegistry;
        _eventBus = eventBus;
        _adapters = (adapters ?? []).ToDictionary(static a => a.Platform);
        _accountDirectories = PlatformAccountLoginDefaults.BuildDirectories(
            accountDirectories, twitchAccountsStore, youtubeAccountsStore);
        _accountLogins = PlatformAccountLoginDefaults.Build(
            accountLogins,
            settingsManager,
            twitchAccountsStore,
            twitchOAuth,
            youtubeSettingsStore,
            youtubeAccountsStore,
            youtubeOAuth,
            shellLauncher,
            timeProvider,
            embeddedTwitchAuth,
            eventBus: eventBus);
    }

    public event EventHandler? Edited;

    public event EventHandler<StreamingPlatform>? AccountsChanged;

    internal void SetCredentialDrafts(OAuthSettingsViewModel? twitch, YouTubeSettingsSectionViewModel? youtube)
    {
        _unsavedCredentialChecks.Clear();
        if (twitch is not null && _settingsManager is not null)
        {
            _unsavedCredentialChecks[StreamingPlatform.Twitch] = () =>
            {
                var settings = _settingsManager.Current.Twitch;
                return twitch.ClientId.Trim() != settings.ClientId
                    || twitch.ClientSecret.Trim() != settings.ClientSecret
                    || twitch.RedirectUri.Trim() != settings.RedirectUri
                    ? "Сначала сохраните новые реквизиты в блоке «Приложение Twitch», затем повторите вход."
                    : null;
            };
        }

        if (youtube is not null && _youtubeSettingsStore is not null && _youtubeAccountsStore is not null)
        {
            _unsavedCredentialChecks[StreamingPlatform.YouTube] = () =>
            {
                var settings = _youtubeSettingsStore.Load();
                var secret = _youtubeAccountsStore.LoadClientSecret();
                return youtube.ClientId.Trim() != settings.ClientId || youtube.ClientSecret.Trim() != secret
                    ? "Сначала сохраните новые реквиты в блоке «Google-проект», затем повторите вход."
                    : null;
            };
        }
    }

    public string PlatformDisplayName =>
        _platformRegistry?.Find(SelectedPlatform)?.DisplayName ?? PlatformVisuals.GetDisplayName(SelectedPlatform);

    public string PlatformChannelsTitle => $"Каналы {PlatformDisplayName}";

    public int VisibleChannelCount => VisibleChannels.Count;

    public bool HasMultipleChannels => VisibleChannelCount > 1;

    public string SelectedSwitchModeDescription => SelectedSwitchModeOption.Mode switch
    {
        ChannelSwitchMode.PreselectedNext => "При запуске эфира на заранее выбранном канале бот переключится на него автоматически.",
        ChannelSwitchMode.AutoDiscover => "Бот отслеживает все наблюдаемые каналы и переключается на тот, где начался эфир.",
        _ => "Активный канал переключается вручную кнопкой «Сделать активным».",
    };

    public IReadOnlyList<ChannelSwitchModeOption> SwitchModeOptions =>
        SupportsAutomaticSwitching ? AllSwitchModeOptions : [AllSwitchModeOptions[0]];

    public bool CanResolveChannel => _adapters.ContainsKey(SelectedPlatform);

    public bool SupportsOwnChannelAuthorization => CanAuthorizeAccount(SelectedPlatform);

    public bool SupportsDefaultBotAuthorization => CanAuthorizeAccount(SelectedPlatform);

    private bool SupportsAutomaticSwitching =>
        HasCapability(SelectedPlatform, PlatformCapabilities.MultiChannelMonitoring);

    private bool HasCapability(StreamingPlatform platform, PlatformCapabilities capability) =>
        _adapters.TryGetValue(platform, out var adapter) && adapter.Capabilities.HasFlag(capability);

    private bool CanAuthorizeAccount(StreamingPlatform platform) =>
        _accountLogins.GetValueOrDefault(platform)?.CanAuthorize == true;

    public ObservableCollection<ChannelCatalogItemViewModel> VisibleChannels { get; } = [];

    public IReadOnlyList<PreselectedNextChannelOption> PreselectedNextOptions { get; private set; } = [];

    public bool IsPreselectedNextMode =>
        SupportsAutomaticSwitching
        && _switchModes.GetValueOrDefault(SelectedPlatform, ChannelSwitchMode.Manual) == ChannelSwitchMode.PreselectedNext;

    public PreselectedNextChannelOption SelectedPreselectedNextOption
    {
        get
        {
            var id = _draftPreselectedNext.GetValueOrDefault(SelectedPlatform);
            return PreselectedNextOptions.FirstOrDefault(o => string.Equals(o.ChannelId, id, StringComparison.Ordinal))
                ?? (PreselectedNextOptions.Count > 0 ? PreselectedNextOptions[0] : new(null, "Не выбран"));
        }
        set
        {
            if (value is null || !PreselectedNextOptions.Contains(value))
            {
                return;
            }

            var nextId = value.ChannelId;
            if (string.Equals(_draftPreselectedNext.GetValueOrDefault(SelectedPlatform), nextId, StringComparison.Ordinal))
            {
                return;
            }

            _draftPreselectedNext[SelectedPlatform] = nextId;
            RefreshPreselectedNextFlags(SelectedPlatform);
            OnPropertyChanged();
            NotifyEdited();
        }
    }

    public bool HasChannels => VisibleChannels.Count > 0;

    internal void SelectActiveChannel()
    {
        var activeId = _catalogStore.GetActiveChannel(SelectedPlatform)?.ChannelId;
        var activeChannel = VisibleChannels.FirstOrDefault(channel => channel.ChannelId == activeId);
        if (activeChannel is not null
            && _draftActiveChannels.GetValueOrDefault(SelectedPlatform) == _initialActiveChannels.GetValueOrDefault(SelectedPlatform))
        {
            _initialActiveChannels[SelectedPlatform] = _draftActiveChannels[SelectedPlatform] = activeId;
            foreach (var channel in VisibleChannels)
            {
                channel.IsActive = ReferenceEquals(channel, activeChannel);
            }
        }

        SelectedChannel = activeChannel
            ?? VisibleChannels.FirstOrDefault(channel => channel.IsActive)
            ?? VisibleChannels.FirstOrDefault();
    }

    public ChannelSwitchModeOption SelectedSwitchModeOption
    {
        get
        {
            var mode = _switchModes.GetValueOrDefault(SelectedPlatform, ChannelSwitchMode.Manual);
            return SwitchModeOptions.FirstOrDefault(o => o.Mode == mode) ?? AllSwitchModeOptions[0];
        }
        set
        {
            if (value is null || !SwitchModeOptions.Contains(value))
            {
                return;
            }

            if (_switchModes.GetValueOrDefault(SelectedPlatform, ChannelSwitchMode.Manual) == value.Mode)
            {
                return;
            }

            _switchModes[SelectedPlatform] = value.Mode;
            RefreshPreselectedNextFlags(SelectedPlatform);
            OnPropertyChanged();
            OnPropertyChanged(nameof(IsPreselectedNextMode));
            OnPropertyChanged(nameof(SelectedSwitchModeDescription));
            NotifyEdited();
        }
    }

    public void Load()
    {
        _draftGeneration++;
        _suppressUpdates = true;
        try
        {
            _allChannels.Clear();
            _removedChannels.Clear();
            _switchModes.Clear();
            _initialSwitchModes.Clear();
            _initialActiveChannels.Clear();
            _draftActiveChannels.Clear();
            _initialPreselectedNext.Clear();
            _draftPreselectedNext.Clear();
            StatusNotice = null;

            var settings = _catalogStore.Load();
            foreach (var routing in settings.Platforms)
            {
                _initialSwitchModes[routing.Platform] = _switchModes[routing.Platform] = routing.SwitchMode;
                _initialActiveChannels[routing.Platform] = routing.ActiveChannelId;
                _draftActiveChannels[routing.Platform] = routing.ActiveChannelId;
                _initialPreselectedNext[routing.Platform] = routing.PreselectedNextChannelId;
                _draftPreselectedNext[routing.Platform] = routing.PreselectedNextChannelId;
            }

            foreach (var entry in settings.Channels.Where(static c => c.IsConfigured))
            {
                var isActive = string.Equals(_draftActiveChannels.GetValueOrDefault(entry.Platform), entry.ChannelId, StringComparison.Ordinal);
                _allChannels.Add(new ChannelCatalogItemViewModel(
                    entry,
                    isActive,
                    HasCapability(entry.Platform, PlatformCapabilities.BackgroundStreamMonitoring),
                    NotifyEdited,
                    VerifyAccessAsync,
                    AuthorizeRoleAsync,
                    SetActiveInDraft,
                    RemoveChannel));
            }

            RefreshPlatformView();
        }
        finally
        {
            _suppressUpdates = false;
        }
    }

    public void ApplyTo(ChannelsSettings target) => ApplyTo(target, applyActive: true);

    internal void ApplyTo(ChannelsSettings target, bool applyActive)
    {
        ArgumentNullException.ThrowIfNull(target);

        var channels = target.Channels.ToDictionary(static entry => entry.Key);
        foreach (var item in _allChannels)
        {
            var key = new ChannelKey(item.Platform, item.ChannelId);
            if (channels.TryGetValue(key, out var current) && current.IsConfigured)
            {
                item.ApplyEditsTo(current);
            }
            else if (item.IsNew)
            {
                var entry = item.ToEntry();
                entry.KnownAliases = [.. entry.KnownAliases.Union(current?.KnownAliases ?? [], StringComparer.OrdinalIgnoreCase)];
                channels[key] = entry;
            }
        }

        target.Channels = channels.Values.Where(entry => !entry.IsConfigured || !_removedChannels.Contains(entry.Key)).ToList();

        var existingPlatforms = target.Platforms.ToDictionary(static p => p.Platform);
        var editedModes = _switchModes
            .Where(draft => draft.Value != _initialSwitchModes.GetValueOrDefault(draft.Key, ChannelSwitchMode.Manual))
            .Select(static draft => draft.Key)
            .ToHashSet();
        var editedPlatforms = editedModes
            .Union(_draftActiveChannels.Where(draft => draft.Value != _initialActiveChannels.GetValueOrDefault(draft.Key))
                .Select(static draft => draft.Key))
            .Union(_draftPreselectedNext.Where(draft => draft.Value != _initialPreselectedNext.GetValueOrDefault(draft.Key))
                .Select(static draft => draft.Key))
            .Union(_removedChannels.Select(static channel => channel.Platform));

        foreach (var platform in editedPlatforms)
        {
            existingPlatforms.TryGetValue(platform, out var livePlatform);
            var mode = editedModes.Contains(platform)
                ? _switchModes[platform]
                : livePlatform?.SwitchMode ?? ChannelSwitchMode.Manual;
            var initialActive = _initialActiveChannels.GetValueOrDefault(platform);
            var draftActive = _draftActiveChannels.GetValueOrDefault(platform);
            var activeChangedInDraft = !string.Equals(initialActive, draftActive, StringComparison.Ordinal);

            var finalActive = activeChangedInDraft && applyActive
                ? draftActive
                : livePlatform?.ActiveChannelId;

            if (!string.IsNullOrWhiteSpace(finalActive)
                && !target.Channels.Any(c => c.Platform == platform && c.IsConfigured && c.ChannelId == finalActive))
            {
                var replacement = target.Channels.FirstOrDefault(c => c.Platform == platform && c.IsConfigured);
                if (!applyActive && replacement is not null)
                {
                    throw new InvalidOperationException("Активный канал изменился во время сохранения. Повторите сохранение.");
                }

                finalActive = replacement?.ChannelId;
            }

            var initialNext = _initialPreselectedNext.GetValueOrDefault(platform);
            var draftNext = _draftPreselectedNext.GetValueOrDefault(platform);
            var nextChangedInDraft = !string.Equals(initialNext, draftNext, StringComparison.Ordinal);
            var finalNext = nextChangedInDraft
                ? draftNext
                : (livePlatform is not null ? livePlatform.PreselectedNextChannelId : draftNext);

            if (!string.IsNullOrWhiteSpace(finalNext)
                && !target.Channels.Any(c => c.Platform == platform && c.IsConfigured && c.ChannelId == finalNext))
            {
                finalNext = null;
            }

            existingPlatforms[platform] = new()
            {
                Platform = platform,
                ActiveChannelId = finalActive,
                SwitchMode = mode,
                PreselectedNextChannelId = finalNext,
            };
        }

        target.Platforms = [.. existingPlatforms.Values];
    }

    internal IReadOnlyList<ChannelCatalogEntry> GetPendingActiveChanges(ChannelsSettings target)
    {
        var editedPlatforms = _draftActiveChannels
            .Where(draft => draft.Value != _initialActiveChannels.GetValueOrDefault(draft.Key))
            .Select(static draft => draft.Key)
            .Union(_removedChannels.Select(static key => key.Platform))
            .ToArray();
        var activeBefore = editedPlatforms.ToDictionary(platform => platform, platform => GetActiveEntry(target, platform)?.Key);
        ApplyTo(target);

        var changes = new List<ChannelCatalogEntry>();
        foreach (var platform in editedPlatforms)
        {
            if (GetActiveEntry(target, platform) is { } activeAfter && activeAfter.Key != activeBefore[platform])
            {
                changes.Add(activeAfter);
            }
        }

        return changes;
    }

    private static ChannelCatalogEntry? GetActiveEntry(ChannelsSettings settings, StreamingPlatform platform)
    {
        var activeId = settings.Platforms.FirstOrDefault(routing => routing.Platform == platform)?.ActiveChannelId;
        var entries = settings.Channels.Where(entry => entry.Platform == platform && entry.IsConfigured);
        return entries.FirstOrDefault(entry => entry.ChannelId == activeId) ?? entries.FirstOrDefault();
    }

    internal void AcceptActiveSwitch(ChannelKey active)
    {
        _initialActiveChannels[active.Platform] = active.ChannelId;
        _allChannels.FirstOrDefault(item => item.Platform == active.Platform && item.ChannelId == active.ChannelId)?.AcceptEdits();
    }

    internal void AcceptSavedCatalog(ChannelsSettings saved)
    {
        _removedChannels.Clear();
        _initialSwitchModes.Clear();
        _initialActiveChannels.Clear();
        _draftActiveChannels.Clear();
        _initialPreselectedNext.Clear();
        _draftPreselectedNext.Clear();
        _switchModes.Clear();

        foreach (var routing in saved.Platforms)
        {
            _initialActiveChannels[routing.Platform] = _draftActiveChannels[routing.Platform] = routing.ActiveChannelId;
            _initialPreselectedNext[routing.Platform] = _draftPreselectedNext[routing.Platform] = routing.PreselectedNextChannelId;
            _initialSwitchModes[routing.Platform] = _switchModes[routing.Platform] = routing.SwitchMode;
        }

        foreach (var item in _allChannels)
        {
            item.AcceptEdits();
            item.IsActive = GetActiveEntry(saved, item.Platform)?.ChannelId == item.ChannelId;
        }

        RefreshPreselectedNextOptions();
        RefreshPreselectedNextFlags(SelectedPlatform);
        OnPropertyChanged(nameof(SelectedSwitchModeOption));
        OnPropertyChanged(nameof(IsPreselectedNextMode));
        OnPropertyChanged(nameof(SelectedSwitchModeDescription));
    }

    public string? ValidateAssignments()
    {
        foreach (var channel in _allChannels)
        {
            var pending = channel.Roles.FirstOrDefault(role => role.IsEdited && role.NeedsVerification);
            if (pending is not null)
            {
                var platformName = _platformRegistry?.Find(channel.Platform)?.DisplayName ?? PlatformVisuals.GetDisplayName(channel.Platform);
                if (pending.NeedsRoleGrant)
                {
                    return $"Выдайте права роли «{pending.Title}» для аккаунта «{pending.AccountDisplayName ?? pending.AccountId}» на канале «{channel.DisplayName}» ({platformName}) или выберите другой аккаунт.";
                }

                return $"Проверьте доступ аккаунта «{pending.Title}» на канале «{channel.DisplayName}» ({platformName}).";
            }
        }

        return null;
    }

    partial void OnSelectedPlatformChanged(StreamingPlatform value)
    {
        StatusNotice = null;
        RefreshPlatformView();
    }

    private bool CanAuthorizeOwnChannel() => !IsAuthorizingAccount && CanAuthorizeAccount(SelectedPlatform);

    private bool CanAuthorizeDefaultBot() => !IsAuthorizingAccount && !HasChannels && SupportsDefaultBotAuthorization;

    [RelayCommand(CanExecute = nameof(CanAuthorizeDefaultBot))]
    private async Task AuthorizeDefaultBotAsync()
    {
        var platform = SelectedPlatform;
        var draft = _draftGeneration;
        void Report(string message, StatusSeverity severity)
        {
            if (draft == _draftGeneration && SelectedPlatform == platform)
            {
                SetNotice(message, severity);
            }
        }

        await AuthorizeAccountAsync(platform, PlatformAuthRole.Bot, Report, useDefaultContext: true);
    }

    [RelayCommand(CanExecute = nameof(CanAuthorizeOwnChannel))]
    private async Task AuthorizeOwnChannelAsync()
    {
        var platform = SelectedPlatform;
        var draft = _draftGeneration;
        StatusNotice = null;
        var account = await AuthorizeAccountAsync(platform, PlatformAuthRole.Broadcaster, SetNotice);
        if (account is null || draft != _draftGeneration)
        {
            return;
        }

        var item = AddOrSelectChannel(account.OwnChannel);
        var role = item.Roles.Single(candidate => candidate.Role == PlatformAuthRole.Broadcaster);
        role.Assign(account.OwnChannel.Channel.ChannelId, account.ContextId);
        await VerifyAccessAsync(item, role);
    }

    private async Task AuthorizeRoleAsync(ChannelCatalogItemViewModel item, ChannelAccountRoleViewModel role)
    {
        var selectedAccountId = role.AccountId;
        var selectedContextId = role.ContextId;
        var account = await AuthorizeAccountAsync(item.Platform, role.Role, role.SetStatus);
        if (account is null || !_allChannels.Contains(item)
            || selectedAccountId != role.AccountId
            || (!string.IsNullOrEmpty(selectedContextId) && selectedContextId != role.ContextId))
        {
            return;
        }

        role.Assign(account.OwnChannel.Channel.ChannelId, account.ContextId);
        await VerifyAccessAsync(item, role);
    }

    private async Task<AuthorizedAccount?> AuthorizeAccountAsync(
        StreamingPlatform platform, PlatformAuthRole role, Action<string, StatusSeverity> report, bool useDefaultContext = false)
    {
        if (IsAuthorizingAccount)
        {
            report("Дождитесь завершения текущего входа.", StatusSeverity.Info);
            return null;
        }

        if (_unsavedCredentialChecks.TryGetValue(platform, out var check) && check() is { } unsavedWarning)
        {
            report(unsavedWarning, StatusSeverity.Warning);
            return null;
        }

        if (!_accountLogins.TryGetValue(platform, out var login))
        {
            return null;
        }

        IsAuthorizingAccount = true;
        report("Ожидаем вход на площадке…", StatusSeverity.Info);
        _logger?.ChannelAuthorizationStarted(platform, role);
        try
        {
            var result = await login.AuthorizeAsync(new(
                [role],
                null,
                null,
                [],
                false,
                ForceDefaultContext: useDefaultContext,
                PreferDefaultContextWhenEmpty: useDefaultContext));
            var grant = result.ChangedAuthorizations.FirstOrDefault(g => g.Role == role)
                ?? (result.ChangedAuthorizations.Count > 0 ? result.ChangedAuthorizations[0] : null);
            if (grant is null || string.IsNullOrWhiteSpace(grant.SubjectId))
            {
                report(result.Message, result.Severity);
                _logger?.ChannelAuthorizationIncomplete(platform, role);
                return null;
            }

            var persisted = _accountDirectories.GetValueOrDefault(platform)?.VerifyPersistedAuthorization(
                grant.Role, grant.ContextId, grant.SubjectId) ?? true;
            if (!persisted)
            {
                report("Аккаунт действует до перезапуска. Записать авторизацию в файл не удалось.", StatusSeverity.Warning);
                _logger?.ChannelAuthorizationIncomplete(platform, role);
                return null;
            }

            var display = grant.DisplayLabel ?? grant.Label;
            var ownChannel = result.OwnChannel ?? new ResolvedChannelIdentity(
                new(platform, grant.SubjectId), display, display, string.Empty);
            report($"Аккаунт «{ownChannel.DisplayName}» подключён.", StatusSeverity.Success);
            RefreshAccountOptions(platform);
            AccountsChanged?.Invoke(this, platform);
            if (_eventBus is not null)
            {
                await _eventBus.PublishAsync(new PlatformAuthorizationRefreshed(platform, role, grant.ContextId));
            }

            return new(grant.ContextId, ownChannel);
        }
        catch (OperationCanceledException)
        {
            report("Вход отменён.", StatusSeverity.Info);
            return null;
        }
        catch (Exception exception)
        {
            _logger?.ChannelAuthorizationFailed(platform, role, exception.GetType().Name);
            report($"Не удалось подключить аккаунт. {StreamingErrorMessages.SafeMessage(exception)}", StatusSeverity.Error);
            return null;
        }
        finally
        {
            IsAuthorizingAccount = false;
        }
    }

    private void SetNotice(string message, StatusSeverity severity)
    {
        StatusNoticeSeverity = severity;
        StatusNotice = message;
    }

    private sealed record AuthorizedAccount(string ContextId, ResolvedChannelIdentity OwnChannel);

    [RelayCommand(CanExecute = nameof(CanResolveChannel))]
    private async Task AddChannelAsync()
    {
        var input = NewChannelInput.Trim();
        if (string.IsNullOrWhiteSpace(input))
        {
            return;
        }

        var targetPlatform = SelectedPlatform;
        var draft = _draftGeneration;
        StatusNotice = null;
        if (!_adapters.TryGetValue(targetPlatform, out var adapter))
        {
            SetNotice("Адаптер площадки не зарегистрирован.", StatusSeverity.Error);
            return;
        }

        ResolvedChannelIdentity? resolved;
        try
        {
            var contextId = (ListAccounts(targetPlatform) ?? [])
                .SelectMany(account => account.Authorizations)
                .Where(grant => grant.HasToken)
                .OrderBy(grant => targetPlatform == StreamingPlatform.YouTube && grant.Role == PlatformAuthRole.Bot ? 0 : 1)
                .FirstOrDefault()?.ContextId;
            resolved = await adapter.ResolveChannelIdentityAsync(input, contextId, CancellationToken.None).ConfigureAwait(true);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger?.ChannelResolveFailed(ex, targetPlatform, ex.GetType().Name);
            if (draft == _draftGeneration && SelectedPlatform == targetPlatform)
            {
                SetNotice($"Не удалось определить канал. {StreamingErrorMessages.SafeMessage(ex)}", StatusSeverity.Error);
            }
            return;
        }

        if (draft != _draftGeneration)
        {
            return;
        }

        if (resolved is null || !resolved.Channel.IsValid || resolved.Channel.Platform != targetPlatform)
        {
            if (SelectedPlatform == targetPlatform)
            {
                SetNotice("Не удалось определить идентификатор канала. Проверьте ввод и авторизацию площадки.", StatusSeverity.Error);
            }
            return;
        }

        AddOrSelectChannel(resolved);
        if (targetPlatform == SelectedPlatform && NewChannelInput.Trim() == input)
        {
            NewChannelInput = string.Empty;
        }
    }

    private ChannelCatalogItemViewModel AddOrSelectChannel(ResolvedChannelIdentity channel)
    {
        var (platform, channelId) = (channel.Channel.Platform, channel.Channel.ChannelId);
        var existing = _allChannels.FirstOrDefault(candidate => candidate.Platform == platform && candidate.ChannelId == channelId);
        if (existing is not null)
        {
            if (SelectedPlatform == platform)
            {
                SelectedChannel = existing;
                SetNotice($"Канал «{existing.DisplayName}» уже добавлен.", StatusSeverity.Info);
            }
            return existing;
        }

        var entry = new ChannelCatalogEntry
        {
            Platform = platform,
            ChannelId = channelId,
            DisplayName = channel.DisplayName,
            Handle = channel.Handle,
            ChannelUrl = channel.ChannelUrl,
            KnownAliases = [channel.DisplayName],
            IsConfigured = true,
            MonitoringEnabled = HasCapability(platform, PlatformCapabilities.BackgroundStreamMonitoring),
            BroadcasterContextId = AuthContextIds.Default,
            BotContextId = AuthContextIds.Default,
        };

        var isFirstForPlatform = !_allChannels.Any(c => c.Platform == platform);
        if (isFirstForPlatform)
        {
            _draftActiveChannels[platform] = channelId;
        }

        var vm = new ChannelCatalogItemViewModel(
            entry,
            isFirstForPlatform,
            HasCapability(platform, PlatformCapabilities.BackgroundStreamMonitoring),
            NotifyEdited,
            VerifyAccessAsync,
            AuthorizeRoleAsync,
            SetActiveInDraft,
            RemoveChannel) { IsNew = true };
        _removedChannels.Remove(entry.Key);
        _allChannels.Add(vm);
        if (SelectedPlatform == platform)
        {
            RefreshPlatformView();
            SelectedChannel = vm;
        }
        else
        {
            RefreshAccountOptions(platform);
        }
        NotifyEdited();
        return vm;
    }

    private async Task VerifyAccessAsync(ChannelCatalogItemViewModel item, ChannelAccountRoleViewModel role)
    {
        if (string.IsNullOrWhiteSpace(role.AccountId))
        {
            role.SetStatus("Сначала выберите или подключите аккаунт.", StatusSeverity.Warning);
            return;
        }

        if (!_adapters.TryGetValue(item.Platform, out var adapter))
        {
            role.SetStatus("Адаптер площадки не зарегистрирован.", StatusSeverity.Error);
            return;
        }

        var contextId = role.ContextId;
        var accountId = role.AccountId;
        role.SetStatus("Проверяем доступ…", StatusSeverity.Info);
        _logger?.ChannelRoleVerificationStarted(role.Role, item.ChannelId, item.Platform);
        ChannelAccessVerification result;
        try
        {
            result = await adapter.VerifyChannelAccessAsync(new(item.Platform, item.ChannelId), role.Role, contextId, CancellationToken.None);
        }
        catch (OperationCanceledException)
        {
            result = new(false, null, "Проверка отменена.");
        }
        catch (Exception exception)
        {
            _logger?.ChannelRoleVerificationFailed(role.Role, item.ChannelId, item.Platform, exception.GetType().Name);
            result = new(false, null, StreamingErrorMessages.SafeMessage(exception));
        }

        if (_allChannels.Contains(item) && contextId == role.ContextId && accountId == role.AccountId)
        {
            if (!result.IsAllowed || string.IsNullOrWhiteSpace(result.SubjectId) || accountId != result.SubjectId)
            {
                _logger?.ChannelRoleVerificationDenied(role.Role, item.ChannelId, item.Platform);
            }

            role.SetVerification(result);
        }
    }

    private void SetActiveInDraft(ChannelCatalogItemViewModel item)
    {
        _draftActiveChannels[item.Platform] = item.ChannelId;
        if (_draftPreselectedNext.GetValueOrDefault(item.Platform) == item.ChannelId)
        {
            _draftPreselectedNext[item.Platform] = null;
        }

        foreach (var candidate in _allChannels.Where(c => c.Platform == item.Platform))
        {
            candidate.IsActive = ReferenceEquals(candidate, item);
        }

        RefreshPreselectedNextFlags(item.Platform);
        NotifyEdited();
    }

    private void RemoveChannel(ChannelCatalogItemViewModel item)
    {
        _allChannels.Remove(item);
        if (!item.IsNew)
        {
            _removedChannels.Add(new(item.Platform, item.ChannelId));
        }

        if (_draftPreselectedNext.GetValueOrDefault(item.Platform) == item.ChannelId)
        {
            _draftPreselectedNext[item.Platform] = null;
        }

        if (string.Equals(_draftActiveChannels.GetValueOrDefault(item.Platform), item.ChannelId, StringComparison.Ordinal))
        {
            var next = _allChannels.FirstOrDefault(c => c.Platform == item.Platform);
            _draftActiveChannels[item.Platform] = next?.ChannelId;
            if (next is not null)
            {
                next.IsActive = true;
            }
        }

        RefreshPlatformView();
        NotifyEdited();
    }

    private void RefreshPlatformView()
    {
        var selectedId = SelectedChannel?.Platform == SelectedPlatform ? SelectedChannel.ChannelId : null;
        VisibleChannels.Clear();
        foreach (var item in _allChannels.Where(c => c.Platform == SelectedPlatform))
        {
            VisibleChannels.Add(item);
        }

        RefreshAccountOptions(SelectedPlatform);
        RefreshPreselectedNextOptions();
        RefreshPreselectedNextFlags(SelectedPlatform);
        SelectedChannel = VisibleChannels.FirstOrDefault(channel => channel.ChannelId == selectedId)
            ?? VisibleChannels.FirstOrDefault(channel => channel.IsActive)
            ?? VisibleChannels.FirstOrDefault();
        OnPropertyChanged(nameof(HasChannels));
        OnPropertyChanged(nameof(VisibleChannelCount));
        OnPropertyChanged(nameof(HasMultipleChannels));
        OnPropertyChanged(nameof(SwitchModeOptions));
        OnPropertyChanged(nameof(SelectedSwitchModeOption));
        OnPropertyChanged(nameof(IsPreselectedNextMode));
        OnPropertyChanged(nameof(SelectedSwitchModeDescription));
        AuthorizeDefaultBotCommand.NotifyCanExecuteChanged();
    }

    private void RefreshPreselectedNextFlags(StreamingPlatform platform)
    {
        var isPreselectedMode = HasCapability(platform, PlatformCapabilities.MultiChannelMonitoring)
            && _switchModes.GetValueOrDefault(platform, ChannelSwitchMode.Manual) == ChannelSwitchMode.PreselectedNext;
        var nextId = _draftPreselectedNext.GetValueOrDefault(platform);
        foreach (var item in _allChannels.Where(c => c.Platform == platform))
        {
            item.IsPreselectedNext = isPreselectedMode
                && !item.IsActive
                && string.Equals(item.ChannelId, nextId, StringComparison.Ordinal);
        }
    }

    private void RefreshPreselectedNextOptions()
    {
        List<PreselectedNextChannelOption> options = [new(null, "Не выбран")];
        var active = _draftActiveChannels.GetValueOrDefault(SelectedPlatform);
        foreach (var item in _allChannels.Where(c => c.Platform == SelectedPlatform && c.ChannelId != active))
        {
            options.Add(new(item.ChannelId, $"{item.DisplayName} ({item.ChannelId})"));
        }

        if (!PreselectedNextOptions.SequenceEqual(options))
        {
            PreselectedNextOptions = options;
            OnPropertyChanged(nameof(PreselectedNextOptions));
        }

        OnPropertyChanged(nameof(SelectedPreselectedNextOption));
    }

    private IReadOnlyList<PlatformAccount>? ListAccounts(StreamingPlatform platform)
    {
        var accounts = _adapters.GetValueOrDefault(platform)?.ListAccounts()
            ?? _accountDirectories.GetValueOrDefault(platform)?.ListAccounts();
        return accounts?.Where(account => account.Key.Platform == platform).ToArray();
    }

    internal void RefreshAccountOptions(StreamingPlatform platform)
    {
        var accounts = ListAccounts(platform);
        var canAuthorize = CanAuthorizeAccount(platform);
        var canVerify = _adapters.ContainsKey(platform);
        foreach (var item in _allChannels.Where(item => item.Platform == platform))
        {
            foreach (var role in item.Roles)
            {
                role.RefreshAccounts(accounts, canAuthorize, canVerify);
            }
        }
    }

    private void NotifyEdited()
    {
        if (!_suppressUpdates)
        {
            RefreshPreselectedNextOptions();
            Edited?.Invoke(this, EventArgs.Empty);
        }
    }
}

public sealed record ChannelCardStatus(
    string BroadcastText,
    StatusSeverity BroadcastSeverity,
    string? BroadcastDetails,
    string ChatText,
    StatusSeverity ChatSeverity);

public sealed partial class ChannelCatalogItemViewModel : ObservableObject
{
    private readonly Action _onEdited;
    private readonly Action<ChannelCatalogItemViewModel> _setActive;
    private readonly Action<ChannelCatalogItemViewModel> _remove;
    private readonly List<string> _aliases;
    private readonly Dictionary<string, string> _parameters;
    private readonly string _channelUrl;
    private string _savedDisplayName;
    private string _savedHandle;
    private bool _savedMonitoring;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(AutomationSummary), nameof(EditorHeading))]
    private string _displayName;

    [ObservableProperty]
    private string _handle;

    [ObservableProperty]
    private bool _monitoringEnabled;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(AutomationSummary))]
    private bool _isActive;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(AutomationSummary))]
    private bool _isPreselectedNext;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(AutomationSummary))]
    private ChannelCardStatus _runtimeStatus = new("Статус эфира неизвестен", StatusSeverity.Info, null, "Чат: не подключён", StatusSeverity.None);

    public ChannelCatalogItemViewModel(
        ChannelCatalogEntry entry,
        bool isActive,
        bool supportsMonitoring,
        Action onEdited,
        Func<ChannelCatalogItemViewModel, ChannelAccountRoleViewModel, Task> verifyAsync,
        Func<ChannelCatalogItemViewModel, ChannelAccountRoleViewModel, Task> authorizeAsync,
        Action<ChannelCatalogItemViewModel> setActive,
        Action<ChannelCatalogItemViewModel> remove)
    {
        Platform = entry.Platform;
        ChannelId = entry.ChannelId;
        SupportsMonitoring = supportsMonitoring;
        _channelUrl = entry.ChannelUrl;
        _aliases = [.. entry.KnownAliases];
        _parameters = new(entry.Parameters, StringComparer.Ordinal);
        _onEdited = onEdited;
        _setActive = setActive;
        _remove = remove;

        _displayName = string.IsNullOrWhiteSpace(entry.DisplayName) ? entry.ChannelId : entry.DisplayName;
        _handle = entry.Handle;
        _monitoringEnabled = entry.MonitoringEnabled;
        _savedDisplayName = _displayName;
        _savedHandle = _handle;
        _savedMonitoring = _monitoringEnabled;
        _isActive = isActive;
        Roles =
        [
            new(entry, PlatformAuthRole.Bot, onEdited, role => verifyAsync(this, role), role => authorizeAsync(this, role)),
            new(entry, PlatformAuthRole.Broadcaster, onEdited, role => verifyAsync(this, role), role => authorizeAsync(this, role)),
        ];
        foreach (var role in Roles)
        {
            role.PropertyChanged += (_, _) => NotifyRoleSummaries();
        }
    }

    public StreamingPlatform Platform { get; }

    public string ChannelId { get; }

    public string EditorHeading => $"Настройка канала «{DisplayName}»";

    public bool SupportsMonitoring { get; }

    public IReadOnlyList<ChannelAccountRoleViewModel> Roles { get; }

    public string BotSummaryText => DescribeRoleSummary(Roles[0], "Бот", emptyLabel: "не назначен").Text;

    public StatusSeverity BotSummarySeverity => DescribeRoleSummary(Roles[0], "Бот", emptyLabel: "не назначен").Severity;

    public string BroadcasterSummaryText => DescribeRoleSummary(Roles[1], "Стример", emptyLabel: "–").Text;

    public StatusSeverity BroadcasterSummarySeverity => DescribeRoleSummary(Roles[1], "Стример", emptyLabel: "–").Severity;

    public string MonitoringSummaryText => MonitoringEnabled ? "Фоновый мониторинг: вкл." : "Фоновый мониторинг: выкл.";

    public string AutomationSummary => string.Join(", ", new[]
    {
        DisplayName,
        IsActive ? "Активный" : null,
        IsPreselectedNext ? "Следующий" : null,
        RuntimeStatus.BroadcastText,
        RuntimeStatus.ChatText,
        BotSummaryText,
        BroadcasterSummaryText,
        SupportsMonitoring ? MonitoringSummaryText : null,
    }.Where(static part => !string.IsNullOrWhiteSpace(part)));

    internal bool IsNew { get; set; }

    private void NotifyRoleSummaries()
    {
        OnPropertyChanged(nameof(BotSummaryText));
        OnPropertyChanged(nameof(BotSummarySeverity));
        OnPropertyChanged(nameof(BroadcasterSummaryText));
        OnPropertyChanged(nameof(BroadcasterSummarySeverity));
        OnPropertyChanged(nameof(AutomationSummary));
    }

    private static (string Text, StatusSeverity Severity) DescribeRoleSummary(
        ChannelAccountRoleViewModel role, string label, string emptyLabel)
    {
        if (!role.CanManageAccount)
        {
            return (string.Empty, StatusSeverity.None);
        }

        if (string.IsNullOrWhiteSpace(role.AccountId))
        {
            return ($"{label}: {emptyLabel}", role.Role == PlatformAuthRole.Bot ? StatusSeverity.Warning : StatusSeverity.None);
        }

        var name = role.AccountDisplayName ?? role.AccountId;
        if (!role.NeedsVerification && role.StatusSeverity == StatusSeverity.Success)
        {
            return ($"{label}: {name} ✓", StatusSeverity.Success);
        }

        var severity = role.StatusSeverity == StatusSeverity.None && role.NeedsVerification
            ? StatusSeverity.Warning
            : role.StatusSeverity;
        return ($"{label}: {name}", severity);
    }

    internal void AcceptEdits()
    {
        IsNew = false;
        _savedDisplayName = DisplayName;
        _savedHandle = Handle;
        _savedMonitoring = MonitoringEnabled;
        foreach (var role in Roles)
        {
            role.AcceptEdits();
        }
    }

    internal void ApplyEditsTo(ChannelCatalogEntry current)
    {
        if (IsNew || DisplayName != _savedDisplayName)
        {
            current.DisplayName = DisplayName.Trim();
        }

        if (IsNew || Handle != _savedHandle)
        {
            current.Handle = Handle.Trim();
        }

        if (IsNew || MonitoringEnabled != _savedMonitoring)
        {
            current.MonitoringEnabled = MonitoringEnabled;
        }

        foreach (var role in Roles.Where(role => IsNew || role.IsEdited))
        {
            role.ApplyTo(current);
        }
    }

    public ChannelCatalogEntry ToEntry()
    {
        var entry = new ChannelCatalogEntry
        {
            Platform = Platform,
            ChannelId = ChannelId,
            DisplayName = DisplayName.Trim(),
            Handle = Handle.Trim(),
            ChannelUrl = _channelUrl,
            KnownAliases = [.. _aliases],
            IsConfigured = true,
            MonitoringEnabled = MonitoringEnabled,
            Parameters = new(_parameters, StringComparer.Ordinal),
        };
        foreach (var role in Roles)
        {
            role.ApplyTo(entry);
        }
        return entry;
    }

    partial void OnDisplayNameChanged(string value) => _onEdited();
    partial void OnHandleChanged(string value) => _onEdited();
    partial void OnMonitoringEnabledChanged(bool value)
    {
        OnPropertyChanged(nameof(MonitoringSummaryText));
        OnPropertyChanged(nameof(AutomationSummary));
        _onEdited();
    }

    [RelayCommand]
    private void MakeActive() => _setActive(this);

    [RelayCommand]
    private void Remove() => _remove(this);

}
