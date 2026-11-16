using System.Text.Json.Serialization;

namespace PoproshaykaBot.Core.YouTube.Chat.InnerTube.Models;

internal sealed record LiveChatRequest
{
    [JsonPropertyName("context")]
    public required RequestContext Context { get; init; }

    [JsonPropertyName("continuation")]
    public string? Continuation { get; init; }
}

internal sealed record RequestContext
{
    [JsonPropertyName("client")]
    public required ClientInfo Client { get; init; }
}

internal sealed record ClientInfo
{
    [JsonPropertyName("clientVersion")]
    public required string ClientVersion { get; init; }

    [JsonPropertyName("clientName")]
    public required string ClientName { get; init; }

    [JsonPropertyName("hl")]
    public string? Hl { get; init; }

    [JsonPropertyName("gl")]
    public string? Gl { get; init; }
}
