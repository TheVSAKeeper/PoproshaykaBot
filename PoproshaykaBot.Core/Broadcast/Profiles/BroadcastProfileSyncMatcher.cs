using PoproshaykaBot.Core.Platforms;
using PoproshaykaBot.Core.Streaming;
using System.Text.RegularExpressions;

namespace PoproshaykaBot.Core.Broadcast.Profiles;

internal static class BroadcastProfileSyncMatcher
{
    private static readonly TimeSpan TitleMatchTimeout = TimeSpan.FromMilliseconds(100);

    public static bool TitleMatches(string profileTitle, string streamTitle)
    {
        const string Placeholder = "{n}";

        if (!profileTitle.Contains(Placeholder, StringComparison.Ordinal))
        {
            return string.Equals(profileTitle, streamTitle, StringComparison.Ordinal);
        }

        var pattern = "^" + Regex.Escape(profileTitle).Replace(Regex.Escape(Placeholder), "\\d+") + "$";
        return Regex.IsMatch(streamTitle, pattern, RegexOptions.None, TitleMatchTimeout);
    }

    public static BroadcastProfile WithResolvedTitle(BroadcastProfile profile, string resolvedTitle)
    {
        if (string.Equals(profile.Title, resolvedTitle, StringComparison.Ordinal))
        {
            return profile;
        }

        return new()
        {
            Id = profile.Id,
            Name = profile.Name,
            Title = resolvedTitle,
            GameId = profile.GameId,
            GameName = profile.GameName,
            BroadcasterLanguage = profile.BroadcasterLanguage,
            Tags = profile.Tags.ToList(),
            ObsSceneName = profile.ObsSceneName,
            CurrentNumber = profile.CurrentNumber,
            LastApplyAt = profile.LastApplyAt,
            LastAutoAdvanceAt = profile.LastAutoAdvanceAt,
        };
    }

    public static BroadcastProfileSyncEvaluation EvaluateTwitch(BroadcastProfile profile, StreamInfo? stream)
    {
        if (stream is null or { IsBare: true })
        {
            return new(StreamingPlatform.Twitch, BroadcastProfileSyncState.Pending);
        }

        var expectedTitle = profile.Title?.Trim() ?? string.Empty;
        var actualTitle = stream.Title?.Trim() ?? string.Empty;

        if (!string.IsNullOrEmpty(expectedTitle) && !TitleMatches(expectedTitle, actualTitle))
        {
            return new(StreamingPlatform.Twitch, BroadcastProfileSyncState.Drifted, "Название отличается");
        }

        var expectedGameId = profile.GameId?.Trim() ?? string.Empty;
        var actualGameId = stream.GameId?.Trim() ?? string.Empty;

        if (!string.Equals(expectedGameId, actualGameId, StringComparison.Ordinal))
        {
            return new(StreamingPlatform.Twitch, BroadcastProfileSyncState.Drifted, "Категория отличается");
        }

        return new(StreamingPlatform.Twitch, BroadcastProfileSyncState.Synced);
    }

    public static BroadcastProfileSyncEvaluation EvaluateTitleOnly(
        StreamingPlatform platform,
        string expectedTitle,
        StreamInfo? stream)
    {
        if (stream is null or { IsBare: true })
        {
            return new(platform, BroadcastProfileSyncState.Pending);
        }

        if (string.IsNullOrEmpty(expectedTitle))
        {
            return new(platform, BroadcastProfileSyncState.Synced);
        }

        var actualTitle = stream.Title?.Trim() ?? string.Empty;

        return TitleMatches(expectedTitle, actualTitle)
            ? new(platform, BroadcastProfileSyncState.Synced)
            : new(platform, BroadcastProfileSyncState.Drifted, "Название отличается");
    }
}
