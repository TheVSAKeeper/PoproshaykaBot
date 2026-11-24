using PoproshaykaBot.Core.Platforms;
using PoproshaykaBot.Core.Streaming;

namespace PoproshaykaBot.Core.Broadcast.Profiles;

internal sealed class TwitchBroadcastProfileApplier(ChannelInformationApplier inner) : IPlatformBroadcastProfileApplier
{
    public StreamingPlatform Platform => StreamingPlatform.Twitch;

    public Task<BroadcastProfilePlatformApplyResult> ApplyAsync(
        BroadcastProfile profile,
        string resolvedTitle,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(profile);

        var effectiveProfile = BroadcastProfileSyncMatcher.WithResolvedTitle(profile, resolvedTitle);
        return inner.ApplyForPlatformAsync(effectiveProfile, cancellationToken);
    }

    public BroadcastProfileSyncEvaluation EvaluateSync(BroadcastProfile profile, StreamInfo? stream)
    {
        ArgumentNullException.ThrowIfNull(profile);
        return BroadcastProfileSyncMatcher.EvaluateTwitch(profile, stream);
    }
}
