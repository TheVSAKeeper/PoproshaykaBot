using PoproshaykaBot.Core.Settings.Stores;
using PoproshaykaBot.Core.Statistics;
using PoproshaykaBot.Core.Users;

namespace PoproshaykaBot.Core.Chat.Commands;

public sealed class MyProfileCommand(
    IUserStatisticsRepository statistics,
    UserRankService rankService,
    ChannelCatalogStore? channelCatalog = null) : IChatCommand
{
    public string Canonical => "мойпрофиль";
    public IReadOnlyCollection<string> Aliases => ["profile", "профиль"];
    public string Description => "твоя статистика (по каналу вызова, 'группа <имя>' или 'все')";

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
        if (viewer is null)
        {
            var msg = lookup.IsSelf ? "У тебя пока нет статистики" : "У этого пользователя пока нет статистики";
            return Task.FromResult<OutgoingMessage?>(OutgoingMessage.Reply(msg, context.MessageId));
        }

        var targetDisplayName = lookup.IsSelf ? "твоя" : $"статистика {viewer.DisplayName}";
        var messages = FormattingUtils.FormatNumber((long)viewer.MessageCount);
        var points = rankService.FormatPoints(viewer.Points);
        var firstSeen = FormattingUtils.FormatDateTime(viewer.FirstSeen);
        var lastSeen = FormattingUtils.FormatDateTime(viewer.LastSeen);
        var rankDisplay = rankService.GetRankDisplay(viewer.Points);

        var text = $"👤 {targetDisplayName} {rankDisplay} | 💬 {messages} мсг | 🏆 {points} | С нами с: {firstSeen} | В чате: {lastSeen}";
        return Task.FromResult<OutgoingMessage?>(OutgoingMessage.Reply(text, context.MessageId));
    }
}
