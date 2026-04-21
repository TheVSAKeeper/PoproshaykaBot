using PoproshaykaBot.WinForms.Broadcast.Profiles;

namespace PoproshaykaBot.WinForms.Infrastructure.Events.Broadcasting;

public sealed record BroadcastProfileApplyFailed(BroadcastProfile Profile, string ErrorMessage) : EventBase;
