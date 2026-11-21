using PoproshaykaBot.Core.Broadcast.Profiles;

namespace PoproshaykaBot.Core.Infrastructure.Events.Broadcasting;

public sealed record BroadcastProfileApplyFailed(
    BroadcastProfile Profile,
    string ErrorMessage,
    IReadOnlyList<BroadcastProfilePlatformApplyResult>? PlatformResults = null) : EventBase;
