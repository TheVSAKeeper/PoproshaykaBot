using Microsoft.Extensions.Logging;
using PoproshaykaBot.Core.Chat;
using PoproshaykaBot.Core.Infrastructure.Events;
using PoproshaykaBot.Core.Infrastructure.Events.Broadcasting;
using PoproshaykaBot.Core.Platforms;
using PoproshaykaBot.Core.Settings.Stores;

namespace PoproshaykaBot.Core.Broadcast.Profiles;

public class BroadcastProfilesManager(
    BroadcastProfilesStore profilesStore,
    IChannelInformationApplier applier,
    IEventBus eventBus,
    TimeProvider timeProvider,
    ILogger<BroadcastProfilesManager> logger,
    BroadcastProfilePlatformsStore? profilePlatformsStore = null)
{
    private enum AdvanceOutcome
    {
        NotFound = 0,
        RaceDetected = 1,
        Advanced = 2,
    }

    public virtual BroadcastProfile? FindByName(string name)
    {
        return profilesStore.Load()
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
            logger.LogDebug("ApplyAsync: профиль Id={ProfileId} не найден", id);
            return null;
        }

        var template = MessageTemplate.For(profile.Title);
        var hasPlaceholder = template.Contains("n");

        BroadcastProfile profileToApply;
        if (hasPlaceholder)
        {
            profileToApply = new()
            {
                Id = profile.Id,
                Name = profile.Name,
                Title = template.With("n", profile.CurrentNumber.ToString()).Render(),
                GameId = profile.GameId,
                GameName = profile.GameName,
                BroadcasterLanguage = profile.BroadcasterLanguage,
                Tags = profile.Tags.ToList(),
                ObsSceneName = profile.ObsSceneName,
                CurrentNumber = profile.CurrentNumber,
                LastApplyAt = profile.LastApplyAt,
                LastAutoAdvanceAt = profile.LastAutoAdvanceAt,
            };
        }
        else
        {
            profileToApply = profile;
        }

        _ = eventBus.PublishAsync(new BroadcastProfileApplying(profileToApply), cancellationToken);

        var success = await applier.ApplyAsync(profileToApply, cancellationToken);

        var written = profilesStore.Mutate(bp =>
        {
            bp.LastAppliedProfileId = profile.Id;

            if (success && hasPlaceholder)
            {
                var stored = bp.Profiles.FirstOrDefault(p => p.Id == profile.Id);
                if (stored != null)
                {
                    stored.LastApplyAt = timeProvider.GetUtcNow();
                }
            }
        });

        logger.LogInformation("Профиль трансляции применён: {ProfileName} (Id={ProfileId}, success={Success}, {Outcome})",
            profile.Name,
            profile.Id,
            success,
            written ? "отметка применения записана" : "отметка применения принята только в памяти до перезапуска");

        return profile;
    }

    public virtual BroadcastProfile? Find(Guid id)
    {
        return profilesStore.Load()
            .Profiles
            .FirstOrDefault(p => p.Id == id);
    }

    public virtual bool AdvanceCurrentNumber(Guid id, int expectedCurrentNumber, int nextValue, DateTimeOffset advancedAt)
    {
        var outcome = AdvanceOutcome.NotFound;

        var written = profilesStore.Mutate(bp =>
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
                logger.LogDebug("AdvanceCurrentNumber: профиль Id={ProfileId} → CurrentNumber={NextValue}, {Outcome}",
                    id,
                    nextValue,
                    written ? "записано" : "принято только в памяти до перезапуска");

                return true;

            case AdvanceOutcome.RaceDetected:
                logger.LogDebug("AdvanceCurrentNumber: профиль Id={ProfileId} изменён вручную, авто-инкремент отменён", id);
                return false;

            default:
                return false;
        }
    }

    public IReadOnlyList<BroadcastProfile> GetAll()
    {
        return profilesStore.Load().Profiles.ToList();
    }

    public IReadOnlySet<StreamingPlatform> GetPlatforms(Guid profileId)
    {
        return profilePlatformsStore?.GetPlatformsOrDefault(profileId)
               ?? new HashSet<StreamingPlatform> { StreamingPlatform.Twitch };
    }

    public bool Upsert(BroadcastProfile profile)
    {
        return Upsert(profile, platforms: null);
    }

    public bool Upsert(BroadcastProfile profile, IReadOnlyCollection<StreamingPlatform>? platforms)
    {
        if (string.IsNullOrWhiteSpace(profile.Name))
        {
            throw new InvalidOperationException("Имя профиля не может быть пустым");
        }

        BroadcastProfile? previous = null;
        var previousIndex = -1;

        var written = profilesStore.Mutate(bp =>
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
            logger.LogWarning("Запись профиля трансляции {ProfileName} (Id={ProfileId}) отклонена затвором записи", profile.Name, profile.Id);
            return false;
        }

        if (platforms is not null && profilePlatformsStore is not null)
        {
            bool platformsWritten;
            try
            {
                platformsWritten = profilePlatformsStore.SetPlatforms(profile.Id, platforms);
            }
            catch
            {
                RollbackProfile(profile.Id, previous, previousIndex);
                throw;
            }

            if (!platformsWritten)
            {
                RollbackProfile(profile.Id, previous, previousIndex);
                logger.LogWarning("Не удалось сохранить площадки профиля {ProfileName} (Id={ProfileId}); изменения профиля откачены", profile.Name, profile.Id);
                return false;
            }
        }

        logger.LogInformation("Upsert профиля трансляции {ProfileName} (Id={ProfileId})", profile.Name, profile.Id);
        _ = eventBus.PublishAsync(new BroadcastProfilesChanged());

        return true;
    }

    private void RollbackProfile(Guid id, BroadcastProfile? previous, int previousIndex)
    {
        try
        {
            profilesStore.Mutate(bp =>
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
            logger.LogWarning(rollbackException, "Не удалось откатить профиль трансляции Id={ProfileId} после сбоя сохранения площадок", id);
        }
    }

    public bool Remove(Guid id)
    {
        var removed = 0;

        var written = profilesStore.Mutate(bp =>
        {
            removed = bp.Profiles.RemoveAll(p => p.Id == id);

            if (bp.LastAppliedProfileId == id)
            {
                bp.LastAppliedProfileId = null;
            }
        });

        if (removed > 0)
        {
            profilePlatformsStore?.RemoveProfile(id);
            logger.LogInformation("Удалён профиль трансляции Id={ProfileId}", id);
        }
        else
        {
            logger.LogDebug("Запрос на удаление профиля трансляции Id={ProfileId} – записей не найдено", id);
        }

        _ = eventBus.PublishAsync(new BroadcastProfilesChanged());

        return written;
    }
}
