using PoproshaykaBot.Core.Polls;

namespace PoproshaykaBot.Core.Infrastructure.Events.Polling;

public sealed record PollProgressed(PollSnapshot Snapshot) : EventBase;
