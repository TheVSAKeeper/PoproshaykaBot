namespace PoproshaykaBot.Core.Infrastructure.Events.Broadcasting;

public sealed record ChannelInformationPatchFailed(string? Title, string? GameId, string? GameName, string ErrorMessage) : EventBase;
