using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using PoproshaykaBot.Core.Chat;
using PoproshaykaBot.Core.Infrastructure.Events;
using PoproshaykaBot.Core.Infrastructure.Events.Broadcasting;
using PoproshaykaBot.Core.Platforms;
using PoproshaykaBot.Core.Settings.Stores;
using PoproshaykaBot.Core.Streaming;
using System.Collections.Frozen;
using System.Globalization;

namespace PoproshaykaBot.Core.Broadcast.Profiles;

public class BroadcastProfilesManager
{
    private readonly BroadcastProfilesStore _profilesStore;
    private readonly IEventBus _eventBus;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<BroadcastProfilesManager> _logger;
    private readonly BroadcastProfilePlatformsStore? _profilePlatformsStore;
    private readonly BroadcastProfileYouTubeStore? _youtubeStore;
    private readonly IChatPlatformRegistry? _platformRegistry;
    private readonly FrozenDictionary<StreamingPlatform, IPlatformBroadcastProfileApplier> _appliers;

    private enum AdvanceOutcome
    {
        NotFound = 0,
        RaceDetected = 1,
        Advanced = 2,
    }

    public BroadcastProfilesManager(
        BroadcastProfilesStore profilesStore,
        IChannelInformationApplier? applier,
        IEventBus eventBus,
        TimeProvider timeProvider,
        ILogger<BroadcastProfilesManager> logger,
        BroadcastProfilePlatformsStore? profilePlatformsStore = null,
        BroadcastProfileYouTubeStore? youtubeStore = null,
        IYouTubeBroadcastProfileApplier? youtubeApplier = null)
        : this(
            profilesStore,
            applier,
            eventBus,
            timeProvider,
            logger,
            profilePlatformsStore,
            youtubeStore,
            youtubeApplier,
            platformAppliers: null,
            platformRegistry: null)
    {
    }

    [ActivatorUtilitiesConstructor]
    public BroadcastProfilesManager(
        BroadcastProfilesStore profilesStore,
        IChannelInformationApplier? applier,
        IEventBus eventBus,
        TimeProvider timeProvider,
        ILogger<BroadcastProfilesManager> logger,
        BroadcastProfilePlatformsStore? profilePlatformsStore,
        BroadcastProfileYouTubeStore? youtubeStore,
        IYouTubeBroadcastProfileApplier? youtubeApplier,
        IEnumerable<IPlatformBroadcastProfileApplier>? platformAppliers,
        IChatPlatformRegistry? platformRegistry)
    {
        _profilesStore = profilesStore;
        _eventBus = eventBus;
        _timeProvider = timeProvider;
        _logger = logger;
        _profilePlatformsStore = profilePlatformsStore;
        _youtubeStore = youtubeStore;
        _platformRegistry = platformRegistry;

        var map = new Dictionary<StreamingPlatform, IPlatformBroadcastProfileApplier>();
        if (platformAppliers is not null)
        {
            foreach (var platformApplier in platformAppliers)
            {
                map[platformApplier.Platform] = platformApplier;
            }
        }

        if (!map.ContainsKey(StreamingPlatform.Twitch) && applier is not null)
        {
            map[StreamingPlatform.Twitch] = new LegacyTwitchPlatformApplier(applier);
        }

        if (!map.ContainsKey(StreamingPlatform.YouTube) && youtubeApplier is not null)
        {
            map[StreamingPlatform.YouTube] = new LegacyYouTubePlatformApplier(youtubeApplier, youtubeStore);
        }

        _appliers = map.ToFrozenDictionary();
    }

