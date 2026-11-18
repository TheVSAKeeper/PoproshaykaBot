using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using KeepShell.Services.Platform;
using PoproshaykaBot.Core.Platforms;
using PoproshaykaBot.Core.Platforms.Auth;
using PoproshaykaBot.Core.Settings;
using PoproshaykaBot.Core.Settings.Stores;
using PoproshaykaBot.Core.Streaming;
using PoproshaykaBot.Core.Twitch.Auth;
using PoproshaykaBot.Core.Twitch.Chat;
using PoproshaykaBot.Core.YouTube.Auth;
using PoproshaykaBot.Wpf.Infrastructure.Platforms;

namespace PoproshaykaBot.Wpf.ViewModels.Settings;

public sealed record ChannelSwitchModeOption(ChannelSwitchMode Mode, string DisplayName);

public sealed record AuthContextOption(string ContextId, string DisplayName);

public sealed record PreselectedNextChannelOption(string? ChannelId, string DisplayName);

public sealed record AuthContextRoleOption(PlatformAuthRole Role, string DisplayName);

public sealed partial class ChannelCatalogSettingsSectionViewModel : ObservableObject
{
    private readonly ChannelCatalogStore _catalogStore;
    private readonly AccountsStore? _twitchAccountsStore;
    private readonly YouTubeAccountsStore? _youtubeAccountsStore;
    private readonly ITwitchOAuthService? _twitchOAuth;
    private readonly IYouTubeOAuthService? _youtubeOAuth;
    private readonly SettingsManager? _settingsManager;
    private readonly YouTubeSettingsStore? _youtubeSettingsStore;
    private readonly IShellLauncher? _shellLauncher;
    private readonly TimeProvider _timeProvider;
    private readonly Dictionary<StreamingPlatform, IPlatformChannelAdapter> _adapters;
    private readonly List<ChannelCatalogItemViewModel> _allChannels = [];
    private readonly Dictionary<StreamingPlatform, ChannelSwitchMode> _switchModes = [];
    private readonly Dictionary<StreamingPlatform, string?> _initialActiveChannels = [];
    private readonly Dictionary<StreamingPlatform, string?> _draftActiveChannels = [];
    private readonly Dictionary<StreamingPlatform, string?> _initialPreselectedNext = [];
    private readonly Dictionary<StreamingPlatform, string?> _draftPreselectedNext = [];
    private bool _suppressUpdates;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SelectedSwitchModeOption))]
    [NotifyPropertyChangedFor(nameof(IsPreselectedNextMode))]
    [NotifyPropertyChangedFor(nameof(SelectedPreselectedNextOption))]
    [NotifyPropertyChangedFor(nameof(CanAuthorizeAdditionalContext))]
    [NotifyPropertyChangedFor(nameof(HasChannels))]
    private StreamingPlatform _selectedPlatform = StreamingPlatform.Twitch;

    [ObservableProperty]
    private PlatformAuthRole _newContextRole = PlatformAuthRole.Broadcaster;

    [ObservableProperty]
    private string _newContextLabel = string.Empty;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(AuthorizeNewContextCommand))]
    private bool _isAuthorizingContext;

    [ObservableProperty]
    private string _newChannelInput = string.Empty;

    [ObservableProperty]
    private string _statusNotice = string.Empty;

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
        TimeProvider? timeProvider = null)
    {
        ArgumentNullException.ThrowIfNull(catalogStore);
        _catalogStore = catalogStore;
        _twitchAccountsStore = twitchAccountsStore;
        _youtubeAccountsStore = youtubeAccountsStore;
        _twitchOAuth = twitchOAuth;
        _youtubeOAuth = youtubeOAuth;
        _settingsManager = settingsManager;
        _youtubeSettingsStore = youtubeSettingsStore;
        _shellLauncher = shellLauncher;
        _timeProvider = timeProvider ?? TimeProvider.System;
        _adapters = (adapters ?? []).ToDictionary(static a => a.Platform);

        PlatformOptions = PlatformVisuals.OrderedPlatforms
            .Select(static p => new PlatformFilterOption(p, PlatformVisuals.GetDisplayName(p)))
            .ToArray();

        SwitchModeOptions =
        [
            new(ChannelSwitchMode.Manual, "Ручное переключение"),
            new(ChannelSwitchMode.PreselectedNext, "Заранее выбранный следующий канал"),
            new(ChannelSwitchMode.AutoDiscover, "Автопоиск старта на наблюдаемых каналах"),
        ];

        AuthRoleOptions =
        [
            new(PlatformAuthRole.Broadcaster, "Ведущий"),
            new(PlatformAuthRole.Bot, "Бот"),
        ];
    }

    public event EventHandler? Edited;

    public IReadOnlyList<PlatformFilterOption> PlatformOptions { get; }

    public IReadOnlyList<ChannelSwitchModeOption> SwitchModeOptions { get; }

    public IReadOnlyList<AuthContextRoleOption> AuthRoleOptions { get; }

    public bool CanAuthorizeAdditionalContext =>
        _shellLauncher is not null
        && ((SelectedPlatform == StreamingPlatform.Twitch && _twitchOAuth is not null && _twitchAccountsStore is not null && _settingsManager is not null)
            || (SelectedPlatform == StreamingPlatform.YouTube && _youtubeOAuth is not null && _youtubeAccountsStore is not null && _youtubeSettingsStore is not null));

    public ObservableCollection<ChannelCatalogItemViewModel> VisibleChannels { get; } = [];

    public ObservableCollection<AuthContextOption> BroadcasterContextOptions { get; } = [];

    public ObservableCollection<AuthContextOption> BotContextOptions { get; } = [];

    public ObservableCollection<PreselectedNextChannelOption> PreselectedNextOptions { get; } = [];

    public bool IsPreselectedNextMode =>
        _switchModes.GetValueOrDefault(SelectedPlatform, ChannelSwitchMode.Manual) == ChannelSwitchMode.PreselectedNext;

    public PreselectedNextChannelOption SelectedPreselectedNextOption
    {
        get
        {
            var id = _draftPreselectedNext.GetValueOrDefault(SelectedPlatform);
            return PreselectedNextOptions.FirstOrDefault(o => string.Equals(o.ChannelId, id, StringComparison.Ordinal))
                ?? PreselectedNextOptions.FirstOrDefault()
                ?? new(null, "Не выбран");
        }
        set
        {
            var nextId = value?.ChannelId;
            if (string.Equals(_draftPreselectedNext.GetValueOrDefault(SelectedPlatform), nextId, StringComparison.Ordinal))
            {
                return;
            }

            _draftPreselectedNext[SelectedPlatform] = nextId;
            OnPropertyChanged();
            NotifyEdited();
        }
    }

    public bool HasChannels => VisibleChannels.Count > 0;

    public ChannelSwitchModeOption SelectedSwitchModeOption
    {
        get
        {
            var mode = _switchModes.GetValueOrDefault(SelectedPlatform, ChannelSwitchMode.Manual);
            return SwitchModeOptions.FirstOrDefault(o => o.Mode == mode) ?? SwitchModeOptions[0];
        }
        set
        {
            if (value is null)
            {
                return;
            }

            if (_switchModes.GetValueOrDefault(SelectedPlatform, ChannelSwitchMode.Manual) == value.Mode)
            {
                return;
            }

            _switchModes[SelectedPlatform] = value.Mode;
            OnPropertyChanged();
            OnPropertyChanged(nameof(IsPreselectedNextMode));
            NotifyEdited();
        }
    }

    public void Load()
    {
        _suppressUpdates = true;
        try
        {
            _allChannels.Clear();
            _switchModes.Clear();
            _initialActiveChannels.Clear();
            _draftActiveChannels.Clear();
            _initialPreselectedNext.Clear();
            _draftPreselectedNext.Clear();
            StatusNotice = string.Empty;

            var settings = _catalogStore.Load();
            foreach (var platform in PlatformVisuals.OrderedPlatforms)
            {
                var ps = settings.Platforms.FirstOrDefault(p => p.Platform == platform);
                _switchModes[platform] = ps?.SwitchMode ?? ChannelSwitchMode.Manual;
                _initialActiveChannels[platform] = ps?.ActiveChannelId;
                _draftActiveChannels[platform] = ps?.ActiveChannelId;
                _initialPreselectedNext[platform] = ps?.PreselectedNextChannelId;
                _draftPreselectedNext[platform] = ps?.PreselectedNextChannelId;
            }

            foreach (var entry in settings.Channels.Where(static c => c.IsConfigured))
            {
                var isActive = string.Equals(_draftActiveChannels.GetValueOrDefault(entry.Platform), entry.ChannelId, StringComparison.Ordinal);
                _allChannels.Add(new ChannelCatalogItemViewModel(
                    entry,
                    isActive,
                    NotifyEdited,
                    VerifyAccessAsync,
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

    public void ApplyTo(ChannelsSettings target)
    {
        ArgumentNullException.ThrowIfNull(target);

        var preservedNonConfigured = target.Channels
            .Where(static c => !c.IsConfigured)
            .ToDictionary(static c => c.Key);

        var resultChannels = new List<ChannelCatalogEntry>();
        var configuredKeys = new HashSet<ChannelKey>();

        foreach (var item in _allChannels)
        {
            var entry = item.ToEntry();
            configuredKeys.Add(entry.Key);
            if (preservedNonConfigured.TryGetValue(entry.Key, out var historical))
            {
                foreach (var alias in historical.KnownAliases)
                {
                    if (!entry.KnownAliases.Contains(alias, StringComparer.OrdinalIgnoreCase))
                    {
                        entry.KnownAliases.Add(alias);
                    }
                }
            }

            resultChannels.Add(entry);
        }

        foreach (var historical in preservedNonConfigured.Values)
        {
            if (!configuredKeys.Contains(historical.Key))
            {
                resultChannels.Add(historical);
            }
        }

        target.Channels = resultChannels;

        var existingPlatforms = target.Platforms.ToDictionary(static p => p.Platform);
        var updatedPlatforms = new List<PlatformChannelRoutingSettings>();

        foreach (var platform in PlatformVisuals.OrderedPlatforms)
        {
            existingPlatforms.TryGetValue(platform, out var livePlatform);
            var mode = _switchModes.GetValueOrDefault(platform, livePlatform?.SwitchMode ?? ChannelSwitchMode.Manual);
            var initialActive = _initialActiveChannels.GetValueOrDefault(platform);
            var draftActive = _draftActiveChannels.GetValueOrDefault(platform);
            var activeChangedInDraft = !string.Equals(initialActive, draftActive, StringComparison.Ordinal);

            var finalActive = activeChangedInDraft
                ? draftActive
                : (livePlatform?.ActiveChannelId ?? draftActive);

            if (!string.IsNullOrWhiteSpace(finalActive)
                && !resultChannels.Any(c => c.Platform == platform && c.IsConfigured && string.Equals(c.ChannelId, finalActive, StringComparison.Ordinal)))
            {
                finalActive = resultChannels.FirstOrDefault(c => c.Platform == platform && c.IsConfigured)?.ChannelId;
            }

            var initialNext = _initialPreselectedNext.GetValueOrDefault(platform);
            var draftNext = _draftPreselectedNext.GetValueOrDefault(platform);
            var nextChangedInDraft = !string.Equals(initialNext, draftNext, StringComparison.Ordinal);
            var finalNext = nextChangedInDraft
                ? draftNext
                : (livePlatform is not null ? livePlatform.PreselectedNextChannelId : draftNext);

            if (!string.IsNullOrWhiteSpace(finalNext)
                && !resultChannels.Any(c => c.Platform == platform && c.IsConfigured && string.Equals(c.ChannelId, finalNext, StringComparison.Ordinal)))
            {
                finalNext = null;
            }

            updatedPlatforms.Add(new()
            {
                Platform = platform,
                ActiveChannelId = finalActive,
                SwitchMode = mode,
                PreselectedNextChannelId = finalNext,
            });
        }

        foreach (var extra in existingPlatforms.Values)
        {
            if (!PlatformVisuals.OrderedPlatforms.Contains(extra.Platform))
            {
                updatedPlatforms.Add(extra);
            }
        }

        target.Platforms = updatedPlatforms;
    }

    public IReadOnlyList<(StreamingPlatform Platform, ChannelKey Target)> GetPendingActiveChanges()
    {
        var changes = new List<(StreamingPlatform, ChannelKey)>();
        foreach (var platform in PlatformVisuals.OrderedPlatforms)
        {
            var initialActive = _initialActiveChannels.GetValueOrDefault(platform);
            var draftActive = _draftActiveChannels.GetValueOrDefault(platform);
            if (!string.Equals(initialActive, draftActive, StringComparison.Ordinal) && !string.IsNullOrWhiteSpace(draftActive))
            {
                changes.Add((platform, new ChannelKey(platform, draftActive)));
            }
        }

        return changes;
    }

    partial void OnSelectedPlatformChanged(StreamingPlatform value)
    {
        RefreshPlatformView();
    }

    [RelayCommand(CanExecute = nameof(CanAuthorizeContextExecute))]
    private async Task AuthorizeNewContextAsync()
    {
        var platform = SelectedPlatform;
        var role = NewContextRole;
        var customLabel = NewContextLabel.Trim();
        var launcher = _shellLauncher;
        if (launcher is null)
        {
            return;
        }

        IsAuthorizingContext = true;
        StatusNotice = string.Empty;
        try
        {
            if (platform == StreamingPlatform.Twitch && _twitchOAuth is not null && _twitchAccountsStore is not null && _settingsManager is not null)
            {
                var twitchSettings = _settingsManager.Current.Twitch;
                string[] scopes = role == PlatformAuthRole.Bot
                    ? [.. TwitchScopes.BotRequired, .. TwitchScopes.BotOptional]
                    : [.. TwitchScopes.BroadcasterRequired];
                var flow = await _twitchOAuth.StartOAuthFlowToDraftAsync(
                    role,
                    twitchSettings.ClientId,
                    twitchSettings.ClientSecret,
                    scopes,
                    twitchSettings.RedirectUri,
                    url => launcher.Open(url)).ConfigureAwait(true);

                var contextId = $"tw-{role.ToString().ToLowerInvariant()}-{flow.UserId}";
                var label = string.IsNullOrWhiteSpace(customLabel) ? flow.Login : customLabel;
                var account = new TwitchAccountSettings();
                flow.ApplyTo(account, _timeProvider.GetUtcNow());
                _twitchAccountsStore.UpsertContext(role, contextId, label, account);
                NewContextLabel = string.Empty;
                ReloadContextOptions(platform);
                StatusNotice = $"Добавлен контекст Twitch ({label}).";
            }
            else if (platform == StreamingPlatform.YouTube && _youtubeOAuth is not null && _youtubeAccountsStore is not null && _youtubeSettingsStore is not null)
            {
                var ytSettings = _youtubeSettingsStore.Load();
                var clientSecret = _youtubeAccountsStore.LoadClientSecret();
                string[] scopes = [.. YouTubeScopes.RequiredFor(role)];
                var flow = await _youtubeOAuth.StartOAuthFlowToDraftAsync(
                    role,
                    ytSettings.ClientId,
                    clientSecret,
                    scopes,
                    url => launcher.Open(url)).ConfigureAwait(true);

                var suffix = string.IsNullOrWhiteSpace(flow.ChannelId) ? Guid.NewGuid().ToString("N")[..8] : flow.ChannelId;
                var contextId = $"yt-{role.ToString().ToLowerInvariant()}-{suffix}";
                var label = string.IsNullOrWhiteSpace(customLabel)
                    ? (string.IsNullOrWhiteSpace(flow.ChannelTitle) ? contextId : flow.ChannelTitle)
                    : customLabel;
                var account = new YouTubeAccountSettings();
                flow.ApplyTo(account, _timeProvider.GetUtcNow());
                _youtubeAccountsStore.UpsertContext(role, contextId, label, account);
                NewContextLabel = string.Empty;
                ReloadContextOptions(platform);
                StatusNotice = $"Добавлен контекст YouTube ({label}).";
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            StatusNotice = $"Не удалось авторизовать дополнительный контекст: {StreamingErrorMessages.SafeMessage(ex)}";
        }
        finally
        {
            IsAuthorizingContext = false;
        }
    }

    private bool CanAuthorizeContextExecute() => !IsAuthorizingContext && CanAuthorizeAdditionalContext;

    [RelayCommand]
    private async Task AddChannelAsync()
    {
        var input = NewChannelInput.Trim();
        if (string.IsNullOrWhiteSpace(input))
        {
            return;
        }

        var targetPlatform = SelectedPlatform;
        StatusNotice = string.Empty;
        ResolvedChannelIdentity? resolved = null;
        if (_adapters.TryGetValue(targetPlatform, out var adapter))
        {
            try
            {
                resolved = await adapter.ResolveChannelIdentityAsync(input, AuthContextIds.Default, CancellationToken.None).ConfigureAwait(true);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                StatusNotice = $"Не удалось разрешить канал: {StreamingErrorMessages.SafeMessage(ex)}";
                return;
            }

            if (resolved is null)
            {
                StatusNotice = "Не удалось определить идентификатор канала. Проверьте ввод и авторизацию площадки.";
                return;
            }
        }

        var channelId = resolved?.Channel.ChannelId ?? input;
        var displayName = resolved?.DisplayName ?? input;
        var handle = resolved?.Handle ?? input;
        var url = resolved?.ChannelUrl ?? string.Empty;

        var existing = _allChannels.FirstOrDefault(c =>
            c.Platform == targetPlatform && string.Equals(c.ChannelId, channelId, StringComparison.Ordinal));
        if (existing is not null)
        {
            SelectedChannel = existing;
            StatusNotice = $"Канал «{existing.DisplayName}» уже добавлен.";
            NewChannelInput = string.Empty;
            return;
        }

        var entry = new ChannelCatalogEntry
        {
            Platform = targetPlatform,
            ChannelId = channelId,
            DisplayName = displayName,
            Handle = handle,
            ChannelUrl = url,
            KnownAliases = [displayName],
            IsConfigured = true,
            MonitoringEnabled = true,
            BroadcasterContextId = AuthContextIds.Default,
            BotContextId = AuthContextIds.Default,
        };

        var isFirstForPlatform = !_allChannels.Any(c => c.Platform == targetPlatform);
        if (isFirstForPlatform)
        {
            _draftActiveChannels[targetPlatform] = channelId;
        }

        var vm = new ChannelCatalogItemViewModel(
            entry,
            isFirstForPlatform,
            NotifyEdited,
            VerifyAccessAsync,
            SetActiveInDraft,
            RemoveChannel);
        _allChannels.Add(vm);
        NewChannelInput = string.Empty;
        RefreshPlatformView();
        SelectedChannel = vm;
        NotifyEdited();
    }

    private async Task VerifyAccessAsync(ChannelCatalogItemViewModel item, PlatformAuthRole role)
    {
        if (!_adapters.TryGetValue(item.Platform, out var adapter))
        {
            item.SetVerificationResult(role, false, "Адаптер площадки не зарегистрирован.");
            return;
        }

        var requestedContextId = role == PlatformAuthRole.Broadcaster ? item.BroadcasterContextId : item.BotContextId;
        var result = await adapter.VerifyChannelAccessAsync(new(item.Platform, item.ChannelId), role, requestedContextId, CancellationToken.None).ConfigureAwait(true);
        var currentContextId = role == PlatformAuthRole.Broadcaster ? item.BroadcasterContextId : item.BotContextId;
        if (!string.Equals(requestedContextId, currentContextId, StringComparison.Ordinal))
        {
            return;
        }

        item.SetVerificationResult(role, result.IsAllowed, result.IsAllowed ? "Доступ подтверждён" : (result.FailureReason ?? "Нет доступа"), result.SubjectId);
        if (result.IsAllowed)
        {
            NotifyEdited();
        }
    }

    private void SetActiveInDraft(ChannelCatalogItemViewModel item)
    {
        _draftActiveChannels[item.Platform] = item.ChannelId;
        foreach (var candidate in _allChannels.Where(c => c.Platform == item.Platform))
        {
            candidate.IsActive = ReferenceEquals(candidate, item);
        }

        NotifyEdited();
    }

    private void RemoveChannel(ChannelCatalogItemViewModel item)
    {
        _allChannels.Remove(item);
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
        VisibleChannels.Clear();
        foreach (var item in _allChannels.Where(c => c.Platform == SelectedPlatform))
        {
            VisibleChannels.Add(item);
        }

        ReloadContextOptions(SelectedPlatform);
        ReloadPreselectedNextOptions(SelectedPlatform);
        SelectedChannel = VisibleChannels.FirstOrDefault();
        OnPropertyChanged(nameof(HasChannels));
        OnPropertyChanged(nameof(SelectedSwitchModeOption));
        OnPropertyChanged(nameof(IsPreselectedNextMode));
        OnPropertyChanged(nameof(SelectedPreselectedNextOption));
    }

    private void ReloadPreselectedNextOptions(StreamingPlatform platform)
    {
        PreselectedNextOptions.Clear();
        PreselectedNextOptions.Add(new(null, "Не выбран"));
        foreach (var item in _allChannels.Where(c => c.Platform == platform))
        {
            PreselectedNextOptions.Add(new(item.ChannelId, $"{item.DisplayName} ({item.ChannelId})"));
        }
    }

    private void ReloadContextOptions(StreamingPlatform platform)
    {
        BroadcasterContextOptions.Clear();
        BotContextOptions.Clear();

        IReadOnlyList<AuthContextSummary> broadcasterContexts = platform switch
        {
            StreamingPlatform.Twitch when _twitchAccountsStore is not null => _twitchAccountsStore.ListContexts(PlatformAuthRole.Broadcaster),
            StreamingPlatform.YouTube when _youtubeAccountsStore is not null => _youtubeAccountsStore.ListContexts(PlatformAuthRole.Broadcaster),
            _ => [new(AuthContextIds.Default, PlatformAuthRole.Broadcaster, "Основной", null, null, false, true)],
        };

        IReadOnlyList<AuthContextSummary> botContexts = platform switch
        {
            StreamingPlatform.Twitch when _twitchAccountsStore is not null => _twitchAccountsStore.ListContexts(PlatformAuthRole.Bot),
            StreamingPlatform.YouTube when _youtubeAccountsStore is not null => _youtubeAccountsStore.ListContexts(PlatformAuthRole.Bot),
            _ => [new(AuthContextIds.Default, PlatformAuthRole.Bot, "Основной", null, null, false, true)],
        };

        foreach (var ctx in broadcasterContexts)
        {
            var title = string.IsNullOrWhiteSpace(ctx.DisplayLabel) ? ctx.Label : $"{ctx.Label} ({ctx.DisplayLabel})";
            BroadcasterContextOptions.Add(new(ctx.ContextId, title));
        }

        foreach (var ctx in botContexts)
        {
            var title = string.IsNullOrWhiteSpace(ctx.DisplayLabel) ? ctx.Label : $"{ctx.Label} ({ctx.DisplayLabel})";
            BotContextOptions.Add(new(ctx.ContextId, title));
        }
    }

    private void NotifyEdited()
    {
        if (!_suppressUpdates)
        {
            Edited?.Invoke(this, EventArgs.Empty);
        }
    }
}

public sealed partial class ChannelCatalogItemViewModel : ObservableObject
{
    private readonly Action _onEdited;
    private readonly Func<ChannelCatalogItemViewModel, PlatformAuthRole, Task> _verifyAsync;
    private readonly Action<ChannelCatalogItemViewModel> _setActive;
    private readonly Action<ChannelCatalogItemViewModel> _remove;
    private readonly List<string> _aliases;
    private readonly Dictionary<string, string> _parameters;
    private readonly string _channelUrl;
    private string? _broadcasterVerifiedSubjectId;
    private string? _botVerifiedSubjectId;
    private bool _suppressNotify;

    [ObservableProperty]
    private string _displayName;

    [ObservableProperty]
    private string _handle;

    [ObservableProperty]
    private bool _monitoringEnabled;

    [ObservableProperty]
    private string _broadcasterContextId;

    [ObservableProperty]
    private string _botContextId;

    [ObservableProperty]
    private bool _isActive;

    [ObservableProperty]
    private string _broadcasterVerificationStatus;

    [ObservableProperty]
    private string _botVerificationStatus;

    public ChannelCatalogItemViewModel(
        ChannelCatalogEntry entry,
        bool isActive,
        Action onEdited,
        Func<ChannelCatalogItemViewModel, PlatformAuthRole, Task> verifyAsync,
        Action<ChannelCatalogItemViewModel> setActive,
        Action<ChannelCatalogItemViewModel> remove)
    {
        Platform = entry.Platform;
        ChannelId = entry.ChannelId;
        _channelUrl = entry.ChannelUrl;
        _aliases = [.. entry.KnownAliases];
        _parameters = new(entry.Parameters, StringComparer.Ordinal);
        _broadcasterVerifiedSubjectId = entry.BroadcasterVerifiedSubjectId;
        _botVerifiedSubjectId = entry.BotVerifiedSubjectId;
        _onEdited = onEdited;
        _verifyAsync = verifyAsync;
        _setActive = setActive;
        _remove = remove;

        _suppressNotify = true;
        _displayName = string.IsNullOrWhiteSpace(entry.DisplayName) ? entry.ChannelId : entry.DisplayName;
        _handle = entry.Handle;
        _monitoringEnabled = entry.MonitoringEnabled;
        _broadcasterContextId = AuthContextIds.Normalize(entry.BroadcasterContextId);
        _botContextId = AuthContextIds.Normalize(entry.BotContextId);
        _isActive = isActive;
        _broadcasterVerificationStatus = string.IsNullOrWhiteSpace(_broadcasterVerifiedSubjectId) ? "Не проверен" : "Проверен";
        _botVerificationStatus = string.IsNullOrWhiteSpace(_botVerifiedSubjectId) ? "Не проверен" : "Проверен";
        _suppressNotify = false;
    }

    public StreamingPlatform Platform { get; }

    public string ChannelId { get; }

    public void SetVerificationResult(PlatformAuthRole role, bool allowed, string statusText, string? subjectId = null)
    {
        if (role == PlatformAuthRole.Broadcaster)
        {
            _broadcasterVerifiedSubjectId = allowed ? subjectId : null;
            BroadcasterVerificationStatus = statusText;
        }
        else
        {
            _botVerifiedSubjectId = allowed ? subjectId : null;
            BotVerificationStatus = statusText;
        }
    }

    public ChannelCatalogEntry ToEntry()
    {
        return new()
        {
            Platform = Platform,
            ChannelId = ChannelId,
            DisplayName = DisplayName.Trim(),
            Handle = Handle.Trim(),
            ChannelUrl = _channelUrl,
            KnownAliases = [.. _aliases],
            IsConfigured = true,
            MonitoringEnabled = MonitoringEnabled,
            BroadcasterContextId = AuthContextIds.Normalize(BroadcasterContextId),
            BroadcasterVerifiedSubjectId = _broadcasterVerifiedSubjectId,
            BotContextId = AuthContextIds.Normalize(BotContextId),
            BotVerifiedSubjectId = _botVerifiedSubjectId,
            Parameters = new(_parameters, StringComparer.Ordinal),
        };
    }

    partial void OnDisplayNameChanged(string value) => NotifyEdited();
    partial void OnHandleChanged(string value) => NotifyEdited();
    partial void OnMonitoringEnabledChanged(bool value) => NotifyEdited();

    partial void OnBroadcasterContextIdChanged(string value)
    {
        _broadcasterVerifiedSubjectId = null;
        BroadcasterVerificationStatus = "Требуется проверка";
        NotifyEdited();
    }

    partial void OnBotContextIdChanged(string value)
    {
        _botVerifiedSubjectId = null;
        BotVerificationStatus = "Требуется проверка";
        NotifyEdited();
    }

    [RelayCommand]
    private Task VerifyBroadcasterAsync() => _verifyAsync(this, PlatformAuthRole.Broadcaster);

    [RelayCommand]
    private Task VerifyBotAsync() => _verifyAsync(this, PlatformAuthRole.Bot);

    [RelayCommand]
    private void MakeActive() => _setActive(this);

    [RelayCommand]
    private void Remove() => _remove(this);

    private void NotifyEdited()
    {
        if (!_suppressNotify)
        {
            _onEdited();
        }
    }
}
