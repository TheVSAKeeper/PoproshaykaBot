using Microsoft.Extensions.Logging;
using PoproshaykaBot.WinForms.Infrastructure.Events;
using PoproshaykaBot.WinForms.Infrastructure.Events.Polling;
using PoproshaykaBot.WinForms.Settings.Stores;

namespace PoproshaykaBot.WinForms.Polls;

public class PollProfilesManager(
    PollsStore pollsStore,
    IEventBus eventBus,
    ILogger<PollProfilesManager> logger)
{
    private readonly object _syncLock = new();

    public virtual PollProfile? FindByName(string name)
    {
        lock (_syncLock)
        {
            return pollsStore.Load()
                .Profiles
                .FirstOrDefault(p => string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase));
        }
    }

    public virtual PollProfile? Find(Guid id)
    {
        lock (_syncLock)
        {
            return pollsStore.Load().Profiles.FirstOrDefault(p => p.Id == id);
        }
    }

    public virtual IReadOnlyList<PollProfile> GetAll()
    {
        lock (_syncLock)
        {
            return pollsStore.Load().Profiles.ToList();
        }
    }

    public virtual void Upsert(PollProfile profile)
    {
        Validate(profile);

        lock (_syncLock)
        {
            var polls = pollsStore.Load();
            var profiles = polls.Profiles;

            var duplicate = profiles.FirstOrDefault(p =>
                p.Id != profile.Id && string.Equals(p.Name, profile.Name, StringComparison.OrdinalIgnoreCase));

            if (duplicate != null)
            {
                throw new InvalidOperationException($"Профиль голосования с именем '{profile.Name}' уже существует");
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

            pollsStore.Save();
        }

        logger.LogDebug("Upsert профиля голосования {ProfileName}", profile.Name);
        _ = eventBus.PublishAsync(new PollProfilesChanged());
    }

    public virtual void ReplaceAll(IEnumerable<PollProfile> profiles)
    {
        ArgumentNullException.ThrowIfNull(profiles);

        var snapshot = profiles.ToList();

        foreach (var profile in snapshot)
        {
            Validate(profile);
        }

        var seenIds = new HashSet<Guid>();
        var seenNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var profile in snapshot)
        {
            if (!seenIds.Add(profile.Id))
            {
                throw new InvalidOperationException($"Дублирующийся id профиля голосования: {profile.Id}");
            }

            if (!seenNames.Add(profile.Name))
            {
                throw new InvalidOperationException($"Профиль голосования с именем '{profile.Name}' уже существует");
            }
        }

        lock (_syncLock)
        {
            var polls = pollsStore.Load();
            polls.Profiles.Clear();
            polls.Profiles.AddRange(snapshot);
            pollsStore.Save();
        }

        logger.LogDebug("ReplaceAll профилей голосований ({Count})", snapshot.Count);
        _ = eventBus.PublishAsync(new PollProfilesChanged());
    }

    public virtual void Remove(Guid id)
    {
        lock (_syncLock)
        {
            var polls = pollsStore.Load();
            polls.Profiles.RemoveAll(p => p.Id == id);
            pollsStore.Save();
        }

        _ = eventBus.PublishAsync(new PollProfilesChanged());
    }

    private static void Validate(PollProfile profile)
    {
        if (string.IsNullOrWhiteSpace(profile.Name))
        {
            throw new InvalidOperationException("Имя профиля не может быть пустым");
        }

        if (profile.Choices.Count < PollProfile.MinChoices)
        {
            throw new InvalidOperationException($"Должно быть минимум {PollProfile.MinChoices} варианта");
        }

        if (profile.Choices.Count > PollProfile.MaxChoices)
        {
            throw new InvalidOperationException($"Должно быть максимум {PollProfile.MaxChoices} вариантов");
        }

        if (profile.Choices.Any(string.IsNullOrWhiteSpace))
        {
            throw new InvalidOperationException("Пустые варианты не допускаются");
        }

        if (profile.DurationSeconds < PollProfile.MinDurationSeconds || profile.DurationSeconds > PollProfile.MaxDurationSeconds)
        {
            throw new InvalidOperationException($"Длительность должна быть от {PollProfile.MinDurationSeconds} до {PollProfile.MaxDurationSeconds} секунд");
        }

        if (profile.ChannelPointsVotingEnabled && profile.ChannelPointsPerVote < PollProfile.MinChannelPointsPerVote)
        {
            throw new InvalidOperationException($"Стоимость голоса Channel Points должна быть не меньше {PollProfile.MinChannelPointsPerVote}");
        }

        if (profile.ChannelPointsVotingEnabled && profile.ChannelPointsPerVote > PollProfile.MaxChannelPointsPerVote)
        {
            throw new InvalidOperationException($"Стоимость голоса Channel Points не может превышать {PollProfile.MaxChannelPointsPerVote}");
        }
    }
}
