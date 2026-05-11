using PoproshaykaBot.Core.Statistics;

namespace PoproshaykaBot.Core.Chat.Commands;

public sealed class HowManyMessagesCommand(IUserStatisticsRepository statistics) : IChatCommand
{
    public string Canonical => "сколькосообщений";
    public IReadOnlyCollection<string> Aliases => ["messages", "cc"];
    public string Description => "твой счётчик сообщений";

    public bool CanExecute(CommandContext context)
    {
        return true;
    }

    public Task<OutgoingMessage?> ExecuteAsync(CommandContext context, CancellationToken cancellationToken)
    {
        var targetUserId = context.UserId;
        var targetDisplayName = "У тебя";

        if (context.Arguments.Count > 0)
        {
            var username = context.Arguments[0];
            var otherUserStats = statistics.GetByName(username);
            if (otherUserStats != null)
            {
                targetUserId = otherUserStats.UserId;
                targetDisplayName = $"У {otherUserStats.Name}";
            }
            else
            {
                return Task.FromResult<OutgoingMessage?>(OutgoingMessage.Reply($"Пользователь {username} не найден", context.MessageId));
            }
        }

        var userStats = statistics.GetById(targetUserId);
        var messageCount = (long?)userStats?.MessageCount ?? 0;

        var text = $"💬 {targetDisplayName} {FormattingUtils.FormatNumber(messageCount)} сообщений";
        return Task.FromResult<OutgoingMessage?>(OutgoingMessage.Reply(text, context.MessageId));
    }
}
