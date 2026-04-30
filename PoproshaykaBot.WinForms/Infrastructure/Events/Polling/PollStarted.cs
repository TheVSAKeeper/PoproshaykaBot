using PoproshaykaBot.WinForms.Polls;

namespace PoproshaykaBot.WinForms.Infrastructure.Events.Polling;

public sealed record PollStarted(PollSnapshot Snapshot) : EventBase;
