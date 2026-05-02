using Microsoft.Extensions.Logging;
using PoproshaykaBot.WinForms.Chat;
using PoproshaykaBot.WinForms.Infrastructure.Events;
using PoproshaykaBot.WinForms.Infrastructure.Events.Broadcasting;
using PoproshaykaBot.WinForms.Settings.Stores;

namespace PoproshaykaBot.WinForms.Broadcast.Profiles;

// TODO: Логгер не используется
public class BroadcastProfilesManager(
    BroadcastProfilesStore profilesStore,
    IChannelInformationApplier applier,
    IEventBus eventBus,
    TimeProvider timeProvider,
    ILogger<BroadcastProfilesManager> logger)
{
    private readonly object _syncLock = new();

    public virtual BroadcastProfile? FindByName(string name)
    {
        lock (_syncLock)
        {
            return profilesStore.Load()
                .Profiles
                .FirstOrDefault(p => string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase));
        }
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

        lock (_syncLock)
        {
            var bp = profilesStore.Load();
            bp.LastAppliedProfileId = profile.Id;

            if (success && hasPlaceholder)
            {
                var stored = bp.Profiles.FirstOrDefault(p => p.Id == profile.Id);
                if (stored != null)
                {
                    stored.LastApplyAt = timeProvider.GetUtcNow();
                }
            }

            profilesStore.Save();
        }

        return profile;
    }

    public virtual BroadcastProfile? Find(Guid id)
    {
        lock (_syncLock)
        {
            return profilesStore.Load()
                .Profiles
                .FirstOrDefault(p => p.Id == id);
        }
    }

    public virtual bool AdvanceCurrentNumber(Guid id, int nextValue, DateTimeOffset appliedAt)
    {
        lock (_syncLock)
        {
            var bp = profilesStore.Load();
            var stored = bp.Profiles.FirstOrDefault(p => p.Id == id);

            if (stored == null)
            {
                return false;
            }

            stored.CurrentNumber = nextValue;
            stored.LastApplyAt = appliedAt;
            profilesStore.Save();
            return true;
        }
    }

    public IReadOnlyList<BroadcastProfile> GetAll()
    {
        lock (_syncLock)
        {
            return profilesStore.Load().Profiles.ToList();
        }
    }

    public void Upsert(BroadcastProfile profile)
    {
        if (string.IsNullOrWhiteSpace(profile.Name))
        {
            throw new InvalidOperationException("Имя профиля не может быть пустым");
        }

        lock (_syncLock)
        {
            var bp = profilesStore.Load();
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

            profilesStore.Save();
        }

        _ = eventBus.PublishAsync(new BroadcastProfilesChanged());
    }

    public void Remove(Guid id)
    {
        lock (_syncLock)
        {
            var bp = profilesStore.Load();
            bp.Profiles.RemoveAll(p => p.Id == id);

            if (bp.LastAppliedProfileId == id)
            {
                bp.LastAppliedProfileId = null;
            }

            profilesStore.Save();
        }

        _ = eventBus.PublishAsync(new BroadcastProfilesChanged());
    }
}
