using PoproshaykaBot.WinForms.Streaming;

namespace PoproshaykaBot.WinForms.Infrastructure.Events.Streaming;

public sealed record StreamWentOnline(string Channel, StreamInfo? Stream, bool IsCatchUp = false) : EventBase;
