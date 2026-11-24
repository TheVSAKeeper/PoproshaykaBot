using PoproshaykaBot.Core.Platforms;
using PoproshaykaBot.Core.Streaming;

namespace PoproshaykaBot.Core.Broadcast.Profiles;

public interface IPlatformBroadcastProfileApplier
{
    StreamingPlatform Platform { get; }

    Task<BroadcastProfilePlatformApplyResult> ApplyAsync(
        BroadcastProfile profile,
        string resolvedTitle,
        CancellationToken cancellationToken);

    BroadcastProfileSyncEvaluation EvaluateSync(BroadcastProfile profile, StreamInfo? stream);
}
