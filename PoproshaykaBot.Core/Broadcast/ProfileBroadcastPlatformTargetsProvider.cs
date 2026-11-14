using PoproshaykaBot.Core.Platforms;
using PoproshaykaBot.Core.Settings.Stores;

namespace PoproshaykaBot.Core.Broadcast;

public sealed class ProfileBroadcastPlatformTargetsProvider(
    BroadcastProfilesStore profilesStore,
    BroadcastProfilePlatformsStore profilePlatformsStore) : IBroadcastPlatformTargetsProvider
{
    private static readonly IReadOnlySet<StreamingPlatform> DefaultTargets =
        new HashSet<StreamingPlatform> { StreamingPlatform.Twitch };

    public IReadOnlySet<StreamingPlatform> GetTargetPlatforms()
    {
        var profilesSettings = profilesStore.Load();

        if (profilesSettings.LastAppliedProfileId is not { } appliedId)
        {
            return DefaultTargets;
        }

        if (!profilesSettings.Profiles.Exists(profile => profile.Id == appliedId))
        {
            return DefaultTargets;
        }

        return profilePlatformsStore.GetPlatformsOrDefault(appliedId);
    }
}
