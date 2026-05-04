using Microsoft.Extensions.Logging;
using PoproshaykaBot.Core.Chat;
using PoproshaykaBot.Core.Infrastructure.Events;
using PoproshaykaBot.Core.Infrastructure.Events.Broadcasting;
using PoproshaykaBot.Core.Settings.Stores;

namespace PoproshaykaBot.Core.Broadcast.Profiles;

public class BroadcastProfilesManager(
    BroadcastProfilesStore profilesStore,
    IChannelInformationApplier applier,
    IEventBus eventBus,
    TimeProvider timeProvider,
    ILogger<BroadcastProfilesManager> logger)
{
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
                CurrentNumber = profile.CurrentNumber,
                LastApplyAt = profile.LastApplyAt,
            };
        }
        else
        {
            profileToApply = profile;
        }

        var success = await applier.ApplyAsync(profileToApply, cancellationToken);

        profilesStore.Mutate(bp =>
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

        logger.LogInformation("Профиль трансляции применён: {ProfileName} (Id={ProfileId}, success={Success})",
            profile.Name,
            profile.Id,
            success);

        return profile;
    }

    public virtual BroadcastProfile? Find(Guid id)
    {
        return profilesStore.Load()
            .Profiles
            .FirstOrDefault(p => p.Id == id);
    }

    public virtual bool AdvanceCurrentNumber(Guid id, int nextValue, DateTimeOffset appliedAt)
    {
        var advanced = false;

        profilesStore.Mutate(bp =>
        {
            var stored = bp.Profiles.FirstOrDefault(p => p.Id == id);

            if (stored == null)
            {
                return;
            }

            stored.CurrentNumber = nextValue;
            stored.LastApplyAt = appliedAt;
            advanced = true;
        });

        if (advanced)
        {
            logger.LogDebug("AdvanceCurrentNumber: профиль Id={ProfileId} → CurrentNumber={NextValue}",
                id,
                nextValue);
        }

        return advanced;
    }

    public IReadOnlyList<BroadcastProfile> GetAll()
    {
        return profilesStore.Load().Profiles.ToList();
    }

    public void Upsert(BroadcastProfile profile)
    {
        if (string.IsNullOrWhiteSpace(profile.Name))
        {
            throw new InvalidOperationException("Имя профиля не может быть пустым");
        }

        profilesStore.Mutate(bp =>
        {
            var profiles = bp.Profiles;

            var duplicate = profiles.FirstOrDefault(p =>
                p.Id != profile.Id && string.Equals(p.Name, profile.Name, StringComparison.OrdinalIgnoreCase));

            if (duplicate != null)
            {
                throw new InvalidOperationException($"Профиль с именем '{profile.Name}' уже существует");
            }

            var existing = profiles.FirstOrDefault(p => p.Id == profile.Id);

            if (existing == null)
            {
                profiles.Add(profile);
            }
            else
            {
                var index = profiles.IndexOf(existing);
                profiles[index] = profile;
            }
        });

        logger.LogInformation("Upsert профиля трансляции {ProfileName} (Id={ProfileId})", profile.Name, profile.Id);
        _ = eventBus.PublishAsync(new BroadcastProfilesChanged());
    }

    public void Remove(Guid id)
    {
        var removed = 0;

        profilesStore.Mutate(bp =>
        {
            removed = bp.Profiles.RemoveAll(p => p.Id == id);

            if (bp.LastAppliedProfileId == id)
            {
                bp.LastAppliedProfileId = null;
            }
        });

        if (removed > 0)
        {
            logger.LogInformation("Удалён профиль трансляции Id={ProfileId}", id);
        }
        else
        {
            logger.LogDebug("Запрос на удаление профиля трансляции Id={ProfileId} — записей не найдено", id);
        }

        _ = eventBus.PublishAsync(new BroadcastProfilesChanged());
    }
}
