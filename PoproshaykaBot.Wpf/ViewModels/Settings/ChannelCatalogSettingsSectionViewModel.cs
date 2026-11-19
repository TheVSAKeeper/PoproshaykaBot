using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using KeepShell.Services.Platform;
using KeepShell.ViewModels;
using Microsoft.Extensions.Logging;
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
    private readonly ChannelCatalogStore _catalogStore;
    private readonly AccountsStore? _twitchAccountsStore;
    private readonly YouTubeAccountsStore? _youtubeAccountsStore;
    private readonly ITwitchOAuthService? _twitchOAuth;
    private readonly IYouTubeOAuthService? _youtubeOAuth;
    private readonly SettingsManager? _settingsManager;
    private readonly YouTubeSettingsStore? _youtubeSettingsStore;
    private readonly IShellLauncher? _shellLauncher;
    private readonly TimeProvider _timeProvider;
    private readonly IEmbeddedTwitchAuthDialog? _embeddedTwitchAuth;
    private readonly ILogger<ChannelCatalogSettingsSectionViewModel>? _logger;
    private readonly Dictionary<StreamingPlatform, IPlatformChannelAdapter> _adapters;
    private readonly List<ChannelCatalogItemViewModel> _allChannels = [];
    private readonly Dictionary<StreamingPlatform, ChannelSwitchMode> _switchModes = [];
    private readonly Dictionary<StreamingPlatform, string?> _initialActiveChannels = [];
    private readonly Dictionary<StreamingPlatform, string?> _draftActiveChannels = [];
    private readonly Dictionary<StreamingPlatform, string?> _initialPreselectedNext = [];
    private readonly Dictionary<StreamingPlatform, string?> _draftPreselectedNext = [];
    private bool _suppressUpdates;
    private int _draftGeneration;
    private OAuthSettingsViewModel? _twitchCredentials;
    private YouTubeSettingsSectionViewModel? _youtubeCredentials;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SelectedSwitchModeOption))]
    [NotifyPropertyChangedFor(nameof(IsPreselectedNextMode))]
    [NotifyPropertyChangedFor(nameof(SelectedPreselectedNextOption))]
    [NotifyCanExecuteChangedFor(nameof(AuthorizeOwnChannelCommand))]
    [NotifyPropertyChangedFor(nameof(HasChannels))]
    private StreamingPlatform _selectedPlatform = StreamingPlatform.Twitch;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(AuthorizeOwnChannelCommand))]
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
        ILogger<ChannelCatalogSettingsSectionViewModel>? logger = null)
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
        _embeddedTwitchAuth = embeddedTwitchAuth;
        _logger = logger;
        _adapters = (adapters ?? []).ToDictionary(static a => a.Platform);

        SwitchModeOptions =
        [
            new(ChannelSwitchMode.Manual, "Ручное переключение"),
            new(ChannelSwitchMode.PreselectedNext, "Заранее выбранный следующий канал"),
            new(ChannelSwitchMode.AutoDiscover, "Автопоиск старта на наблюдаемых каналах"),
        ];

    }

    public event EventHandler? Edited;

    internal void SetCredentialDrafts(OAuthSettingsViewModel? twitch, YouTubeSettingsSectionViewModel? youtube)
    {
        _twitchCredentials = twitch;
        _youtubeCredentials = youtube;
    }

    public IReadOnlyList<ChannelSwitchModeOption> SwitchModeOptions { get; }

    private bool CanAuthorizeAccount(StreamingPlatform platform) =>
        _shellLauncher is not null
        && ((platform == StreamingPlatform.Twitch && _twitchOAuth is not null && _twitchAccountsStore is not null && _settingsManager is not null)
            || (platform == StreamingPlatform.YouTube && _youtubeOAuth is not null && _youtubeAccountsStore is not null && _youtubeSettingsStore is not null));

    public ObservableCollection<ChannelCatalogItemViewModel> VisibleChannels { get; } = [];

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
            if (value is null)
            {
                return;
            }

            var nextId = value.ChannelId;
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
        _draftGeneration++;
        _suppressUpdates = true;
        try
        {
            _allChannels.Clear();
            _switchModes.Clear();
            _initialActiveChannels.Clear();
            _draftActiveChannels.Clear();
            _initialPreselectedNext.Clear();
            _draftPreselectedNext.Clear();
            StatusNotice = null;

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

    public string? ValidateAssignments()
    {
        foreach (var channel in _allChannels)
        {
            var pending = channel.Roles.FirstOrDefault(role => role.NeedsVerification);
            if (pending is not null)
            {
                return $"Проверьте доступ аккаунта «{pending.Title}» на канале «{channel.DisplayName}» ({PlatformVisuals.GetDisplayName(channel.Platform)}).";
            }
        }

        return null;
    }

    partial void OnSelectedPlatformChanged(StreamingPlatform value)
    {
        RefreshPlatformView();
    }

    private bool CanAuthorizeOwnChannel() => !IsAuthorizingAccount && CanAuthorizeAccount(SelectedPlatform);

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
        var role = item.Roles[0];
        role.Assign(account.OwnChannel.Channel.ChannelId, account.ContextId);
        await VerifyAccessAsync(item, role);
    }

    private async Task AuthorizeRoleAsync(ChannelCatalogItemViewModel item, ChannelAccountRoleViewModel role)
    {
        var selectedAccountId = role.AccountId;
        var selectedContextId = role.ContextId;
        var account = await AuthorizeAccountAsync(item.Platform, role.Role, role.SetStatus);
        if (account is null || !_allChannels.Contains(item)
            || selectedAccountId != role.AccountId || selectedContextId != role.ContextId)
        {
            return;
        }

        role.Assign(account.OwnChannel.Channel.ChannelId, account.ContextId);
        await VerifyAccessAsync(item, role);
    }

    private async Task<AuthorizedAccount?> AuthorizeAccountAsync(StreamingPlatform platform, PlatformAuthRole role, Action<string, StatusSeverity> report)
    {
        if (IsAuthorizingAccount)
        {
            report("Дождитесь завершения текущего входа.", StatusSeverity.Info);
            return null;
        }

        IsAuthorizingAccount = true;
        report("Ожидаем вход на площадке…", StatusSeverity.Info);
        _logger?.ChannelAuthorizationStarted(platform, role);
        try
        {
            var account = platform switch
            {
                StreamingPlatform.Twitch => await AuthorizeTwitchAsync(role, report),
                StreamingPlatform.YouTube => await AuthorizeYouTubeAsync(role, report),
                _ => null,
            };
            if (account is null)
            {
                _logger?.ChannelAuthorizationIncomplete(platform, role);
            }
            else
            {
                RefreshAccountOptions(platform);
            }
            return account;
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

    private async Task<AuthorizedAccount?> AuthorizeTwitchAsync(PlatformAuthRole role, Action<string, StatusSeverity> report)
    {
        if (_settingsManager is null || _twitchOAuth is null || _twitchAccountsStore is null || _shellLauncher is null)
        {
            return null;
        }

        var settings = _settingsManager.Current.Twitch;
        if (_twitchCredentials is { } twitch
            && (twitch.ClientId.Trim() != settings.ClientId || twitch.ClientSecret.Trim() != settings.ClientSecret
                || twitch.RedirectUri.Trim() != settings.RedirectUri))
        {
            report("Сначала сохраните новые реквизиты в блоке «Приложение Twitch», затем повторите вход.", StatusSeverity.Warning);
            return null;
        }

        if (string.IsNullOrWhiteSpace(settings.ClientId) || string.IsNullOrWhiteSpace(settings.ClientSecret))
        {
            report("Заполните и сохраните ключи в блоке «Приложение Twitch».", StatusSeverity.Warning);
            return null;
        }

        string[] scopes = role == PlatformAuthRole.Bot
            ? [.. TwitchScopes.BotRequired, .. TwitchScopes.BotOptional]
            : [.. TwitchScopes.BroadcasterRequired];
        OAuthFlowResult? flow = null;
        if (_embeddedTwitchAuth is not null)
        {
            var embedded = await _embeddedTwitchAuth.AuthorizeAsync(new(role, settings.ClientId, settings.ClientSecret,
                scopes, settings.RedirectUri, CheckBroadcasterChannel: false));
            if (embedded.Outcome is EmbeddedTwitchAuthOutcome.Canceled or EmbeddedTwitchAuthOutcome.Failed)
            {
                report(embedded.Message, embedded.Outcome == EmbeddedTwitchAuthOutcome.Failed ? StatusSeverity.Error : StatusSeverity.Info);
                return null;
            }

            flow = embedded.Flow;
        }

        flow ??= await _twitchOAuth.StartOAuthFlowToDraftAsync(role, settings.ClientId, settings.ClientSecret, scopes,
            settings.RedirectUri, url => _shellLauncher.Open(url), checkBroadcasterChannel: false);
        if (string.IsNullOrWhiteSpace(flow.UserId))
        {
            report("Twitch не сообщил ID аккаунта.", StatusSeverity.Error);
            return null;
        }

        var id = flow.UserId.Trim();
        var contextId = FindGrant(StreamingPlatform.Twitch, role, id) ?? $"tw-{role.ToString().ToLowerInvariant()}-{id}";
        var account = new TwitchAccountSettings();
        flow.ApplyTo(account, _timeProvider.GetUtcNow());
        var key = new ChannelKey(StreamingPlatform.Twitch, id);
        return CompleteAuthorization(_twitchAccountsStore.UpsertContext(role, contextId, flow.Login, account),
            contextId, new(key, flow.Login, flow.Login, $"https://twitch.tv/{flow.Login}"), report);
    }

    private async Task<AuthorizedAccount?> AuthorizeYouTubeAsync(PlatformAuthRole role, Action<string, StatusSeverity> report)
    {
        if (_youtubeSettingsStore is null || _youtubeOAuth is null || _youtubeAccountsStore is null || _shellLauncher is null)
        {
            return null;
        }

        var settings = _youtubeSettingsStore.Load();
        var secret = _youtubeAccountsStore.LoadClientSecret();
        if (_youtubeCredentials is { } youtube
            && (youtube.ClientId.Trim() != settings.ClientId || youtube.ClientSecret.Trim() != secret))
        {
            report("Сначала сохраните новые реквизиты в блоке «Google-проект», затем повторите вход.", StatusSeverity.Warning);
            return null;
        }

        if (string.IsNullOrWhiteSpace(settings.ClientId) || string.IsNullOrWhiteSpace(secret))
        {
            report("Заполните и сохраните ключи в блоке «Google-проект».", StatusSeverity.Warning);
            return null;
        }

        var flow = await _youtubeOAuth.StartOAuthFlowToDraftAsync(role, settings.ClientId, secret,
            YouTubeScopes.RequiredFor(role).ToArray(), url => _shellLauncher.Open(url));
        if (string.IsNullOrWhiteSpace(flow.ChannelId))
        {
            report("YouTube не сообщил ID авторизованного канала.", StatusSeverity.Error);
            return null;
        }

        var id = flow.ChannelId.Trim();
        var name = string.IsNullOrWhiteSpace(flow.ChannelTitle) ? id : flow.ChannelTitle;
        var contextId = FindGrant(StreamingPlatform.YouTube, role, id) ?? $"yt-{role.ToString().ToLowerInvariant()}-{id}";
        var account = new YouTubeAccountSettings();
        flow.ApplyTo(account, _timeProvider.GetUtcNow());
        return CompleteAuthorization(_youtubeAccountsStore.UpsertContext(role, contextId, name, account),
            contextId, new(new(StreamingPlatform.YouTube, id), name, id, $"https://youtube.com/channel/{id}"), report);
    }

    private static AuthorizedAccount? CompleteAuthorization(bool written, string contextId, ResolvedChannelIdentity ownChannel, Action<string, StatusSeverity> report)
    {
        if (!written)
        {
            report("Аккаунт действует до перезапуска. Записать авторизацию в файл не удалось.", StatusSeverity.Warning);
            return null;
        }

        report($"Аккаунт «{ownChannel.DisplayName}» подключён.", StatusSeverity.Success);
        return new(contextId, ownChannel);
    }

    private void SetNotice(string message, StatusSeverity severity)
    {
        StatusNoticeSeverity = severity;
        StatusNotice = message;
    }

    private string? FindGrant(StreamingPlatform platform, PlatformAuthRole role, string accountId) => ListAccounts(platform)
        .FirstOrDefault(account => account.Key.AccountId == accountId)?.Authorizations
        .FirstOrDefault(grant => grant.Role == role && !grant.IsDefault)?.ContextId;

    private sealed record AuthorizedAccount(string ContextId, ResolvedChannelIdentity OwnChannel);

    [RelayCommand]
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
            var contextId = ListAccounts(targetPlatform).SelectMany(account => account.Authorizations)
                .FirstOrDefault(grant => grant.HasToken
                    && (targetPlatform != StreamingPlatform.YouTube || grant.Role == PlatformAuthRole.Broadcaster))?.ContextId;
            resolved = await adapter.ResolveChannelIdentityAsync(input, contextId, CancellationToken.None).ConfigureAwait(true);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
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
            MonitoringEnabled = true,
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
            NotifyEdited,
            VerifyAccessAsync,
            AuthorizeRoleAsync,
            SetActiveInDraft,
            RemoveChannel);
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

        RefreshAccountOptions(SelectedPlatform);
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

    private IReadOnlyList<PlatformAccount> ListAccounts(StreamingPlatform platform) => platform switch
    {
        StreamingPlatform.Twitch => _twitchAccountsStore?.ListAccounts() ?? [],
        StreamingPlatform.YouTube => _youtubeAccountsStore?.ListAccounts() ?? [],
        _ => [],
    };

    private void RefreshAccountOptions(StreamingPlatform platform)
    {
        var accounts = ListAccounts(platform);
        var canAuthorize = CanAuthorizeAccount(platform);
        foreach (var item in _allChannels.Where(item => item.Platform == platform))
        {
            foreach (var role in item.Roles)
            {
                role.RefreshAccounts(accounts, canAuthorize);
            }
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
    private readonly Action<ChannelCatalogItemViewModel> _setActive;
    private readonly Action<ChannelCatalogItemViewModel> _remove;
    private readonly List<string> _aliases;
    private readonly Dictionary<string, string> _parameters;
    private readonly string _channelUrl;

    [ObservableProperty]
    private string _displayName;

    [ObservableProperty]
    private string _handle;

    [ObservableProperty]
    private bool _monitoringEnabled;

    [ObservableProperty]
    private bool _isActive;

    public ChannelCatalogItemViewModel(
        ChannelCatalogEntry entry,
        bool isActive,
        Action onEdited,
        Func<ChannelCatalogItemViewModel, ChannelAccountRoleViewModel, Task> verifyAsync,
        Func<ChannelCatalogItemViewModel, ChannelAccountRoleViewModel, Task> authorizeAsync,
        Action<ChannelCatalogItemViewModel> setActive,
        Action<ChannelCatalogItemViewModel> remove)
    {
        Platform = entry.Platform;
        ChannelId = entry.ChannelId;
        _channelUrl = entry.ChannelUrl;
        _aliases = [.. entry.KnownAliases];
        _parameters = new(entry.Parameters, StringComparer.Ordinal);
        _onEdited = onEdited;
        _setActive = setActive;
        _remove = remove;

        _displayName = string.IsNullOrWhiteSpace(entry.DisplayName) ? entry.ChannelId : entry.DisplayName;
        _handle = entry.Handle;
        _monitoringEnabled = entry.MonitoringEnabled;
        _isActive = isActive;
        Roles =
        [
            new(entry, PlatformAuthRole.Broadcaster, onEdited, role => verifyAsync(this, role), role => authorizeAsync(this, role)),
            new(entry, PlatformAuthRole.Bot, onEdited, role => verifyAsync(this, role), role => authorizeAsync(this, role)),
        ];
    }

    public StreamingPlatform Platform { get; }

    public string ChannelId { get; }

    public IReadOnlyList<ChannelAccountRoleViewModel> Roles { get; }

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
    partial void OnMonitoringEnabledChanged(bool value) => _onEdited();

    [RelayCommand]
    private void MakeActive() => _setActive(this);

    [RelayCommand]
    private void Remove() => _remove(this);

}
