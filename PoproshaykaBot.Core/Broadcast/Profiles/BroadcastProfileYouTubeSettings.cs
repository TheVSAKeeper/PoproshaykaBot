using System.Text.Json.Serialization;

namespace PoproshaykaBot.Core.Broadcast.Profiles;

public sealed class BroadcastProfileYouTubeSettings
{
    [JsonObjectCreationHandling(JsonObjectCreationHandling.Populate)]
    public Dictionary<Guid, BroadcastProfileYouTubeMetadata> Profiles { get; set; } = [];
}
