using PoproshaykaBot.Core.Streaming;

namespace PoproshaykaBot.Core.Infrastructure.Events.Streaming;

public sealed record StreamMetadataResolved(string Channel, StreamInfo Stream) : EventBase;