    public virtual BroadcastProfile? FindByName(string name)
    {
        return _profilesStore.Load()
            .Profiles
            .FirstOrDefault(p => string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase));
    }

    public virtual async Task<BroadcastProfile?> ApplyByNameAsync(string name, CancellationToken cancellationToken)
    {
        var profile = FindByName(name);
        return profile == null ? null : await ApplyAsync(profile.Id, cancellationToken);
    }

    public virtual async Task<BroadcastProfile?> ApplyAsync(Guid id, CancellationToken cancellationToken)
    {
        var profile = Find(id);

        if (profile == null)
        {
            _logger.LogDebug("ApplyAsync: профиль Id={ProfileId} не найден", id);
            return null;
        }

        var youtubeMetadata = _youtubeStore?.GetMetadata(profile.Id);
        var template = MessageTemplate.For(profile.Title);
        var hasTitlePlaceholder = template.Contains("n");
        var hasYouTubePlaceholder = youtubeMetadata is not null
            && (MessageTemplate.For(youtubeMetadata.TitleOverride ?? string.Empty).Contains("n")
                || MessageTemplate.For(youtubeMetadata.Description ?? string.Empty).Contains("n"));
        var hasPlaceholder = hasTitlePlaceholder || hasYouTubePlaceholder;

        var resolvedTitle = hasTitlePlaceholder
            ? template.With("n", profile.CurrentNumber.ToString(CultureInfo.InvariantCulture)).Render()
            : profile.Title;

        var profileToApply = BroadcastProfileSyncMatcher.WithResolvedTitle(profile, resolvedTitle);

        _ = _eventBus.PublishAsync(new BroadcastProfileApplying(profileToApply), cancellationToken);

        var targets = GetPlatforms(profile.Id).Order().ToArray();
        var results = new List<BroadcastProfilePlatformApplyResult>(targets.Length);

        foreach (var platform in targets)
        {
            if (_appliers.TryGetValue(platform, out var platformApplier))
            {
                results.Add(await platformApplier.ApplyAsync(profileToApply, resolvedTitle, cancellationToken));
            }
            else
            {
                var platformName = _platformRegistry?.GetDisplayName(platform) ?? platform.ToString();
                results.Add(new(platform, false, $"Применение профилей {platformName} недоступно"));
            }
        }

        var success = results.Any(static result => result.Succeeded);

        if (success)
        {
            await _eventBus.PublishAsync(new BroadcastProfileApplied(profileToApply, results), cancellationToken);
        }
        else
        {
            var errorMessage = BroadcastProfileApplySummary.FormatFailureStatus(
                results,
                _platformRegistry is not null ? _platformRegistry.GetDisplayName : null);
            await _eventBus.PublishAsync(new BroadcastProfileApplyFailed(profileToApply, errorMessage, results), cancellationToken);
        }

        var written = _profilesStore.Mutate(bp =>
        {
            bp.LastAppliedProfileId = profile.Id;

            if (success && hasPlaceholder)
            {
                var stored = bp.Profiles.FirstOrDefault(p => p.Id == profile.Id);
                if (stored != null)
                {
                    stored.LastApplyAt = _timeProvider.GetUtcNow();
                }
            }
        });

        _logger.LogInformation("Профиль трансляции применён: {ProfileName} (Id={ProfileId}, success={Success}, {Outcome})",
            profile.Name,
            profile.Id,
            success,
            written ? "отметка применения записана" : "отметка применения принята только в памяти до перезапуска");

        return profile;
    }

    public virtual BroadcastProfileSyncSummary EvaluateSync(
        BroadcastProfile profile,
        IChatPlatformRegistry platformRegistry)
    {
        ArgumentNullException.ThrowIfNull(profile);
        ArgumentNullException.ThrowIfNull(platformRegistry);

        var targets = GetPlatforms(profile.Id).Order().ToArray();
        if (targets.Length == 0)
        {
            return BroadcastProfileSyncSummary.Empty;
        }

        var drifted = new List<StreamingPlatform>();
        var pending = new List<StreamingPlatform>();

        foreach (var platform in targets)
        {
            var stream = platformRegistry.TryGet(platform, out var chatPlatform)
                ? chatPlatform.StreamStatus.CurrentStream
                : null;

            var evaluation = _appliers.TryGetValue(platform, out var platformApplier)
                ? platformApplier.EvaluateSync(profile, stream)
                : BroadcastProfileSyncMatcher.EvaluateTitleOnly(platform, profile.Title?.Trim() ?? string.Empty, stream);

            switch (evaluation.State)
            {
                case BroadcastProfileSyncState.Drifted:
                    drifted.Add(platform);
                    break;

                case BroadcastProfileSyncState.Pending:
                case BroadcastProfileSyncState.None:
                    pending.Add(platform);
                    break;
            }
        }

        if (drifted.Count > 0)
        {
            return new(BroadcastProfileSyncState.Drifted, drifted, pending);
        }

        if (pending.Count > 0)
        {
            return new(BroadcastProfileSyncState.Pending, drifted, pending);
        }

        return new(BroadcastProfileSyncState.Synced, drifted, pending);
    }

    public virtual BroadcastProfile? Find(Guid id)
    {
        return _profilesStore.Load()
            .Profiles
            .FirstOrDefault(p => p.Id == id);
    }

    public virtual bool AdvanceCurrentNumber(Guid id, int expectedCurrentNumber, int nextValue, DateTimeOffset advancedAt)
    {
        var outcome = AdvanceOutcome.NotFound;

        var written = _profilesStore.Mutate(bp =>
        {
            var stored = bp.Profiles.FirstOrDefault(p => p.Id == id);

            if (stored == null)
            {
                outcome = AdvanceOutcome.NotFound;
                return;
            }

            if (stored.CurrentNumber != expectedCurrentNumber)
            {
                outcome = AdvanceOutcome.RaceDetected;
                return;
            }

            stored.CurrentNumber = nextValue;
            stored.LastAutoAdvanceAt = advancedAt;
            outcome = AdvanceOutcome.Advanced;
        });

        switch (outcome)
        {
            case AdvanceOutcome.Advanced:
                _logger.LogDebug("AdvanceCurrentNumber: профиль Id={ProfileId} → CurrentNumber={NextValue}, {Outcome}",
                    id,
                    nextValue,
                    written ? "записано" : "принято только в памяти до перезапуска");

                return true;

            case AdvanceOutcome.RaceDetected:
                _logger.LogDebug("AdvanceCurrentNumber: профиль Id={ProfileId} изменён вручную, авто-инкремент отменён", id);
                return false;

            default:
                return false;
        }
    }

    public IReadOnlyList<BroadcastProfile> GetAll()
    {
        return _profilesStore.Load().Profiles.ToList();
    }

    public IReadOnlySet<StreamingPlatform> GetPlatforms(Guid profileId)
    {
        return _profilePlatformsStore?.GetPlatformsOrDefault(profileId)
               ?? new HashSet<StreamingPlatform> { StreamingPlatform.Twitch };
    }

    public BroadcastProfileYouTubeMetadata? GetYouTubeMetadata(Guid profileId)
    {
        return _youtubeStore?.GetMetadata(profileId);
    }

    public bool Upsert(BroadcastProfile profile)
    {
        return Upsert(profile, platforms: null, youtubeMetadata: null);
    }

    public bool Upsert(BroadcastProfile profile, IReadOnlyCollection<StreamingPlatform>? platforms)
    {
        return Upsert(profile, platforms, youtubeMetadata: null);
    }

    public bool Upsert(
        BroadcastProfile profile,
        IReadOnlyCollection<StreamingPlatform>? platforms,
        BroadcastProfileYouTubeMetadata? youtubeMetadata)
    {
        if (string.IsNullOrWhiteSpace(profile.Name))
        {
            throw new InvalidOperationException("Имя профиля не может быть пустым");
        }

        BroadcastProfile? previous = null;
        var previousIndex = -1;
        var hadPreviousPlatforms = false;
        List<StreamingPlatform>? previousPlatforms = null;

        if (platforms is not null && _profilePlatformsStore is not null)
        {
            var currentPlatforms = _profilePlatformsStore.Load().Profiles;
            if (currentPlatforms.TryGetValue(profile.Id, out var existingPlatforms))
            {
                hadPreviousPlatforms = true;
                previousPlatforms = existingPlatforms.ToList();
            }
        }

        var written = _profilesStore.Mutate(bp =>
        {
            var profiles = bp.Profiles;

            var duplicate = profiles.FirstOrDefault(p =>
                p.Id != profile.Id && string.Equals(p.Name, profile.Name, StringComparison.OrdinalIgnoreCase));

            if (duplicate != null)
            {
                throw new InvalidOperationException($"Профиль с именем '{profile.Name}' уже существует");
            }

            if (!string.IsNullOrWhiteSpace(profile.ObsSceneName))
            {
                var sceneDuplicate = profiles.FirstOrDefault(p =>
                    p.Id != profile.Id
                    && string.Equals(p.ObsSceneName.Trim(), profile.ObsSceneName.Trim(), StringComparison.OrdinalIgnoreCase));

                if (sceneDuplicate != null)
                {
                    throw new InvalidOperationException($"Сцена OBS «{profile.ObsSceneName.Trim()}» уже привязана к профилю «{sceneDuplicate.Name}»");
                }
            }

            var existing = profiles.FirstOrDefault(p => p.Id == profile.Id);

            if (existing == null)
            {
                profiles.Add(profile);
            }
            else
            {
                previousIndex = profiles.IndexOf(existing);
                previous = existing;
                profiles[previousIndex] = profile;
            }
        });

        if (!written)
        {
            _logger.LogWarning("Запись профиля трансляции {ProfileName} (Id={ProfileId}) отклонена затвором записи", profile.Name, profile.Id);
            return false;
        }

        var platformsMutated = false;
        if (platforms is not null && _profilePlatformsStore is not null)
        {
            bool platformsWritten;
            try
            {
                platformsWritten = _profilePlatformsStore.SetPlatforms(profile.Id, platforms);
            }
            catch
            {
                RollbackProfile(profile.Id, previous, previousIndex);
                throw;
            }

            if (!platformsWritten)
            {
                RollbackProfile(profile.Id, previous, previousIndex);
                _logger.LogWarning("Не удалось сохранить площадки профиля {ProfileName} (Id={ProfileId}); изменения профиля откачены", profile.Name, profile.Id);
                return false;
            }

            platformsMutated = true;
        }

        if (youtubeMetadata is not null && _youtubeStore is not null)
        {
            bool youtubeWritten;
            try
            {
                youtubeWritten = _youtubeStore.SetMetadata(profile.Id, youtubeMetadata);
            }
            catch
            {
                if (platformsMutated)
                {
                    RollbackPlatforms(profile.Id, hadPreviousPlatforms, previousPlatforms);
                }

                RollbackProfile(profile.Id, previous, previousIndex);
                throw;
            }

            if (!youtubeWritten)
            {
                if (platformsMutated)
                {
                    RollbackPlatforms(profile.Id, hadPreviousPlatforms, previousPlatforms);
                }

                RollbackProfile(profile.Id, previous, previousIndex);
                _logger.LogWarning("Не удалось сохранить метаданные YouTube профиля {ProfileName} (Id={ProfileId}); изменения профиля откачены", profile.Name, profile.Id);
                return false;
            }
        }

        _logger.LogInformation("Upsert профиля трансляции {ProfileName} (Id={ProfileId})", profile.Name, profile.Id);
        _ = _eventBus.PublishAsync(new BroadcastProfilesChanged());

        return true;
    }

    private void RollbackPlatforms(Guid id, bool hadPrevious, List<StreamingPlatform>? previousPlatforms)
    {
        if (_profilePlatformsStore is null)
        {
            return;
        }

        try
        {
            if (hadPrevious && previousPlatforms is not null)
            {
                _profilePlatformsStore.SetPlatforms(id, previousPlatforms);
            }
            else
            {
                _profilePlatformsStore.RemoveProfile(id);
            }
        }
        catch (Exception rollbackException)
        {
            _logger.LogWarning(rollbackException, "Не удалось откатить площадки профиля трансляции Id={ProfileId}", id);
        }
    }

    private void RollbackProfile(Guid id, BroadcastProfile? previous, int previousIndex)
    {
        try
        {
            _profilesStore.Mutate(bp =>
            {
                if (previous is null)
                {
                    bp.Profiles.RemoveAll(p => p.Id == id);
                    return;
                }

                var currentIndex = bp.Profiles.FindIndex(p => p.Id == id);
                if (currentIndex >= 0)
                {
                    bp.Profiles[currentIndex] = previous;
                }
                else
                {
                    var insertAt = Math.Clamp(previousIndex, 0, bp.Profiles.Count);
                    bp.Profiles.Insert(insertAt, previous);
                }
            });
        }
        catch (Exception rollbackException)
        {
            _logger.LogWarning(rollbackException, "Не удалось откатить профиль трансляции Id={ProfileId} после сбоя сохранения площадок", id);
        }
    }

    public bool Remove(Guid id)
    {
        var removed = 0;

        var written = _profilesStore.Mutate(bp =>
        {
            removed = bp.Profiles.RemoveAll(p => p.Id == id);

            if (bp.LastAppliedProfileId == id)
            {
                bp.LastAppliedProfileId = null;
            }
        });

        if (removed > 0)
        {
            _profilePlatformsStore?.RemoveProfile(id);
            _youtubeStore?.RemoveProfile(id);
            _logger.LogInformation("Удалён профиль трансляции Id={ProfileId}", id);
        }
        else
        {
            _logger.LogDebug("Запрос на удаление профиля трансляции Id={ProfileId} – записей не найдено", id);
        }

        _ = _eventBus.PublishAsync(new BroadcastProfilesChanged());

        return written;
    }

    private sealed class LegacyTwitchPlatformApplier(IChannelInformationApplier inner) : IPlatformBroadcastProfileApplier
    {
        public StreamingPlatform Platform => StreamingPlatform.Twitch;

        public async Task<BroadcastProfilePlatformApplyResult> ApplyAsync(
            BroadcastProfile profile,
            string resolvedTitle,
            CancellationToken cancellationToken)
        {
            var effectiveProfile = BroadcastProfileSyncMatcher.WithResolvedTitle(profile, resolvedTitle);
            var succeeded = await inner.ApplyAsync(effectiveProfile, cancellationToken);
            return new(StreamingPlatform.Twitch, succeeded, succeeded ? null : "Не удалось обновить канал Twitch");
        }

        public BroadcastProfileSyncEvaluation EvaluateSync(BroadcastProfile profile, StreamInfo? stream)
        {
            return BroadcastProfileSyncMatcher.EvaluateTwitch(profile, stream);
        }
    }

    private sealed class LegacyYouTubePlatformApplier(
        IYouTubeBroadcastProfileApplier inner,
        BroadcastProfileYouTubeStore? youtubeStore) : IPlatformBroadcastProfileApplier
    {
        public StreamingPlatform Platform => StreamingPlatform.YouTube;

        public Task<BroadcastProfilePlatformApplyResult> ApplyAsync(
            BroadcastProfile profile,
            string resolvedTitle,
            CancellationToken cancellationToken)
        {
            var effectiveProfile = BroadcastProfileSyncMatcher.WithResolvedTitle(profile, resolvedTitle);
            return inner.ApplyAsync(effectiveProfile, youtubeStore?.GetMetadata(profile.Id), cancellationToken);
        }

        public BroadcastProfileSyncEvaluation EvaluateSync(BroadcastProfile profile, StreamInfo? stream)
        {
            var normalized = youtubeStore?.GetMetadata(profile.Id)?.Normalize();
            var expectedTitle = !string.IsNullOrWhiteSpace(normalized?.TitleOverride)
                ? normalized.TitleOverride
                : profile.Title?.Trim() ?? string.Empty;

            return BroadcastProfileSyncMatcher.EvaluateTitleOnly(StreamingPlatform.YouTube, expectedTitle, stream);
        }
    }
}
