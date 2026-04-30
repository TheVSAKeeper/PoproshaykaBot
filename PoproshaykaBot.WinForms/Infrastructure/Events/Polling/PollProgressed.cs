using PoproshaykaBot.WinForms.Polls;

namespace PoproshaykaBot.WinForms.Infrastructure.Events.Polling;

public sealed record PollProgressed(PollSnapshot Snapshot) : EventBase;
