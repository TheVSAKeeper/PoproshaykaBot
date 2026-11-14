using PoproshaykaBot.Core.Platforms;

namespace PoproshaykaBot.Core.Statistics;

public sealed record BroadcastChatterEntry(PlatformUserKey Key, string DisplayName, long MessageCount);
