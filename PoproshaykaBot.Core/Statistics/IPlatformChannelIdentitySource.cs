using PoproshaykaBot.Core.Platforms;

namespace PoproshaykaBot.Core.Statistics;

internal interface IPlatformChannelIdentitySource
{
    StreamingPlatform Platform { get; }

    void CollectKnownChannels(Action<string, IEnumerable<string?>> registerChannel);

    string? GetConfiguredChannelRaw();

    bool TryExtractDirectChannelId(string rawChannel, out string channelId)
    {
        channelId = string.Empty;
        return false;
    }

    string? NormalizeAlias(string rawAlias)
    {
        return string.IsNullOrWhiteSpace(rawAlias)
            ? null
            : rawAlias.Trim().TrimStart('@').ToLowerInvariant();
    }
}
