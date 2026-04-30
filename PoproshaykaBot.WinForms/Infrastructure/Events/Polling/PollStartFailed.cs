namespace PoproshaykaBot.WinForms.Infrastructure.Events.Polling;

public sealed record PollStartFailed(Guid? SourceProfileId, string ProfileName, string SafeMessage) : EventBase;
