using PoproshaykaBot.Core.Settings;
using PoproshaykaBot.Core.Settings.Migrations.LegacyImport;
using PoproshaykaBot.Core.Settings.Onboarding;
using PoproshaykaBot.Core.Twitch.Auth;
using PoproshaykaBot.Core.Platforms;
using PoproshaykaBot.Core.Platforms.Auth;
using PoproshaykaBot.Core.Settings.Stores;
using PoproshaykaBot.Core.YouTube;
using PoproshaykaBot.Core.YouTube.Auth;
using System.Runtime.ExceptionServices;

namespace PoproshaykaBot.Wpf.ViewModels.Onboarding;

public sealed class OnboardingContext(
    AppSettings settings,
    TwitchAccountSettings botAccount,
    TwitchAccountSettings broadcasterAccount,
    YouTubeSettings? youTubeSettings = null,
    string youTubeClientSecret = "",
    YouTubeAccountSettings? youTubeBotAccount = null,
    YouTubeAccountSettings? youTubeBroadcasterAccount = null)
{
    public event EventHandler? CredentialsChanged;

    public event EventHandler? PlatformsChanged;

    public AppSettings Settings { get; } = settings;

    public TwitchAccountSettings BotAccount { get; set; } = botAccount;

    public TwitchAccountSettings BroadcasterAccount { get; set; } = broadcasterAccount;

    public YouTubeSettings YouTubeSettings { get; } = youTubeSettings ?? new();

    public string YouTubeClientSecret { get; set; } = youTubeClientSecret;

    public YouTubeAccountSettings YouTubeBotAccount { get; set; } = youTubeBotAccount ?? new();

    public YouTubeAccountSettings YouTubeBroadcasterAccount { get; set; } = youTubeBroadcasterAccount ?? new();

    public bool AutoDetectChannel { get; set; } = true;

    public bool IncludeLegacyImport { get; set; }

    public LegacyImportResult? ImportResult { get; set; }

    internal OnboardingChecklist? Checklist { get; set; }

    internal bool TwitchSettingsWritten { get; set; }

    internal ChannelCatalogStore? TwitchCatalog { get; init; }

    internal ChannelCatalogEntry? TwitchChannel { get; init; }

    internal ChannelCatalogEntry? YouTubeChannel { get; set; }

    internal ResolvedYouTubeChannelInfo? YouTubeResolvedChannel { get; set; }

    internal sealed record ResolvedYouTubeChannelInfo(string Input, string ChannelId, string DisplayName, string ChannelUrl);

    private readonly HashSet<PlatformAuthRole> _reauthorizedTwitchRoles = [];
    private readonly HashSet<PlatformAuthRole> _pendingTwitchRoles = [];
    private readonly Dictionary<PlatformAuthRole, TwitchAccountSettings?> _expectedTwitchAccounts = [];
    private readonly Dictionary<PlatformAuthRole, TwitchAssignment> _appliedTwitchAssignments = [];
    private readonly List<ProvisionalWrite> _provisionalWrites = [];
    private bool _rollbackIncomplete;
    private YouTubeSettings _savedYouTubeSettings = JsonStoreClone.DeepClone(youTubeSettings ?? new());
    private (string Secret, YouTubeAccountSettings Bot, YouTubeAccountSettings Broadcaster) _savedYouTubeAccounts =
        (youTubeClientSecret, JsonStoreClone.DeepClone(youTubeBotAccount ?? new()), JsonStoreClone.DeepClone(youTubeBroadcasterAccount ?? new()));

    private const string ChannelChanged = "Канал или назначенный аккаунт Twitch изменились. Закройте мастер и откройте его снова.";
    private const string AccountChanged = "Аккаунт Twitch изменился во время настройки. Закройте мастер и откройте его снова.";
    private const string AccountNotWritten = "Не удалось сохранить вход в Twitch. Если вы только что перенесли настройки, перезапустите приложение.";
    private const string AssignmentNotWritten = "Не удалось сохранить назначение аккаунта Twitch. Если вы только что перенесли настройки, перезапустите приложение.";
    private const string TwitchSettingsNotWritten = "Не удалось сохранить настройки приложения. Если вы только что перенесли данные, перезапустите приложение.";
    private const string YouTubeSettingsNotWritten = "Запись настроек YouTube приостановлена. Если вы перенесли данные, перезапустите приложение.";
    private const string YouTubeAccountsNotWritten = "Запись аккаунтов YouTube приостановлена. Если вы перенесли данные, перезапустите приложение.";
    private const string YouTubeSettingsChanged = "Настройки YouTube изменились. Закройте мастер и откройте его снова.";
    private const string YouTubeAccountsChanged = "Аккаунты YouTube изменились. Закройте мастер и откройте его снова.";
    internal const string RollbackIncomplete = "Часть изменений не удалось отменить. Закройте мастер, чтобы повторить восстановление.";

    internal bool HasProvisionalWrites => _provisionalWrites.Count > 0;
    internal bool HasIncompleteRollback => _rollbackIncomplete;
    internal bool CancellationStarted { get; set; }

    internal string TwitchContextId(PlatformAuthRole role) => AuthContextIds.Normalize(
        role == PlatformAuthRole.Bot ? TwitchChannel?.BotContextId : TwitchChannel?.BroadcasterContextId);

    internal string YouTubeContextId(PlatformAuthRole role) => AuthContextIds.Normalize(
        role == PlatformAuthRole.Bot ? YouTubeChannel?.BotContextId : YouTubeChannel?.BroadcasterContextId);

    internal bool HasConfiguredTwitchChannel =>
        TwitchChannel is not null || !string.IsNullOrWhiteSpace(Settings.Twitch.Channel);

    internal bool CanUseTwitchRole(PlatformAuthRole role, TwitchAccountSettings account)
    {
        if (!MatchesBroadcaster(role, account.UserId, account.Login))
        {
            return false;
        }

        if (TwitchChannel is null || (_pendingTwitchRoles.Contains(role) && _reauthorizedTwitchRoles.Contains(role)))
        {
            return true;
        }

        var assignment = ExpectedTwitchAssignment(role);
        return !string.IsNullOrWhiteSpace(account.UserId)
            && string.Equals(assignment.VerifiedSubject, account.UserId, StringComparison.Ordinal)
            && string.Equals(assignment.AccountId ?? assignment.VerifiedSubject, account.UserId, StringComparison.Ordinal);
    }

    internal void CaptureTwitchAccounts(TwitchAccountSettings? bot, TwitchAccountSettings? broadcaster)
    {
        _expectedTwitchAccounts[PlatformAuthRole.Bot] = bot;
        _expectedTwitchAccounts[PlatformAuthRole.Broadcaster] = broadcaster;
    }

    internal TwitchAccountSettings CurrentTwitchAccount(AccountsStore accounts, PlatformAuthRole role) =>
        _pendingTwitchRoles.Contains(role)
            ? DraftTwitchAccount(role)
            : accounts.Load(role, TwitchContextId(role));

    private TwitchAccountSettings DraftTwitchAccount(PlatformAuthRole role) =>
        role == PlatformAuthRole.Bot ? BotAccount : BroadcasterAccount;

    private bool MatchesBroadcaster(PlatformAuthRole role, string userId, string login)
    {
        if (role != PlatformAuthRole.Broadcaster)
        {
            return true;
        }

        if (TwitchChannel is { } channel)
        {
            return string.Equals(channel.ChannelId, userId, StringComparison.Ordinal);
        }

        var handle = Settings.Twitch.Channel?.Trim().TrimStart('@');
        return AutoDetectChannel || string.IsNullOrEmpty(handle) || string.Equals(handle, login, StringComparison.OrdinalIgnoreCase);
    }

    internal string? GetExpectedTwitchAccountLabel(
        PlatformAuthRole role,
        AccountsStore? accounts = null,
        TwitchAccountSettings? currentAccount = null)
    {
        var currentSubject = TwitchChannel?.GetAccountAssignment(role)?.Account.AccountId;
        var contextId = TwitchContextId(role);
        var sharedContext = TwitchCatalog?.GetConfiguredChannels(StreamingPlatform.Twitch)
            .Any(channel => channel.Key != TwitchChannel?.Key
                && AuthContextIds.Normalize(role == PlatformAuthRole.Bot ? channel.BotContextId : channel.BroadcasterContextId) == contextId) == true;
        if (string.IsNullOrWhiteSpace(currentSubject) && (!AuthContextIds.IsDefault(contextId) || sharedContext))
        {
            currentSubject = (currentAccount ?? _expectedTwitchAccounts.GetValueOrDefault(role))?.UserId;
        }

        if (!string.IsNullOrWhiteSpace(currentSubject))
        {
            return ResolveTwitchAccountDisplay(role, currentSubject, accounts, currentAccount);
        }

        if (role == PlatformAuthRole.Broadcaster)
        {
            if (TwitchChannel is { } channel)
            {
                return FormatChannelLabel(channel.Handle, channel.DisplayName, channel.ChannelId);
            }

            var handle = Settings.Twitch.Channel?.Trim().TrimStart('@');
            if (!AutoDetectChannel && !string.IsNullOrWhiteSpace(handle))
            {
                return $"@{handle}";
            }
        }

        return null;
    }

    internal bool IsTwitchAccountFixed(
        PlatformAuthRole role,
        AccountsStore? accounts = null,
        TwitchAccountSettings? currentAccount = null) =>
        GetExpectedTwitchAccountLabel(role, accounts, currentAccount) is not null;

    private string ResolveTwitchAccountDisplay(
        PlatformAuthRole role,
        string userId,
        AccountsStore? accounts,
        TwitchAccountSettings? currentAccount)
    {
        var captured = _expectedTwitchAccounts.GetValueOrDefault(role);
        if (captured is not null
            && string.Equals(captured.UserId, userId, StringComparison.Ordinal)
            && !string.IsNullOrWhiteSpace(captured.Login))
        {
            return $"@{captured.Login.TrimStart('@')}";
        }

        if (currentAccount is not null
            && string.Equals(currentAccount.UserId, userId, StringComparison.Ordinal)
            && !string.IsNullOrWhiteSpace(currentAccount.Login))
        {
            return $"@{currentAccount.Login.TrimStart('@')}";
        }

        var fromStore = accounts?.ListAccounts()
            .FirstOrDefault(a => string.Equals(a.Key.AccountId, userId, StringComparison.Ordinal));
        if (fromStore is not null && !string.IsNullOrWhiteSpace(fromStore.DisplayName))
        {
            return $"@{fromStore.DisplayName.TrimStart('@')}";
        }

        if (role == PlatformAuthRole.Broadcaster
            && TwitchChannel is { } channel
            && string.Equals(channel.ChannelId, userId, StringComparison.Ordinal))
        {
            return FormatChannelLabel(channel.Handle, channel.DisplayName, userId);
        }

        return $"ID {userId}";
    }

    private static string FormatChannelLabel(string? handle, string? displayName, string channelId)
    {
        if (!string.IsNullOrWhiteSpace(handle))
        {
            return $"@{handle.Trim().TrimStart('@')}";
        }

        if (!string.IsNullOrWhiteSpace(displayName))
        {
            return displayName.Trim();
        }

        return $"ID {channelId}";
    }

    internal bool AcceptTwitchLogin(PlatformAuthRole role, TwitchAccountSettings account, OAuthFlowResult login)
    {
        var currentSubject = TwitchChannel?.GetAccountAssignment(role)?.Account.AccountId;
        var contextId = TwitchContextId(role);
        var sharedContext = TwitchCatalog?.GetConfiguredChannels(StreamingPlatform.Twitch)
            .Any(channel => channel.Key != TwitchChannel?.Key
                && AuthContextIds.Normalize(role == PlatformAuthRole.Bot ? channel.BotContextId : channel.BroadcasterContextId) == contextId) == true;
        if (string.IsNullOrWhiteSpace(currentSubject) && (!AuthContextIds.IsDefault(contextId) || sharedContext))
        {
            currentSubject = account.UserId;
        }

        if (string.IsNullOrWhiteSpace(login.UserId) || !MatchesBroadcaster(role, login.UserId, login.Login)
            || (!string.IsNullOrWhiteSpace(currentSubject) && !string.Equals(currentSubject, login.UserId, StringComparison.Ordinal)))
        {
            return false;
        }

        login.ApplyTo(account, DateTimeOffset.UtcNow);
        _reauthorizedTwitchRoles.Add(role);
        _pendingTwitchRoles.Add(role);
        return true;
    }

    internal void ClearTwitchLogin(PlatformAuthRole role)
    {
        if (_reauthorizedTwitchRoles.Remove(role)
            || _expectedTwitchAccounts.GetValueOrDefault(role) is not null)
        {
            _pendingTwitchRoles.Add(role);
        }
    }

    internal string? SaveSetup(SettingsManager settingsManager, AccountsStore accounts,
        YouTubeSettingsStore? youtubeSettingsStore = null, YouTubeAccountsStore? youtubeAccountsStore = null)
    {
        if (CancellationStarted)
        {
            return "Настройка уже отменяется. Закройте мастер, чтобы завершить восстановление.";
        }

        if (_rollbackIncomplete)
        {
            return RollbackIncomplete;
        }

        if (settingsManager.IsWriteRevoked)
        {
            return TwitchSettingsNotWritten;
        }

        if (accounts.IsWriteRevoked)
        {
            return AccountNotWritten;
        }

        if (TwitchCatalog?.IsWriteRevoked == true)
        {
            return AssignmentNotWritten;
        }

        if (TwitchCatalog is not null && !MatchesTwitchChannel(TwitchCatalog.GetActiveChannel(StreamingPlatform.Twitch)))
        {
            return ChannelChanged;
        }

        var undo = new List<ProvisionalWrite>();
        var accountSnapshots = new Dictionary<PlatformAuthRole, TwitchAccountSettings>();
        var assignments = new Dictionary<PlatformAuthRole, TwitchAssignment>();
        var youtubeDraft = (YouTubeClientSecret, YouTubeBotAccount, YouTubeBroadcasterAccount);
        string? error = null;
        Exception? failure = null;
        try
        {
            error = WriteTwitchAccounts(accounts, undo, accountSnapshots, assignments)
                ?? WriteYouTube(youtubeSettingsStore, youtubeAccountsStore, youtubeDraft, undo)
                ?? WriteYouTubeCatalog(undo);
            if (error is null && !settingsManager.SaveSettingsIfWritable(Settings))
            {
                error = TwitchSettingsNotWritten;
            }
        }
        catch (Exception exception)
        {
            failure = exception;
        }

        if (error is not null || failure is not null)
        {
            var rollbackErrors = UndoWrites(undo);
            if (undo.Count > 0)
            {
                _rollbackIncomplete = true;
                _provisionalWrites.AddRange(undo);
                if (failure is not null)
                {
                    rollbackErrors.Insert(0, failure);
                }
                else if (error is not null)
                {
                    rollbackErrors.Insert(0, new InvalidOperationException(error));
                }

                throw new AggregateException(RollbackIncomplete, rollbackErrors);
            }

            if (failure is not null)
            {
                ExceptionDispatchInfo.Throw(failure);
            }

            return error;
        }

        TwitchSettingsWritten = true;
        foreach (var (role, snapshot) in accountSnapshots)
        {
            _expectedTwitchAccounts[role] = snapshot;
        }

        foreach (var (role, binding) in assignments)
        {
            _appliedTwitchAssignments[role] = binding;
        }

        _savedYouTubeSettings = JsonStoreClone.DeepClone(YouTubeSettings);
        _savedYouTubeAccounts = (YouTubeClientSecret, JsonStoreClone.DeepClone(YouTubeBotAccount), JsonStoreClone.DeepClone(YouTubeBroadcasterAccount));
        _pendingTwitchRoles.Clear();
        _provisionalWrites.AddRange(undo);
        return null;
    }

    private string? WriteTwitchAccounts(AccountsStore accounts, List<ProvisionalWrite> undo,
        Dictionary<PlatformAuthRole, TwitchAccountSettings> snapshots, Dictionary<PlatformAuthRole, TwitchAssignment> assignments)
    {
        foreach (var role in _pendingTwitchRoles)
        {
            var contextId = TwitchContextId(role);
            var draft = DraftTwitchAccount(role);
            if (!_expectedTwitchAccounts.TryGetValue(role, out var previous))
            {
                accounts.TryLoadContext(role, contextId, out previous);
            }

            var result = accounts.TryReplaceContext(role, contextId, current => SameAccount(current, previous), draft);
            if (!result.Applied || !result.Written)
            {
                return result.Applied ? AccountNotWritten : AccountChanged;
            }

            var written = result.Snapshot!;
            snapshots.Add(role, written);
            undo.Add(new(new(accounts, role), () =>
            {
                var restore = accounts.TryReplaceContext(role, contextId, current => SameAccount(current, written), previous);
                return !restore.Applied || restore.Written;
            }));

            if (TwitchChannel is not null && _reauthorizedTwitchRoles.Contains(role))
            {
                if (!accounts.VerifyPersistedAuthorization(role, contextId, draft.UserId))
                {
                    return AccountNotWritten;
                }

                assignments.Add(role, new(draft.UserId, contextId, draft.UserId));
            }
        }

        if (TwitchCatalog is not { } catalog || TwitchChannel is not { } channel || assignments.Count == 0)
        {
            return null;
        }

        var before = assignments.Keys.ToDictionary(role => role, ExpectedTwitchAssignment);
        var (applied, saved) = catalog.TryMutateChannel(channel.Key, requireActive: true, entry =>
        {
            if (!MatchesTwitchChannel(entry))
            {
                return false;
            }

            foreach (var (role, binding) in assignments)
            {
                binding.Apply(entry, role);
            }

            return true;
        });
        if (!applied || !saved)
        {
            return applied ? AssignmentNotWritten : ChannelChanged;
        }

        undo.Add(new(new(catalog), () =>
        {
            var restored = catalog.TryMutateChannel(channel.Key, requireActive: false, entry =>
            {
                var changed = false;
                foreach (var (role, binding) in assignments)
                {
                    if (TwitchAssignment.From(entry, role) == binding && before[role] != binding)
                    {
                        before[role].Apply(entry, role);
                        changed = true;
                    }
                }

                return changed;
            });
            return !restored.Applied || restored.Written;
        }));
        return null;
    }

    private bool MatchesTwitchChannel(ChannelCatalogEntry? entry) =>
        entry?.Key == TwitchChannel?.Key && (entry is null || PlatformAuthRoles.Active.All(role =>
            TwitchAssignment.From(entry, role) == ExpectedTwitchAssignment(role)));

    private TwitchAssignment ExpectedTwitchAssignment(PlatformAuthRole role) =>
        _appliedTwitchAssignments.TryGetValue(role, out var applied) ? applied : TwitchAssignment.From(TwitchChannel!, role);

    private string? WriteYouTube(YouTubeSettingsStore? settingsStore, YouTubeAccountsStore? accountsStore,
        (string Secret, YouTubeAccountSettings Bot, YouTubeAccountSettings Broadcaster) accounts, List<ProvisionalWrite> undo)
    {
        if (settingsStore is not null && !JsonStoreClone.SameValues(YouTubeSettings, _savedYouTubeSettings))
        {
            var original = _savedYouTubeSettings;
            var replacement = JsonStoreClone.DeepClone(YouTubeSettings);
            var written = settingsStore.TryReplace(original, replacement);
            if (!written.Written)
            {
                return written.Matched ? YouTubeSettingsNotWritten : YouTubeSettingsChanged;
            }

            undo.Add(new(new(settingsStore), () =>
            {
                var result = settingsStore.TryReplace(replacement, original);
                return !result.Matched || result.Written;
            }));
        }

        if (accountsStore is not null &&
            (!string.Equals(accounts.Secret, _savedYouTubeAccounts.Secret, StringComparison.Ordinal)
             || !JsonStoreClone.SameValues(accounts.Bot, _savedYouTubeAccounts.Bot)
             || !JsonStoreClone.SameValues(accounts.Broadcaster, _savedYouTubeAccounts.Broadcaster)))
        {
            var botContextId = YouTubeContextId(PlatformAuthRole.Bot);
            var broadcasterContextId = YouTubeContextId(PlatformAuthRole.Broadcaster);
            var original = _savedYouTubeAccounts;
            var replacement = (accounts.Secret, JsonStoreClone.DeepClone(accounts.Bot), JsonStoreClone.DeepClone(accounts.Broadcaster));
            var written = accountsStore.TryReplaceAccounts(botContextId, broadcasterContextId, original, replacement);
            if (!written.Written)
            {
                return written.Matched ? YouTubeAccountsNotWritten : YouTubeAccountsChanged;
            }

            undo.Add(new(new(accountsStore), () =>
            {
                var result = accountsStore.TryReplaceAccounts(botContextId, broadcasterContextId, replacement, original);
                return !result.Matched || result.Written;
            }));
        }

        return null;
    }

    internal void RestoreProvisionalWrites()
    {
        var errors = UndoWrites(_provisionalWrites);
        _rollbackIncomplete = errors.Count > 0;
        if (_rollbackIncomplete)
        {
            throw new AggregateException(RollbackIncomplete, errors);
        }
    }

    private static List<Exception> UndoWrites(List<ProvisionalWrite> writes)
    {
        var errors = new List<Exception>();
        var blocked = new HashSet<WriteKey>();
        for (var index = writes.Count - 1; index >= 0; index--)
        {
            var write = writes[index];
            if (blocked.Contains(write.Key))
            {
                continue;
            }

            try
            {
                if (write.Undo())
                {
                    writes.RemoveAt(index);
                }
                else
                {
                    blocked.Add(write.Key);
                    errors.Add(new InvalidOperationException("Хранилище отклонило восстановление настроек."));
                }
            }
            catch (Exception exception)
            {
                blocked.Add(write.Key);
                errors.Add(exception);
            }
        }

        return errors;
    }

    private sealed record ProvisionalWrite(WriteKey Key, Func<bool> Undo);

    private readonly record struct WriteKey(object Store, PlatformAuthRole Role = PlatformAuthRole.None, StreamingPlatform? Platform = null);

    private string? WriteYouTubeCatalog(List<ProvisionalWrite> undo)
    {
        if (TwitchCatalog is not { } catalog || !YouTubeSettings.Enabled)
        {
            return null;
        }

        if (ResolveYouTubeCatalogIdentity() is not { } identity)
        {
            return null;
        }

        var before = catalog.Load();
        var previousEntry = before.Channels.FirstOrDefault(channel =>
            channel.Platform == StreamingPlatform.YouTube
            && string.Equals(channel.ChannelId, identity.ChannelId, StringComparison.Ordinal));
        var previousSnapshot = previousEntry is null ? null : JsonStoreClone.DeepClone(previousEntry);
        var previousRouting = before.Platforms.FirstOrDefault(platform => platform.Platform == StreamingPlatform.YouTube);
        var previousRoutingSnapshot = previousRouting is null ? null : JsonStoreClone.DeepClone(previousRouting);

        var written = catalog.Mutate(settings =>
        {
            var entry = settings.Channels.FirstOrDefault(channel =>
                channel.Platform == StreamingPlatform.YouTube
                && string.Equals(channel.ChannelId, identity.ChannelId, StringComparison.Ordinal));

            if (entry is null)
            {
                entry = new()
                {
                    Platform = StreamingPlatform.YouTube,
                    ChannelId = identity.ChannelId,
                    BotContextId = YouTubeContextId(PlatformAuthRole.Bot),
                    BroadcasterContextId = YouTubeContextId(PlatformAuthRole.Broadcaster),
                };
                settings.Channels.Add(entry);
            }

            entry.IsConfigured = true;
            entry.MonitoringEnabled = true;
            entry.DisplayName = identity.DisplayName;
            if (!string.IsNullOrWhiteSpace(identity.Handle))
            {
                entry.Handle = identity.Handle;
            }

            entry.ChannelUrl = identity.ChannelUrl;

            entry.BotContextId = AuthContextIds.Normalize(entry.BotContextId);
            if (PlatformAuthRoles.HasCredentials(YouTubeBotAccount.AccessToken, YouTubeBotAccount.RefreshToken)
                && !string.IsNullOrWhiteSpace(YouTubeBotAccount.ChannelId))
            {
                var botId = YouTubeBotAccount.ChannelId.Trim();
                entry.BotAccountId = botId;
                entry.BotVerifiedSubjectId = botId;
            }

            entry.BroadcasterContextId = AuthContextIds.Normalize(entry.BroadcasterContextId);
            if (PlatformAuthRoles.HasCredentials(YouTubeBroadcasterAccount.AccessToken, YouTubeBroadcasterAccount.RefreshToken)
                && string.Equals(YouTubeBroadcasterAccount.ChannelId?.Trim(), identity.ChannelId, StringComparison.Ordinal))
            {
                entry.BroadcasterAccountId = identity.ChannelId;
                entry.BroadcasterVerifiedSubjectId = identity.ChannelId;
            }

            var routing = settings.Platforms.FirstOrDefault(platform => platform.Platform == StreamingPlatform.YouTube);
            if (routing is null)
            {
                settings.Platforms.Add(new()
                {
                    Platform = StreamingPlatform.YouTube,
                    ActiveChannelId = identity.ChannelId,
                    SwitchMode = ChannelSwitchMode.Manual,
                });
            }
            else
            {
                routing.ActiveChannelId = identity.ChannelId;
            }
        });

        if (!written)
        {
            return AssignmentNotWritten;
        }

        var updatedEntry = catalog.FindEntry(new ChannelKey(StreamingPlatform.YouTube, identity.ChannelId));
        var updatedSnapshot = updatedEntry is null ? null : JsonStoreClone.DeepClone(updatedEntry);
        if (updatedEntry is not null)
        {
            YouTubeChannel = updatedEntry;
        }

        undo.Add(new(new(catalog, Platform: StreamingPlatform.YouTube), () =>
        {
            return catalog.Mutate(settings =>
            {
                var current = settings.Channels.FirstOrDefault(channel =>
                    channel.Platform == StreamingPlatform.YouTube
                    && string.Equals(channel.ChannelId, identity.ChannelId, StringComparison.Ordinal));

                if (previousSnapshot is null)
                {
                    if (current is not null && updatedSnapshot is not null && JsonStoreClone.SameValues(current, updatedSnapshot))
                    {
                        settings.Channels.Remove(current);
                    }
                }
                else if (current is not null)
                {
                    if (updatedSnapshot is null || JsonStoreClone.SameValues(current, updatedSnapshot))
                    {
                        var index = settings.Channels.IndexOf(current);
                        settings.Channels[index] = JsonStoreClone.DeepClone(previousSnapshot);
                    }
                }
                else
                {
                    settings.Channels.Add(JsonStoreClone.DeepClone(previousSnapshot));
                }

                var routing = settings.Platforms.FirstOrDefault(platform => platform.Platform == StreamingPlatform.YouTube);
                if (previousRoutingSnapshot is null)
                {
                    if (routing is not null && string.Equals(routing.ActiveChannelId, identity.ChannelId, StringComparison.Ordinal))
                    {
                        settings.Platforms.Remove(routing);
                    }
                }
                else if (routing is not null)
                {
                    if (string.Equals(routing.ActiveChannelId, identity.ChannelId, StringComparison.Ordinal))
                    {
                        var index = settings.Platforms.IndexOf(routing);
                        settings.Platforms[index] = JsonStoreClone.DeepClone(previousRoutingSnapshot);
                    }
                }
                else
                {
                    settings.Platforms.Add(JsonStoreClone.DeepClone(previousRoutingSnapshot));
                }
            });
        }));

        return null;
    }

    private (string ChannelId, string DisplayName, string Handle, string ChannelUrl)? ResolveYouTubeCatalogIdentity()
    {
        var rawInput = YouTubeSettings.Channel?.Trim() ?? string.Empty;
        string? channelId = null;
        var handle = string.Empty;
        string? resolvedDisplayName = null;
        string? resolvedUrl = null;

        if (!string.IsNullOrWhiteSpace(rawInput) && TryExtractYouTubeChannelId(rawInput, out var directId))
        {
            channelId = directId;
            handle = rawInput;
        }
        else if (YouTubeResolvedChannel is { } resolved
            && !string.IsNullOrWhiteSpace(resolved.ChannelId)
            && string.Equals(resolved.Input, rawInput, StringComparison.OrdinalIgnoreCase))
        {
            channelId = resolved.ChannelId;
            handle = rawInput;
            resolvedDisplayName = resolved.DisplayName;
            resolvedUrl = resolved.ChannelUrl;
        }
        else if (YouTubeChannel is { } existing
            && !string.IsNullOrWhiteSpace(existing.ChannelId)
            && (string.IsNullOrWhiteSpace(rawInput)
                || string.Equals(rawInput, existing.Handle, StringComparison.OrdinalIgnoreCase)
                || string.Equals(rawInput, existing.ChannelId, StringComparison.Ordinal)
                || (YouTubeChannelInput.NormalizeHandle(rawInput) is { } normInput
                    && string.Equals(normInput, YouTubeChannelInput.NormalizeHandle(existing.Handle), StringComparison.OrdinalIgnoreCase))))
        {
            channelId = existing.ChannelId;
            handle = string.IsNullOrWhiteSpace(rawInput) ? existing.Handle : rawInput;
            resolvedDisplayName = existing.DisplayName;
            resolvedUrl = existing.ChannelUrl;
        }
        else if (string.IsNullOrWhiteSpace(rawInput) && !string.IsNullOrWhiteSpace(YouTubeBroadcasterAccount.ChannelId))
        {
            channelId = YouTubeBroadcasterAccount.ChannelId.Trim();
            handle = YouTubeBroadcasterAccount.ChannelTitle ?? string.Empty;
        }

        if (string.IsNullOrWhiteSpace(channelId))
        {
            return null;
        }

        var displayName = !string.IsNullOrWhiteSpace(YouTubeBroadcasterAccount.ChannelTitle)
            && string.Equals(YouTubeBroadcasterAccount.ChannelId?.Trim(), channelId, StringComparison.Ordinal)
            ? YouTubeBroadcasterAccount.ChannelTitle.Trim()
            : (!string.IsNullOrWhiteSpace(resolvedDisplayName)
                ? resolvedDisplayName
                : (!string.IsNullOrWhiteSpace(handle) ? handle : channelId));
        var channelUrl = !string.IsNullOrWhiteSpace(resolvedUrl)
            ? resolvedUrl
            : $"https://youtube.com/channel/{channelId}";

        return (channelId, displayName, handle, channelUrl);
    }

    internal static bool TryExtractYouTubeChannelId(string? rawInput, out string channelId) =>
        YouTubeChannelInput.TryExtractDirectChannelId(rawInput, out channelId);

    private static bool SameAccount(TwitchAccountSettings? left, TwitchAccountSettings? right) =>
        left is null || right is null ? left is null && right is null
            : string.Equals(left.AccessToken, right.AccessToken, StringComparison.Ordinal)
            && string.Equals(left.RefreshToken, right.RefreshToken, StringComparison.Ordinal)
            && string.Equals(left.Login, right.Login, StringComparison.Ordinal)
            && string.Equals(left.UserId, right.UserId, StringComparison.Ordinal)
            && left.AccessTokenExpiresAt == right.AccessTokenExpiresAt
            && left.Scopes.SequenceEqual(right.Scopes, StringComparer.Ordinal)
            && left.StoredScopes.SequenceEqual(right.StoredScopes, StringComparer.Ordinal)
            && string.Equals(left.BrowserProfileName, right.BrowserProfileName, StringComparison.Ordinal);

    private readonly record struct TwitchAssignment(string? AccountId, string? ContextId, string? VerifiedSubject)
    {
        internal static TwitchAssignment From(ChannelCatalogEntry entry, PlatformAuthRole role) =>
            role == PlatformAuthRole.Bot
                ? new(entry.BotAccountId, AuthContextIds.Normalize(entry.BotContextId), entry.BotVerifiedSubjectId)
                : new(entry.BroadcasterAccountId, AuthContextIds.Normalize(entry.BroadcasterContextId), entry.BroadcasterVerifiedSubjectId);

        internal void Apply(ChannelCatalogEntry entry, PlatformAuthRole role)
        {
            if (role == PlatformAuthRole.Bot)
            {
                (entry.BotAccountId, entry.BotContextId, entry.BotVerifiedSubjectId) = (AccountId, ContextId, VerifiedSubject);
            }
            else
            {
                (entry.BroadcasterAccountId, entry.BroadcasterContextId, entry.BroadcasterVerifiedSubjectId) = (AccountId, ContextId, VerifiedSubject);
            }
        }
    }

    public void RaiseCredentialsChanged()
    {
        CredentialsChanged?.Invoke(this, EventArgs.Empty);
    }

    public void RaisePlatformsChanged()
    {
        PlatformsChanged?.Invoke(this, EventArgs.Empty);
    }
}
