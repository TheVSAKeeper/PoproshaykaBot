namespace PoproshaykaBot.Core.Broadcast.Profiles;

public interface IYouTubeBroadcastProfileApplier
{
    Task<BroadcastProfilePlatformApplyResult> ApplyAsync(
        BroadcastProfile profile,
        BroadcastProfileYouTubeMetadata? youtubeMetadata,
        CancellationToken cancellationToken);
}
