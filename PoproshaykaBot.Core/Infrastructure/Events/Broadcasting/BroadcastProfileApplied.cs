using PoproshaykaBot.Core.Broadcast.Profiles;

namespace PoproshaykaBot.Core.Infrastructure.Events.Broadcasting;

public sealed record BroadcastProfileApplied(BroadcastProfile Profile) : EventBase;
