using PoproshaykaBot.Core.Platforms;
using System.Text.Json.Serialization;

namespace PoproshaykaBot.Core.Broadcast.Profiles;

public sealed class BroadcastProfilePlatformsSettings
{
    [JsonObjectCreationHandling(JsonObjectCreationHandling.Populate)]
    public Dictionary<Guid, List<StreamingPlatform>> Profiles { get; init; } = [];
}
