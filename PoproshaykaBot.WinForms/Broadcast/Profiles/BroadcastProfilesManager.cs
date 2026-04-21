using Microsoft.Extensions.Logging;
using PoproshaykaBot.WinForms.Infrastructure.Events;
using PoproshaykaBot.WinForms.Infrastructure.Events.Broadcasting;
using PoproshaykaBot.WinForms.Settings;

namespace PoproshaykaBot.WinForms.Broadcast.Profiles;

// TODO: Логгер не используется
public class BroadcastProfilesManager(
    SettingsManager settingsManager,
    IChannelInformationApplier applier,
    IEventBus eventBus,
    ILogger<BroadcastProfilesManager> logger)
{
    private readonly object _syncLock = new();

    public virtual BroadcastProfile? FindByName(string name)
    {
        lock (_syncLock)
        {
            return settingsManager.Current.Twitch.BroadcastProfiles.Profiles
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

        await applier.ApplyAsync(profile, cancellationToken);

        lock (_syncLock)
        {
            var settings = settingsManager.Current;
            settings.Twitch.BroadcastProfiles.LastAppliedProfileId = profile.Id;
            settingsManager.SaveSettings(settings);
        }

        return profile;
    }

    public IReadOnlyList<BroadcastProfile> GetAll()
    {
        lock (_syncLock)
        {
            return settingsManager.Current.Twitch.BroadcastProfiles.Profiles.ToList();
        }
    }

    public BroadcastProfile? Find(Guid id)
    {
        lock (_syncLock)
        {
            return settingsManager.Current.Twitch.BroadcastProfiles.Profiles
                .FirstOrDefault(p => p.Id == id);
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
            var settings = settingsManager.Current;
            var profiles = settings.Twitch.BroadcastProfiles.Profiles;

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

            settingsManager.SaveSettings(settings);
        }

        _ = eventBus.PublishAsync(new BroadcastProfilesChanged());
    }

    public void Remove(Guid id)
    {
        lock (_syncLock)
        {
            var settings = settingsManager.Current;
            settings.Twitch.BroadcastProfiles.Profiles.RemoveAll(p => p.Id == id);

            if (settings.Twitch.BroadcastProfiles.LastAppliedProfileId == id)
            {
                settings.Twitch.BroadcastProfiles.LastAppliedProfileId = null;
            }

            settingsManager.SaveSettings(settings);
        }

        _ = eventBus.PublishAsync(new BroadcastProfilesChanged());
    }
}
