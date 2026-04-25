using PoproshaykaBot.WinForms.Polls;

namespace PoproshaykaBot.WinForms.Infrastructure.Events.Polling;

public sealed record PollArchived(PollSnapshot Snapshot) : EventBase;
