using PoproshaykaBot.WinForms.Streaming;

namespace PoproshaykaBot.WinForms.Infrastructure.Events.Streaming;

public sealed record StreamMetadataResolved(string Channel, StreamInfo Stream) : EventBase;
