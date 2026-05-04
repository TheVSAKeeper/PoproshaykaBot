namespace PoproshaykaBot.Core.Infrastructure.Events.Broadcasting;

public sealed record ChannelInformationPatched(string? Title, string? GameId, string? GameName) : EventBase;
