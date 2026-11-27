using KeepShell.Bootstrap;
using KeepShell.Diagnostics;
using KeepShell.Updates;
using Microsoft.Extensions.Logging;
using Microsoft.Web.WebView2.Core;
using PoproshaykaBot.Core.Broadcast;
using PoproshaykaBot.Core.Broadcast.Profiles;
using PoproshaykaBot.Core.Chat;
using PoproshaykaBot.Core.Chat.Display;
using PoproshaykaBot.Core.Debugging;
using PoproshaykaBot.Core.Diagnostics;
using PoproshaykaBot.Core.Infrastructure;
using PoproshaykaBot.Core.Infrastructure.Diagnostics;
using PoproshaykaBot.Core.Infrastructure.Events.Lifecycle;
using PoproshaykaBot.Core.Infrastructure.Hosting;
using PoproshaykaBot.Core.Infrastructure.Runtime;
using PoproshaykaBot.Core.Obs;
using PoproshaykaBot.Core.Platforms;
using PoproshaykaBot.Core.Platforms.Auth;
using PoproshaykaBot.Core.Polls;
using PoproshaykaBot.Core.Server;
using PoproshaykaBot.Core.Settings;
using PoproshaykaBot.Core.Settings.Onboarding;
using PoproshaykaBot.Core.Settings.Stores;
using PoproshaykaBot.Core.Statistics;
using PoproshaykaBot.Core.Streaming;
using PoproshaykaBot.Core.Streaming.Switching;
using PoproshaykaBot.Core.YouTube.Auth;
using PoproshaykaBot.Core.Dashboard;
using PoproshaykaBot.Wpf.Bootstrap;
using PoproshaykaBot.Wpf.Infrastructure;
using PoproshaykaBot.Wpf.Infrastructure.Diagnostics;
using PoproshaykaBot.Wpf.ViewModels;

namespace PoproshaykaBot.Wpf.Mcp;

