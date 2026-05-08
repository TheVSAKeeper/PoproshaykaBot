using PoproshaykaBot.Core.Streaming;

namespace PoproshaykaBot.Core.Infrastructure.Events.Streaming;

public sealed record StreamWentOnline(string Channel, StreamInfo? Stream, bool IsCatchUp = false) : EventBase;
