using PoproshaykaBot.Core.Platforms;
using PoproshaykaBot.Core.Settings.Stores;
using PoproshaykaBot.Core.Statistics;

namespace PoproshaykaBot.Core.Chat.Commands;

internal static class StatisticsCommandSliceParser
{
    private static readonly HashSet<string> GlobalKeywords = new(StringComparer.OrdinalIgnoreCase)
    {
        "все",
        "всё",
        "общий",
        "all",
        "total",
    };

    private static readonly HashSet<string> GroupKeywords = new(StringComparer.OrdinalIgnoreCase)
    {
        "группа",
        "group",
    };

    public static SliceParseResult Parse(CommandContext context, ChannelCatalogStore? channelCatalog)
    {
        var remaining = new List<string>(context.Arguments.Count);
        StatisticsSlice? explicitSlice = null;

        for (var i = 0; i < context.Arguments.Count; i++)
        {
            var arg = context.Arguments[i];
            if (string.IsNullOrWhiteSpace(arg))
            {
                continue;
            }

            var trimmed = arg.Trim();
            if (explicitSlice is null && !trimmed.StartsWith('@') && GlobalKeywords.Contains(trimmed))
            {
                explicitSlice = StatisticsSlice.All();
                continue;
            }

            if (explicitSlice is null && !trimmed.StartsWith('@') && GroupKeywords.Contains(trimmed))
            {
                var groupResult = TryParseGroup(context.Arguments, ref i, channelCatalog);
                if (!groupResult.Succeeded)
                {
                    return groupResult;
                }

                explicitSlice = groupResult.Slice;
                continue;
            }

            remaining.Add(trimmed);
        }

        var slice = explicitSlice
            ?? (context.ChannelKey is { } ck
                ? StatisticsSlice.ForChannel(ck)
                : StatisticsSlice.ForPlatform(context.Platform));

        return new(true, slice, remaining, null);
    }

    private static SliceParseResult TryParseGroup(
        IReadOnlyList<string> arguments,
        ref int index,
        ChannelCatalogStore? channelCatalog)
    {
        if (index + 1 >= arguments.Count)
        {
            return FailWithGroups("Укажи название группы каналов после «группа».", channelCatalog);
        }

        var groups = channelCatalog?.GetGroups() ?? [];
        if (groups.Count == 0)
        {
            return new(false, null, [], "Группы каналов ещё не настроены.");
        }

        var candidateTokens = new List<string>();
        for (var j = index + 1; j < arguments.Count; j++)
        {
            var token = arguments[j].Trim();
            if (string.IsNullOrWhiteSpace(token) || token.StartsWith('@'))
            {
                break;
            }

            candidateTokens.Add(token);
        }

        for (var len = candidateTokens.Count; len >= 1; len--)
        {
            var candidateName = string.Join(' ', candidateTokens.Take(len));
            var matched = groups.FirstOrDefault(g =>
                string.Equals(g.Name, candidateName, StringComparison.OrdinalIgnoreCase)
                || string.Equals(g.Id, candidateName, StringComparison.OrdinalIgnoreCase));

            if (matched is not null)
            {
                index += len;
                var members = ChannelCatalogStore.DeduplicateMembers(matched.Channels);
                return new(
                    true,
                    StatisticsSlice.ForGroup(matched.Id, matched.Name, members),
                    [],
                    null);
            }
        }

        var requestedName = candidateTokens.Count > 0 ? candidateTokens[0] : arguments[index + 1];
        return FailWithGroups($"Группа каналов «{requestedName}» не найдена.", channelCatalog);
    }

    private static SliceParseResult FailWithGroups(string prefix, ChannelCatalogStore? channelCatalog)
    {
        var groups = channelCatalog?.GetGroups() ?? [];
        if (groups.Count == 0)
        {
            return new(false, null, [], $"{prefix} Группы каналов пока не настроены.");
        }

        var names = string.Join(", ", groups.Select(static g => g.Name));
        return new(false, null, [], $"{prefix} Доступные группы: {names}");
    }

    public static ViewerLookupResult ResolveViewer(
        CommandContext context,
        IUserStatisticsRepository statistics,
        ChannelCatalogStore? channelCatalog)
    {
        var parsed = Parse(context, channelCatalog);
        if (!parsed.Succeeded)
        {
            return new(false, parsed.ErrorMessage, null, true);
        }

        var slice = parsed.Slice!;
        if (parsed.RemainingArguments.Count > 0)
        {
            var username = parsed.RemainingArguments[0];
            var found = statistics.FindViewerByName(slice, username, context.Platform);
            return found is not null
                ? new(true, null, found, false)
                : new(false, $"Пользователь {username} не найден", null, false);
        }

        if (string.IsNullOrWhiteSpace(context.UserId))
        {
            return new(true, null, null, true);
        }

        var self = statistics.FindViewerById(slice, new PlatformUserKey(context.Platform, context.UserId));
        return new(true, null, self, true);
    }

    internal readonly record struct SliceParseResult(
        bool Succeeded,
        StatisticsSlice? Slice,
        IReadOnlyList<string> RemainingArguments,
        string? ErrorMessage);

    internal readonly record struct ViewerLookupResult(
        bool Succeeded,
        string? ErrorMessage,
        ViewerAggregateStatistics? Viewer,
        bool IsSelf);
}
