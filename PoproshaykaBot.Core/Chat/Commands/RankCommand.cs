using PoproshaykaBot.Core.Settings.Stores;
using PoproshaykaBot.Core.Statistics;
using PoproshaykaBot.Core.Users;

namespace PoproshaykaBot.Core.Chat.Commands;

public sealed class RankCommand(
    IUserStatisticsRepository statistics,
    UserRankService rankService,
    ChannelCatalogStore? channelCatalog = null) : IChatCommand
{
    public string Canonical => "ранг";
    public IReadOnlyCollection<string> Aliases => ["rank"];
    public string Description => "посмотреть ранг (по каналу вызова, 'группа <имя>' или 'все')";

    public bool CanExecute(CommandContext context)
    {
        return true;
    }

    public Task<OutgoingMessage?> ExecuteAsync(CommandContext context, CancellationToken cancellationToken)
    {
        var lookup = StatisticsCommandSliceParser.ResolveViewer(context, statistics, channelCatalog);
        if (!lookup.Succeeded)
        {
            return Task.FromResult<OutgoingMessage?>(OutgoingMessage.Reply(lookup.ErrorMessage!, context.MessageId));
        }

        var viewer = lookup.Viewer;
        var targetDisplayName = lookup.IsSelf ? "Твой" : $"Ранг {viewer!.DisplayName}";
        var points = viewer?.Points ?? 0;
        var messages = (long?)viewer?.MessageCount ?? 0;
        var rankDisplay = rankService.GetRankDisplay(points);

        var text = $"{targetDisplayName}: {rankDisplay} ({rankService.FormatPoints(points)} / {FormattingUtils.FormatNumber(messages)} сообщ.)";
        return Task.FromResult<OutgoingMessage?>(OutgoingMessage.Reply(text, context.MessageId));
    }
}
