using PoproshaykaBot.Core.Polls;

namespace PoproshaykaBot.Core.Infrastructure.Events.Polling;

public sealed record PollFinalized(PollSnapshot Snapshot, PollChoiceSnapshot? Winner, bool WinnerIsTie) : EventBase;
