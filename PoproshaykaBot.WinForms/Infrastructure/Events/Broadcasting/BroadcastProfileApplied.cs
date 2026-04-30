using PoproshaykaBot.WinForms.Broadcast.Profiles;

namespace PoproshaykaBot.WinForms.Infrastructure.Events.Broadcasting;

public sealed record BroadcastProfileApplied(BroadcastProfile Profile) : EventBase;