public sealed class BotAutomation(
    IBotConnectionController connectionManager,
    ITargetChannelProvider targetChannelProvider,
    IStreamStatus streamStatus,
    IHttpServerStatus httpServer,
    IObsSceneController obsIntegration,
    SettingsManager settingsManager,
    ILogger<BotAutomation> logger,
    IChatPlatformRegistry? platformRegistry = null,
    IBroadcastState? broadcastState = null,
    DiagnosticsSnapshotSource? diagnosticsSource = null,
    ChannelCatalogStore? channelCatalog = null,
    IEnumerable<IPlatformChannelMonitor>? channelMonitors = null,
    IMcpConnectNotificationGate? connectNotificationGate = null,
    IChannelSwitchCoordinator? channelSwitchCoordinator = null,
    PreviousRunState? previousRun = null,
    UnobservedTaskExceptionTracker? unobservedExceptions = null,
    BindingErrorSink? bindingErrors = null,
    IUpdateEnvironment? updateEnvironment = null,
    KeepShell.Updates.IUpdateCoordinator? updateCoordinator = null,
    ChatDisplayStore? chatDisplayStore = null,
    UserStatisticsLoader? userStatisticsLoader = null,
    UserStatisticsRepository? userStatisticsRepository = null,
    BotStatisticsRepository? botStatisticsRepository = null,
    BroadcastHistoryStore? broadcastHistoryStore = null,
    ActiveBroadcastStore? activeBroadcastStore = null,
    BroadcastProfilesManager? broadcastProfilesManager = null,
    BroadcastScheduler? broadcastScheduler = null,
    PollHistoryStore? pollHistoryStore = null,
    PollSnapshotStore? pollSnapshotStore = null,
    PollsAvailabilityService? pollsAvailability = null,
    PollsStore? pollsStore = null,
    DashboardLayoutCoordinator? dashboardLayoutCoordinator = null,
    IEnumerable<ISettingsSnapshotContributor>? settingsSnapshotContributors = null,
    OnboardingChecklist? onboardingChecklist = null,
    IEnumerable<IPlatformAccountDirectory>? accountDirectories = null,
    ChatHistoryManager? chatHistoryManager = null,
    ChatCommandProcessor? commandProcessor = null,
    CommandSettingsStore? commandSettingsStore = null,
    CommandUsageRepository? commandUsageRepository = null,
    CommandResponseTracker? commandResponseTracker = null,
    ObsIntegrationStore? obsIntegrationStore = null,
    ObsIntegrationService? obsIntegrationService = null,
    StatisticsAutoSaver? statisticsAutoSaver = null,
    DiagnosticsCollector? diagnosticsCollector = null,
    TimeProvider? timeProvider = null,
    IEnumerable<IDiagnosticsSecretSource>? diagnosticsSecretSources = null)
{
    private const int MaxBindingErrorLines = 30;
    private const int MaxBindingMessageLength = 240;
    private const int MaxChatHistoryLimit = 200;
    private const int MaxRecentItemsLimit = 50;
    private const string ConnectionAlreadyInProgressError = "Connection is already in progress";
    private const string IncompleteRedactionMarker = "часть правил вымарывания получить не удалось";

    private readonly TimeProvider _timeProvider = timeProvider ?? TimeProvider.System;
    private readonly IReadOnlyList<IDiagnosticsSecretSource>? _diagnosticsSecretSources = diagnosticsSecretSources?.ToArray();

    public bool IsSupportedPlatform(StreamingPlatform platform)
    {
        if (platformRegistry is not null)
        {
            return platformRegistry.Find(platform) is not null;
        }

        return platform is StreamingPlatform.Twitch or StreamingPlatform.YouTube;
    }

    public BotStateDto GetState()
    {
        var target = targetChannelProvider.Current;
        var stream = streamStatus.CurrentStream;
        var obs = obsIntegration.CurrentStatus;
        var obsEnabled = obsIntegrationStore?.Load().Enabled;
        var httpSnapshot = httpServer.CaptureSnapshot();
        var configuredPort = settingsManager.Current.Twitch.HttpServerPort;
        var isRunning = httpSnapshot.IsRunning;
        var actualPort = httpSnapshot.Port ?? configuredPort;
        var portMismatch = isRunning && httpSnapshot.Port is { } runningPort && runningPort != configuredPort;
        var chatUrl = isRunning ? $"http://localhost:{actualPort}/chat" : null;
        var endpoints = isRunning
            ? new BotStateDto.HttpServerEndpointsDto(
                $"http://localhost:{actualPort}/chat",
                $"http://localhost:{actualPort}/events",
                $"http://localhost:{actualPort}/api/history",
                $"http://localhost:{actualPort}/api/chat-settings")
            : null;
        var platforms = BuildPlatformStates();
        var broadcast = broadcastState is null
            ? null
            : new BotStateDto.BroadcastStateDto(
                broadcastState.IsAnyLive,
                broadcastState.CurrentBroadcastId,
                [.. broadcastState.LivePlatforms.Order()]);

        return new BotStateDto(
            AppInfo.Version,
            connectionManager.CurrentPhase,
            connectionManager.IsBusy,
            new BotStateDto.TargetChannelDto(
                target.Login,
                target.OwnChannel,
                target.IsDebugSession,
                target.IsForeign,
                target.IsSendingAllowed,
                target.IsProfileIsolated,
                target.RecordsUserData),
            ToStreamDto(streamStatus.CurrentStatus, stream),
            new BotStateDto.HttpServerStateDto(
                isRunning,
                actualPort,
                chatUrl,
                configuredPort,
                portMismatch,
                endpoints),
            new BotStateDto.ObsStateDto(
                obs.IsConnected,
                obs.ObsVersion,
                obs.ErrorMessage,
                obs.ObsWebSocketVersion,
                obsEnabled),
            platforms,
            broadcast);
    }

    public BotLifecyclePhase Connect(StreamingPlatform? platform = null)
    {
        if (platform is { } target)
        {
            return ConnectPlatform(target);
        }

        var phase = connectionManager.CurrentPhase;

        if (platformRegistry is not null)
        {
            var enabledPlatforms = platformRegistry.All.Where(static p => p.IsEnabled).ToArray();
            if (enabledPlatforms.Length == 0)
            {
                throw new InvalidOperationException("Нет включённых площадок для подключения.");
            }

            var hasConnectable = enabledPlatforms.Any(p => IsConnectablePhase(connectionManager.GetPhase(p.Id)));
            if (!hasConnectable)
            {
                if (enabledPlatforms.Any(p => connectionManager.GetPhase(p.Id) == BotLifecyclePhase.Disconnecting))
                {
                    throw new InvalidOperationException("Бот сейчас отключается – дождитесь фазы Disconnected и повторите.");
                }

                throw new InvalidOperationException($"Бот уже в фазе {phase} – подключать нечего.");
            }
        }
        else
        {
            if (phase is BotLifecyclePhase.Connecting or BotLifecyclePhase.Connected)
            {
                throw new InvalidOperationException($"Бот уже в фазе {phase} – подключать нечего.");
            }

            if (phase == BotLifecyclePhase.Disconnecting)
            {
                throw new InvalidOperationException("Бот сейчас отключается – дождитесь фазы Disconnected и повторите.");
            }
        }

        logger.McpBotConnectRequested();
        connectNotificationGate?.SuppressNextErrorDialog();

        try
        {
            connectionManager.StartConnection();
        }
        catch (InvalidOperationException exception)
        {
            connectNotificationGate?.ResetErrorDialogSuppression();
            throw TranslateStartConnectionException(exception);
        }
        catch
        {
            connectNotificationGate?.ResetErrorDialogSuppression();
            throw;
        }

        return connectionManager.CurrentPhase;
    }

    public async Task<BotLifecyclePhase> DisconnectAsync(StreamingPlatform? platform = null, CancellationToken cancellationToken = default)
    {
        ThrowIfDisconnectCancelled(cancellationToken);

        if (platform is { } target)
        {
            return await DisconnectPlatformAsync(target, cancellationToken);
        }

        var phase = connectionManager.CurrentPhase;
        var hasConnecting = phase == BotLifecyclePhase.Connecting || HasAnyPlatformInPhase(BotLifecyclePhase.Connecting);
        var hasConnected = phase == BotLifecyclePhase.Connected || HasAnyPlatformInPhase(BotLifecyclePhase.Connected);

        if (hasConnecting)
        {
            ThrowIfDisconnectCancelled(cancellationToken);
            logger.McpBotDisconnectRequested();
            connectionManager.CancelConnection();
            await AwaitCancellationAwareAsync(
                connectionManager.WaitForConnectionAsync(),
                "Ожидание отмены подключения прервано – проверьте текущую фазу через get_bot_state.",
                cancellationToken);

            phase = connectionManager.CurrentPhase;
            hasConnected = phase == BotLifecyclePhase.Connected || HasAnyPlatformInPhase(BotLifecyclePhase.Connected);

            if (!hasConnected)
            {
                return phase;
            }
        }
        else if (hasConnected)
        {
            ThrowIfDisconnectCancelled(cancellationToken);
            logger.McpBotDisconnectRequested();
        }
        else
        {
            throw new InvalidOperationException($"Бот в фазе {phase} – отключать нечего.");
        }

        ThrowIfDisconnectCancelled(cancellationToken);
        await AwaitCancellationAwareAsync(
            connectionManager.StopAsync(BotStopMode.Graceful),
            "Ожидание отключения бота прервано – проверьте текущую фазу через get_bot_state.",
            cancellationToken);

        return connectionManager.CurrentPhase;
    }

    public DiagnosticsSnapshot GetDiagnostics()
    {
        if (diagnosticsSource is null)
        {
            throw new InvalidOperationException("Источник диагностики недоступен.");
        }

        return diagnosticsSource.Capture();
    }

    public ChannelsStateDto GetChannelsState()
    {
        if (channelCatalog is null)
        {
            throw new InvalidOperationException("Каталог каналов недоступен.");
        }

        var settings = channelCatalog.Load();
        var activeByPlatform = new Dictionary<StreamingPlatform, string?>();
        foreach (var platformRow in settings.Platforms)
        {
            activeByPlatform[platformRow.Platform] = platformRow.ActiveChannelId;
        }

        var snapshotsByKey = new Dictionary<ChannelKey, ChannelMonitoringSnapshot>();
        if (channelMonitors is not null)
        {
            foreach (var monitor in channelMonitors)
            {
                foreach (var snapshot in monitor.GetSnapshots())
                {
                    snapshotsByKey[snapshot.Channel] = snapshot;
                }
            }
        }

        var platforms = settings.Platforms
            .Select(static p => new ChannelsStateDto.PlatformRoutingDto(
                p.Platform,
                p.ActiveChannelId,
                p.SwitchMode,
                p.PreselectedNextChannelId))
            .ToArray();

        var channels = settings.Channels
            .Select(entry =>
            {
                activeByPlatform.TryGetValue(entry.Platform, out var activeId);
                var isActive = !string.IsNullOrWhiteSpace(activeId)
                    && string.Equals(entry.ChannelId, activeId, StringComparison.Ordinal);
                snapshotsByKey.TryGetValue(entry.Key, out var monitoring);

                return new ChannelsStateDto.ChannelEntryDto(
                    entry.Platform,
                    entry.ChannelId,
                    entry.DisplayName,
                    entry.Handle,
                    entry.ChannelUrl,
                    entry.IsConfigured,
                    entry.MonitoringEnabled,
                    isActive,
                    ToRoleDto(entry, PlatformAuthRole.Bot),
                    ToRoleDto(entry, PlatformAuthRole.Broadcaster),
                    monitoring is null
                        ? null
                        : new ChannelsStateDto.ChannelMonitoringDto(
                            monitoring.Availability,
                            monitoring.Reason,
                            monitoring.Detail,
                            monitoring.Status,
                            monitoring.Stream?.Title,
                            monitoring.Stream?.GameName,
                            monitoring.Stream?.ViewerCount,
                            monitoring.ObservedAtUtc));
            })
            .ToArray();

        var groups = settings.Groups
            .Select(static g => new ChannelsStateDto.ChannelGroupDto(
                g.Id,
                g.Name,
                [.. g.Channels.Select(static m => new ChannelsStateDto.ChannelGroupMemberDto(m.Platform, m.ChannelId))]))
            .ToArray();

        var prompts = channelSwitchCoordinator?.GetActivePrompts()
            .Select(static prompt => new ChannelsStateDto.AutoDiscoverPromptDto(
                prompt.Platform,
                prompt.CurrentActiveChannel?.ChannelId,
                prompt.CurrentActiveDisplayName,
                [.. prompt.Candidates.Select(static c => new ChannelsStateDto.AutoDiscoverCandidateDto(
                    c.Channel.Platform,
                    c.Channel.ChannelId,
                    c.ChannelDisplayName,
                    c.StreamKey,
                    c.StreamTitle,
                    c.StartedAtUtc,
                    c.DetectedAtUtc))]))
            .ToArray();

        return new(platforms, channels, groups, prompts);
    }

    public RuntimeHealthDto GetRuntimeHealth()
    {
        var storageMode = AppPaths.IsBaseDirectoryOverridden
            ? "override"
            : AppPaths.IsPortable ? "portable" : "AppData";

        RuntimeHealthDto.BindingErrorsDto? bindingErrorsDto = null;
        if (bindingErrors is not null)
        {
            var records = bindingErrors.Records;
            var recent = records
                .TakeLast(MaxBindingErrorLines)
                .Select(static r => new RuntimeHealthDto.BindingErrorItemDto(
                    r.Case,
                    r.Theme,
                    Truncate(r.Message, MaxBindingMessageLength)))
                .ToArray();
            bindingErrorsDto = new(bindingErrors.Count, records.Count, recent);
        }

        RuntimeHealthDto.UserStatisticsHealthDto? userStatsHealth = null;
        if (userStatisticsLoader is not null)
        {
            var state = userStatisticsLoader.LoadState;
            if (state == StatisticsLoadState.Loaded && userStatisticsRepository is not null)
            {
                var (accounts, channelCounters, unattributedCounters, linkGroups) = userStatisticsRepository.GetCatalogCounts();
                userStatsHealth = new(state, true, accounts, channelCounters, unattributedCounters, linkGroups);
            }
            else
            {
                userStatsHealth = new(state, userStatisticsLoader.IsLoaded, null, null, null, null);
            }
        }

        RuntimeHealthDto.UpdateHealthDto? updateHealth = updateCoordinator is null
            ? null
            : new(
                updateEnvironment?.Kind ?? KeepShell.Updates.UpdateKind.None,
                updateCoordinator.IsUpdatable,
                updateEnvironment?.CurrentVersion.ToString() ?? AppInfo.Version,
                updateCoordinator.Candidate?.Version.ToString(),
                updateCoordinator.Pending is not null,
                updateCoordinator.Pending?.Version);

        RuntimeHealthDto.DashboardLayoutHealthDto? dashboardHealth = null;
        if (dashboardLayoutCoordinator is not null)
        {
            var snapshot = dashboardLayoutCoordinator.Read();
            var tiles = snapshot.Layout?.Tiles ?? [];
            var visible = tiles.Count(static t => t.IsVisible);
            var collapsed = tiles.Count(static t => t.IsCollapsed);
            dashboardHealth = new(snapshot.Revision, tiles.Count, visible, tiles.Count - visible, collapsed);
        }

        return new(
            new(AppInfo.Version, AppBuildBadge.StartupLine, AppBuildBadge.Current.Value),
            new(storageMode, AppPaths.BaseDirectory, updateEnvironment?.Kind),
            previousRun,
            unobservedExceptions?.Capture(),
            bindingErrorsDto,
            chatDisplayStore?.CaptureStatus(),
            userStatsHealth,
            broadcastHistoryStore?.CaptureDiagnosticsSummary(maxRecent: 0),
            activeBroadcastStore?.CaptureDiagnosticsSummary(),
            pollHistoryStore?.CaptureDiagnosticsSummary(),
            updateHealth,
            dashboardHealth,
            TryReadWebView2Version(),
            DisplayMonitorProbe.Capture());
    }

    public SettingsSummaryDto GetSettingsSummary()
    {
        var entries = new List<SettingsSummaryDto.EntryDto>();
        if (settingsSnapshotContributors is not null)
        {
            foreach (var contributor in settingsSnapshotContributors)
            {
                foreach (var item in contributor.CollectSnapshots())
                {
                    entries.Add(new(item.Key, item.Description));
                }
            }
        }

        return new(entries);
    }

    public AccountsReadinessDto GetAccountsReadiness(bool verifyPersisted = false)
    {
        var nowUtc = _timeProvider.GetUtcNow();

        AccountsReadinessDto.OnboardingStatusDto? onboarding = null;
        if (onboardingChecklist is not null)
        {
            var missing = onboardingChecklist.GetMissingItems();
            onboarding = new(missing.Count == 0, onboardingChecklist.RequiresWizard, missing);
        }

        var platforms = new List<AccountsReadinessDto.PlatformAccountsDto>();
        if (accountDirectories is not null)
        {
            foreach (var directory in accountDirectories)
            {
                var accounts = new List<AccountsReadinessDto.PlatformAccountReadinessDto>();

                foreach (var account in directory.ListAccounts())
                {
                    var roles = new List<AccountsReadinessDto.RoleContextReadinessDto>(account.Authorizations.Count);

                    foreach (var auth in account.Authorizations)
                    {
                        var roleSettings = directory.LoadAuthorization(auth.Role, auth.ContextId);
                        var hasAccessToken = !string.IsNullOrWhiteSpace(roleSettings?.AccessToken);
                        var hasRefreshToken = !string.IsNullOrWhiteSpace(roleSettings?.RefreshToken);
                        var expiresAt = roleSettings?.AccessTokenExpiresAt;
                        var isExpired = expiresAt is { } exp && exp <= nowUtc;

                        var grantedScopes = roleSettings?.StoredScopes is { Length: > 0 } stored
                            ? stored.Where(static s => !string.IsNullOrWhiteSpace(s))
                                .Distinct(StringComparer.OrdinalIgnoreCase)
                                .Order(StringComparer.OrdinalIgnoreCase)
                                .ToArray()
                            : [];

                        var requiredScopes = directory.GetPermissions(auth.Role)
                            .SelectMany(static op => op.RequiredScopes)
                            .Where(static s => !string.IsNullOrWhiteSpace(s))
                            .Distinct(StringComparer.OrdinalIgnoreCase)
                            .Order(StringComparer.OrdinalIgnoreCase)
                            .ToArray();

                        var grantedSet = new HashSet<string>(grantedScopes, StringComparer.OrdinalIgnoreCase);
                        var missingScopes = requiredScopes
                            .Where(scope => !(directory.Platform == StreamingPlatform.YouTube
                                ? YouTubeScopes.IsScopeSatisfied(grantedSet, scope)
                                : grantedSet.Contains(scope)))
                            .ToArray();

                        bool? persistedVerified = verifyPersisted
                            ? directory.VerifyPersistedAuthorization(auth.Role, auth.ContextId, account.Key.AccountId)
                            : null;

                        roles.Add(new(
                            auth.Role,
                            auth.ContextId,
                            auth.Label,
                            auth.DisplayLabel,
                            auth.SubjectId,
                            auth.IsDefault,
                            hasAccessToken,
                            hasRefreshToken,
                            expiresAt,
                            isExpired,
                            grantedScopes,
                            requiredScopes,
                            missingScopes,
                            auth.ChatBrowserProfile is not null,
                            persistedVerified));
                    }

                    accounts.Add(new(
                        directory.Platform,
                        account.Key.AccountId,
                        account.DisplayName,
                        roles));
                }

                platforms.Add(new(directory.Platform, accounts));
            }
        }

        return new(onboarding, platforms);
    }

    public ChatHistoryResponseDto GetChatHistory(
        StreamingPlatform? platform = null,
        string? channel = null,
        bool activeOnly = true,
        int limit = 50)
    {
        if (chatHistoryManager is null)
        {
            throw new InvalidOperationException("Менеджер истории чата недоступен.");
        }

        var clampedLimit = Math.Clamp(limit, 1, MaxChatHistoryLimit);
        var trimmedChannel = string.IsNullOrWhiteSpace(channel) ? null : channel.Trim();

        var activeHistory = chatHistoryManager.GetActiveHistory();
        var activeScopes = activeHistory
            .Select(static m => new ChatHistoryResponseDto.ScopeDto(
                m.Platform,
                string.IsNullOrWhiteSpace(m.ChannelId) ? m.Channel : m.ChannelId,
                m.Channel,
                m.ChatSessionId))
            .Distinct()
            .ToArray();

        IEnumerable<ChatMessageData> source = activeOnly
            ? activeHistory
            : chatHistoryManager.GetHistory();

        if (platform is { } filterPlatform)
        {
            source = source.Where(m => m.Platform == filterPlatform);
        }

        if (trimmedChannel is not null)
        {
            source = source.Where(m =>
                string.Equals(m.Channel, trimmedChannel, StringComparison.OrdinalIgnoreCase)
                || string.Equals(m.ChannelId, trimmedChannel, StringComparison.OrdinalIgnoreCase));
        }

        var matched = source.ToList();
        var recent = matched
            .TakeLast(clampedLimit)
            .Select(static m => new ChatHistoryResponseDto.MessageDto(
                m.Timestamp,
                m.Platform,
                m.Channel,
                m.ChannelId,
                m.UserId,
                m.DisplayName,
                m.DisplayName,
                m.Message,
                m.MessageType,
                m.IsBot,
                m.CommandResponse,
                m.MessageId))
            .ToArray();

        return new(activeOnly, platform, trimmedChannel, clampedLimit, matched.Count, activeScopes, recent);
    }

    public CommandsStateDto GetCommandsState(StreamingPlatform? platform = null)
    {
        if (commandProcessor is null || commandSettingsStore is null)
        {
            throw new InvalidOperationException("Подсистема команд чата недоступна.");
        }

        var settings = commandSettingsStore.Load();
        var usageByCanonical = new Dictionary<string, CommandUsageRecord>(StringComparer.OrdinalIgnoreCase);

        if (commandUsageRepository is not null)
        {
            foreach (var record in commandUsageRepository.GetSnapshot())
            {
                usageByCanonical[record.Canonical] = record;
            }
        }

        HashSet<string>? availableOnPlatform = null;
        if (platform is { } targetPlatform)
        {
            availableOnPlatform = new(
                commandProcessor.GetAvailableCommands(targetPlatform).Select(static c => c.Canonical),
                StringComparer.OrdinalIgnoreCase);
        }

        var entries = commandProcessor.GetAllCommands()
            .OrderBy(static c => c.Canonical, StringComparer.OrdinalIgnoreCase)
            .Select(command =>
            {
                usageByCanonical.TryGetValue(command.Canonical, out var usage);
                var enabled = platform is { } filterPlatform
                    ? settings.IsEnabled(command.Canonical, filterPlatform)
                    : settings.IsEnabled(command.Canonical);

                return new CommandsStateDto.CommandEntryDto(
                    command.Canonical,
                    [.. command.Aliases],
                    command.RequiredCapabilities,
                    enabled,
                    settings.ResolveAccessLevel(command.Canonical),
                    settings.ResolveResponseTarget(command.Canonical),
                    availableOnPlatform?.Contains(command.Canonical),
                    usage?.TotalCount ?? 0,
                    usage?.StreamCount ?? 0,
                    usage?.LastUsedAt,
                    string.IsNullOrWhiteSpace(usage?.LastUsedBy) ? null : usage.LastUsedBy);
            })
            .ToArray();

        return new(
            commandProcessor.Prefix,
            settings.DefaultResponseTarget,
            platform,
            commandResponseTracker?.PendingCount ?? 0,
            entries);
    }

    public BroadcastsStateDto GetBroadcastsState(int recentLimit = 10)
    {
        var clampedLimit = Math.Clamp(recentLimit, 1, MaxRecentItemsLimit);

        var profiles = broadcastProfilesManager is null
            ? Array.Empty<BroadcastsStateDto.ProfileSummaryDto>()
            : broadcastProfilesManager.GetAll()
                .Select(p => new BroadcastsStateDto.ProfileSummaryDto(
                    p.Id,
                    p.Name,
                    p.Title,
                    p.GameName,
                    p.CurrentNumber,
                    p.ObsSceneName,
                    [.. broadcastProfilesManager.GetPlatforms(p.Id).Order()],
                    p.LastApplyAt,
                    p.LastAutoAdvanceAt))
                .ToArray();

        var scheduler = broadcastScheduler is null
            ? null
            : new BroadcastsStateDto.SchedulerStatusDto(
                broadcastScheduler.IsActive,
                broadcastScheduler.Interval,
                broadcastScheduler.NextBroadcastTime,
                broadcastScheduler.LastBroadcastAt,
                broadcastScheduler.SentMessagesCount,
                broadcastScheduler.LastError);

        return new(
            activeBroadcastStore?.CaptureDiagnosticsSummary(),
            broadcastHistoryStore?.CaptureDiagnosticsSummary(clampedLimit),
            profiles,
            scheduler);
    }

    public async Task<PollsStateDto> GetPollsStateAsync(int recentLimit = 10, CancellationToken cancellationToken = default)
    {
        var clampedLimit = Math.Clamp(recentLimit, 1, MaxRecentItemsLimit);

        PollsAvailability? availability = null;
        string? availabilityError = null;
        if (pollsAvailability is not null)
        {
            try
            {
                using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                timeoutCts.CancelAfter(TimeSpan.FromSeconds(5));
                availability = await pollsAvailability.GetAsync(timeoutCts.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception)
            {
                availabilityError = exception.Message;
            }
        }

        var profilesCount = pollsStore?.Load().Profiles.Count ?? 0;
        var health = pollHistoryStore?.CaptureDiagnosticsSummary();
        var recentHistory = pollHistoryStore is null
            ? Array.Empty<PollsStateDto.PollHistoryItemDto>()
            : pollHistoryStore.GetAll()
                .OrderByDescending(static e => e.EndedAtUtc)
                .Take(clampedLimit)
                .Select(static e => new PollsStateDto.PollHistoryItemDto(
                    e.PollId,
                    e.SourceProfileId,
                    e.Title,
                    e.StartedAtUtc,
                    e.EndedAtUtc,
                    e.FinalStatus,
                    e.WinnerChoiceId,
                    e.WinnerIsTie,
                    e.FinalChoices.Sum(static c => c.Votes),
                    [.. e.FinalChoices.Select(static c => new PollsStateDto.PollHistoryChoiceDto(
                        c.ChoiceId,
                        c.Title,
                        c.Votes,
                        c.ChannelPointsVotes,
                        c.BitsVotes))]))
                .ToArray();

        return new(
            pollSnapshotStore?.Current,
            availability,
            availabilityError,
            profilesCount,
            health,
            recentHistory);
    }

    public StatisticsSummaryDto GetStatisticsSummary(
        StreamingPlatform? platform = null,
        string? channelId = null,
        int topCount = 10,
        string? queryUser = null)
    {
        var trimmedChannelId = string.IsNullOrWhiteSpace(channelId) ? null : channelId.Trim();
        if (trimmedChannelId is not null && platform is null)
        {
            throw new InvalidOperationException("Чтобы указать channelId, задайте также параметр platform (Twitch или YouTube).");
        }

        var clampedTop = Math.Clamp(topCount, 1, MaxRecentItemsLimit);
        var slice = (platform, trimmedChannelId) switch
        {
            ({ } p, { } ch) => StatisticsSlice.ForChannel(new(p, ch)),
            ({ } p, null) => StatisticsSlice.ForPlatform(p),
            _ => StatisticsSlice.All(),
        };

        StatisticsSummaryDto.BotStatisticsDto? botStatsDto = null;
        if (botStatisticsRepository is not null)
        {
            var stats = botStatisticsRepository.GetSnapshot();
            botStatsDto = new(stats.TotalMessagesProcessed, stats.BotStartTime, stats.LastUpdated, stats.TotalUptime);
        }

        StatisticsSummaryDto.CatalogCountsDto? catalogCounts = null;
        var sliceUsersCount = 0;
        IReadOnlyList<StatisticsSummaryDto.ViewerSummaryDto> topViewers = [];
        StatisticsSummaryDto.ViewerSummaryDto? queriedViewer = null;

        if (userStatisticsRepository is not null)
        {
            var (accounts, channelCounters, unattributedCounters, linkGroups) = userStatisticsRepository.GetCatalogCounts();
            catalogCounts = new(accounts, channelCounters, unattributedCounters, linkGroups);
            sliceUsersCount = userStatisticsRepository.QuerySlice(slice).Count;

            if (!string.IsNullOrWhiteSpace(queryUser))
            {
                var found = userStatisticsRepository.FindViewerByName(slice, queryUser, platform);
                if (found is null && platform is { } preferredPlatform)
                {
                    found = userStatisticsRepository.FindViewerById(slice, new(preferredPlatform, queryUser.Trim()));
                }

                if (found is not null)
                {
                    queriedViewer = ToViewerSummaryDto(found);
                }
            }
            else
            {
                topViewers = [.. userStatisticsRepository.GetTop(slice, clampedTop).Select(ToViewerSummaryDto)];
            }
        }

        return new(
            userStatisticsLoader?.LoadState,
            botStatsDto,
            catalogCounts,
            new(slice.Kind, slice.Platform, slice.Channel?.ChannelId),
            sliceUsersCount,
            topViewers,
            queriedViewer);
    }

    public async Task<ObsStateResponseDto> GetObsStateAsync(bool includeLists = false, CancellationToken cancellationToken = default)
    {
        var settings = obsIntegrationStore?.Load();
        var enabled = settings?.Enabled ?? false;
        var host = settings?.Host ?? string.Empty;
        var port = settings?.Port ?? 0;
        var autoConnect = settings?.AutoConnect ?? false;
        var sceneName = settings?.SceneName ?? string.Empty;
        var sourceName = settings?.SourceName ?? string.Empty;
        var chatRefreshSources = settings?.GetChatRefreshSourceNames() ?? [];
        var connection = obsIntegration.CurrentStatus;

        ObsDashboardSnapshot? dashboard = null;
        IReadOnlyList<string>? scenes = null;
        IReadOnlyList<string>? browserSources = null;
        string? queryError = null;

        if (obsIntegrationService is not null && settings is { Enabled: true } && obsIntegrationService.IsConnected)
        {
            try
            {
                dashboard = await obsIntegrationService
                    .GetDashboardSnapshotAsync(settings, connectIfNeeded: false, cancellationToken)
                    .ConfigureAwait(false);
                connection = dashboard.Connection;

                if (includeLists && dashboard.IsConnected)
                {
                    scenes = await obsIntegrationService.ListScenesAsync(settings, cancellationToken).ConfigureAwait(false);
                    browserSources = await obsIntegrationService.ListBrowserSourceNamesAsync(settings, cancellationToken).ConfigureAwait(false);
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception)
            {
                queryError = exception.Message;
            }
        }

        return new(
            enabled,
            host,
            port,
            autoConnect,
            sceneName,
            sourceName,
            chatRefreshSources,
            connection,
            dashboard,
            scenes,
            browserSources,
            queryError);
    }

    public CommandDryRunResult DryRunCommand(
        string text,
        StreamingPlatform platform,
        bool isBroadcaster = false,
        bool isModerator = false)
    {
        if (commandProcessor is null)
        {
            throw new InvalidOperationException("Процессор команд чата недоступен.");
        }

        return commandProcessor.DryRunInspect(text, platform, isBroadcaster, isModerator);
    }

    public async Task<RefreshMonitoringResultDto> RefreshChannelMonitoringAsync(
        StreamingPlatform? platform = null,
        string? channelId = null,
        CancellationToken cancellationToken = default)
    {
        var trimmedChannelId = string.IsNullOrWhiteSpace(channelId) ? null : channelId.Trim();
        if (trimmedChannelId is not null && platform is null)
        {
            throw new InvalidOperationException("Чтобы указать channelId, задайте также параметр platform (Twitch или YouTube).");
        }

        if (channelMonitors is null)
        {
            throw new InvalidOperationException("Мониторы каналов недоступны.");
        }

        var targets = platform is { } targetPlatform
            ? channelMonitors.Where(m => m.Platform == targetPlatform).ToArray()
            : channelMonitors.ToArray();

        if (targets.Length == 0)
        {
            throw new InvalidOperationException(
                platform is { } missing
                    ? $"Монитор для площадки {missing} не зарегистрирован."
                    : "Нет зарегистрированных мониторов каналов.");
        }

        logger.McpMonitoringRefreshRequested(platform, trimmedChannelId);

        var channelKey = platform is { } p && trimmedChannelId is not null
            ? new ChannelKey(p, trimmedChannelId)
            : (ChannelKey?)null;

        var refreshed = new List<StreamingPlatform>(targets.Length);
        foreach (var monitor in targets)
        {
            await monitor.RefreshAsync(channelKey, cancellationToken).ConfigureAwait(false);
            refreshed.Add(monitor.Platform);
        }

        return new(true, refreshed, trimmedChannelId);
    }

    public async Task<SwitchActiveChannelResultDto> SwitchActiveChannelAsync(
        StreamingPlatform platform,
        string channelId,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(channelId))
        {
            throw new InvalidOperationException("Не указан идентификатор канала (channelId).");
        }

        if (channelSwitchCoordinator is null)
        {
            throw new InvalidOperationException("Координатор переключения каналов недоступен.");
        }

        var trimmedChannelId = channelId.Trim();
        logger.McpActiveChannelSwitchRequested(platform, trimmedChannelId);

        var result = await channelSwitchCoordinator
            .SwitchActiveChannelAsync(platform, new(platform, trimmedChannelId), cancellationToken)
            .ConfigureAwait(false);

        return new(
            result.Succeeded,
            result.Outcome,
            result.ActiveChannel?.Platform,
            result.ActiveChannel?.ChannelId,
            result.ErrorMessage);
    }

    public async Task<RefreshObsSourcesResultDto> RefreshObsChatSourcesAsync(
        bool connectIfNeeded = false,
        CancellationToken cancellationToken = default)
    {
        if (obsIntegrationService is null || obsIntegrationStore is null)
        {
            throw new InvalidOperationException("Сервис OBS-интеграции недоступен.");
        }

        var settings = obsIntegrationStore.Load();
        if (!settings.Enabled)
        {
            throw new InvalidOperationException("OBS-интеграция отключена в настройках.");
        }

        if (!connectIfNeeded && !obsIntegrationService.IsConnected)
        {
            throw new InvalidOperationException("OBS не подключён – передайте connectIfNeeded=true или подключите OBS.");
        }

        logger.McpObsRefreshRequested(connectIfNeeded);

        var sources = settings.GetChatRefreshSourceNames();
        var refreshed = await obsIntegrationService
            .RefreshConfiguredChatSourcesAsync(settings, cancellationToken)
            .ConfigureAwait(false);

        return new(true, sources, refreshed);
    }

    public async Task<FlushStatisticsResultDto> FlushStatisticsAsync(CancellationToken cancellationToken = default)
    {
        if (statisticsAutoSaver is null)
        {
            throw new InvalidOperationException("Сервис автосохранения статистики недоступен.");
        }

        logger.McpStatisticsFlushRequested();

        await statisticsAutoSaver.SaveNowAsync(cancellationToken).ConfigureAwait(false);

        return new(true, statisticsAutoSaver.LastRunAt, statisticsAutoSaver.LastError);
    }

    public async Task<ExportDiagnosticsBundleResultDto> ExportDiagnosticsBundleAsync(CancellationToken cancellationToken = default)
    {
        if (diagnosticsCollector is null)
        {
            throw new InvalidOperationException("Сборщик пакетов диагностики недоступен.");
        }

        cancellationToken.ThrowIfCancellationRequested();

        var result = await Task.Run(() =>
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (_diagnosticsSecretSources is not null)
            {
                var precheck = DiagnosticsSecrets.Create(_diagnosticsSecretSources);
                if (!precheck.RulesComplete)
                {
                    throw new InvalidOperationException(
                        "Пакет диагностики не сохранён: не удалось собрать полный набор правил вымарывания секретов.");
                }
            }

            var bundle = diagnosticsCollector.Build(rawPaths: false);
            var description = bundle.Describe();
            var redactionComplete = !description.Contains(IncompleteRedactionMarker, StringComparison.Ordinal);

            if (!redactionComplete)
            {
                throw new InvalidOperationException(
                    "Пакет диагностики не сохранён: не удалось гарантировать полное вымарывание секретов.");
            }

            cancellationToken.ThrowIfCancellationRequested();
            var path = diagnosticsCollector.Save(bundle);

            return new ExportDiagnosticsBundleResultDto(path, bundle.Entries.Count, true);
        }, cancellationToken).ConfigureAwait(false);

        logger.McpDiagnosticsBundleExported(result.Path, result.RedactionComplete);

        return result;
    }

    private BotLifecyclePhase ConnectPlatform(StreamingPlatform platform)
    {
        if (platformRegistry is not null)
        {
            var adapter = platformRegistry.Find(platform);
            if (adapter is null)
            {
                throw new InvalidOperationException($"Площадка {platform} не зарегистрирована.");
            }

            if (!adapter.IsEnabled)
            {
                var reason = targetChannelProvider.Current.IsDebugSession && platform == StreamingPlatform.YouTube
                    ? "Площадка YouTube отключена в настройках или заблокирована в режиме отладки на чужом канале."
                    : $"Площадка {platform} отключена в настройках или недоступна в текущем режиме.";
                throw new InvalidOperationException(reason);
            }
        }

        var phase = connectionManager.GetPhase(platform);

        if (phase is BotLifecyclePhase.Connecting or BotLifecyclePhase.Connected)
        {
            throw new InvalidOperationException($"Площадка {platform} уже в фазе {phase} – подключать нечего.");
        }

        if (phase == BotLifecyclePhase.Disconnecting)
        {
            throw new InvalidOperationException($"Площадка {platform} сейчас отключается – дождитесь фазы Disconnected и повторите.");
        }

        logger.McpPlatformConnectRequested(platform);
        connectNotificationGate?.SuppressNextErrorDialog();

        try
        {
            connectionManager.StartConnection(platform);
        }
        catch (InvalidOperationException exception)
        {
            connectNotificationGate?.ResetErrorDialogSuppression();
            throw TranslateStartConnectionException(exception);
        }
        catch
        {
            connectNotificationGate?.ResetErrorDialogSuppression();
            throw;
        }

        return connectionManager.GetPhase(platform);
    }

    private async Task<BotLifecyclePhase> DisconnectPlatformAsync(StreamingPlatform platform, CancellationToken cancellationToken)
    {
        ThrowIfDisconnectCancelled(cancellationToken, platform);

        if (platformRegistry is not null && platformRegistry.Find(platform) is null)
        {
            throw new InvalidOperationException($"Площадка {platform} не зарегистрирована.");
        }

        var phase = connectionManager.GetPhase(platform);

        if (phase == BotLifecyclePhase.Connecting)
        {
            ThrowIfDisconnectCancelled(cancellationToken, platform);
            logger.McpPlatformDisconnectRequested(platform);
            connectionManager.CancelConnection(platform);
            await AwaitCancellationAwareAsync(
                connectionManager.WaitForConnectionAsync(platform),
                $"Ожидание отмены подключения площадки {platform} прервано – проверьте текущую фазу через get_bot_state.",
                cancellationToken);

            phase = connectionManager.GetPhase(platform);

            if (phase != BotLifecyclePhase.Connected)
            {
                return phase;
            }
        }
        else if (phase == BotLifecyclePhase.Connected)
        {
            ThrowIfDisconnectCancelled(cancellationToken, platform);
            logger.McpPlatformDisconnectRequested(platform);
        }
        else
        {
            throw new InvalidOperationException($"Площадка {platform} в фазе {phase} – отключать нечего.");
        }

        ThrowIfDisconnectCancelled(cancellationToken, platform);
        await AwaitCancellationAwareAsync(
            connectionManager.StopAsync(platform, BotStopMode.Graceful),
            $"Ожидание отключения площадки {platform} прервано – проверьте текущую фазу через get_bot_state.",
            cancellationToken);

        return connectionManager.GetPhase(platform);
    }

    private static void ThrowIfDisconnectCancelled(CancellationToken cancellationToken, StreamingPlatform? platform = null)
    {
        if (!cancellationToken.IsCancellationRequested)
        {
            return;
        }

        var message = platform is { } target
            ? $"Запрос отключения площадки {target} отменён до выполнения – проверьте текущую фазу через get_bot_state."
            : "Запрос отключения бота отменён до выполнения – проверьте текущую фазу через get_bot_state.";

        throw new InvalidOperationException(message);
    }

    private bool HasAnyPlatformInPhase(BotLifecyclePhase phase)
    {
        return platformRegistry is not null
            && platformRegistry.All.Any(p => connectionManager.GetPhase(p.Id) == phase);
    }

    private static bool IsConnectablePhase(BotLifecyclePhase phase)
    {
        return phase is not (BotLifecyclePhase.Connecting or BotLifecyclePhase.Connected or BotLifecyclePhase.Disconnecting);
    }

    private static InvalidOperationException TranslateStartConnectionException(InvalidOperationException exception)
    {
        return string.Equals(exception.Message, ConnectionAlreadyInProgressError, StringComparison.Ordinal)
            ? new InvalidOperationException("Подключение уже запущено кем-то другим – проверьте фазу через get_bot_state.", exception)
            : exception;
    }

    private static async Task AwaitCancellationAwareAsync(Task operation, string canceledMessage, CancellationToken cancellationToken)
    {
        try
        {
            await operation.WaitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException exception) when (cancellationToken.IsCancellationRequested)
        {
            throw new InvalidOperationException(canceledMessage, exception);
        }
    }

    private IReadOnlyList<BotStateDto.PlatformStateDto>? BuildPlatformStates()
    {
        if (platformRegistry is null)
        {
            return null;
        }

        var list = new List<BotStateDto.PlatformStateDto>(platformRegistry.All.Count);
        foreach (var platform in platformRegistry.All)
        {
            var status = platform.StreamStatus;
            var currentStream = status.CurrentStream;
            var phase = connectionManager.GetPhase(platform.Id);
            var activeChannelId = channelCatalog?.GetActiveChannel(platform.Id)?.ChannelId;
            list.Add(new BotStateDto.PlatformStateDto(
                platform.Id,
                platform.DisplayName,
                platform.IsEnabled,
                platform.IsConnected,
                phase,
                connectionManager.IsPlatformBusy(platform.Id),
                platform.ConfiguredChannel,
                ToStreamDto(status.CurrentStatus, currentStream),
                platform.BuildStreamUrl(currentStream),
                platform.CaptureChatStatus(phase),
                activeChannelId));
        }

        return list;
    }

    private static StatisticsSummaryDto.ViewerSummaryDto ToViewerSummaryDto(ViewerAggregateStatistics viewer)
    {
        return new(
            viewer.DisplayName,
            viewer.PrimaryKey.Platform,
            viewer.PrimaryKey.UserId,
            viewer.MessageCount,
            viewer.BonusPoints,
            viewer.PenaltyPoints,
            viewer.Points,
            viewer.FirstSeen,
            viewer.LastSeen,
            viewer.IsLinked,
            viewer.LinkGroupId,
            [.. viewer.Accounts.Select(static a => new StatisticsSummaryDto.LinkedAccountDto(
                a.Key.Platform,
                a.Key.UserId,
                a.Name,
                a.FirstSeen,
                a.LastSeen))],
            [.. viewer.ChannelBreakdowns.Select(static b => new StatisticsSummaryDto.ChannelBreakdownDto(
                b.Target.Platform,
                b.Target.ChannelId,
                b.MessageCount,
                b.BonusPoints,
                b.PenaltyPoints,
                b.Points,
                b.FirstSeen,
                b.LastSeen))]);
    }

    private static ChannelsStateDto.RoleAssignmentDto? ToRoleDto(ChannelCatalogEntry entry, PlatformAuthRole role)
    {
        var assignment = entry.GetAccountAssignment(role);
        if (assignment is null)
        {
            return null;
        }

        var verifiedId = role == PlatformAuthRole.Bot
            ? entry.BotVerifiedSubjectId
            : entry.BroadcasterVerifiedSubjectId;

        return new(
            assignment.Account.AccountId,
            assignment.ContextId,
            verifiedId,
            assignment.IsVerified);
    }

    private static BotStateDto.StreamStateDto ToStreamDto(StreamStatus status, StreamInfo? stream)
    {
        return new BotStateDto.StreamStateDto(
            status,
            stream?.Title,
            stream?.GameName,
            stream?.ViewerCount,
            status == StreamStatus.Online ? stream?.StartedAt : null);
    }

    private static string? TryReadWebView2Version()
    {
        try
        {
            var version = CoreWebView2Environment.GetAvailableBrowserVersionString();
            return string.IsNullOrWhiteSpace(version) ? null : version;
        }
        catch (Exception)
        {
            return null;
        }
    }

    private static string Truncate(string value, int maxLength)
    {
        var singleLine = value.Replace('\r', ' ').Replace('\n', ' ').Trim();
        return singleLine.Length <= maxLength ? singleLine : singleLine[..maxLength] + "…";
    }
}
