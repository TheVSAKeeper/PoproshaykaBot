using PoproshaykaBot.WinForms.Polls;

namespace PoproshaykaBot.WinForms.Infrastructure.Events.Polling;

public sealed record PollFinalized(PollSnapshot Snapshot, PollChoiceSnapshot? Winner, bool WinnerIsTie) : EventBase;
