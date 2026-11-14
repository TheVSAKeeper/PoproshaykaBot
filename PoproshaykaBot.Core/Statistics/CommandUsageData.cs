using System.Text.Json.Serialization;

namespace PoproshaykaBot.Core.Statistics;

public sealed class CommandUsageData
{
    [JsonPropertyName("streamId")]
    public string BroadcastId { get; set; } = string.Empty;

    public List<CommandUsageRecord> Commands { get; set; } = [];
}
