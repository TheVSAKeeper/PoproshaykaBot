using PoproshaykaBot.WinForms.Polls;

namespace PoproshaykaBot.WinForms.Infrastructure.Events.Polling;

public sealed record PollTerminated(PollSnapshot Snapshot) : EventBase;
