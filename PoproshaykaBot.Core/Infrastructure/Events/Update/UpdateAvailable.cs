namespace PoproshaykaBot.Core.Infrastructure.Events.Update;

public sealed record UpdateAvailable(string Version, string NotesUrl) : EventBase;
