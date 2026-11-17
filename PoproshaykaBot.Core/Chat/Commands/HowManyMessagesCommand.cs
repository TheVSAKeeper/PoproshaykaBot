using PoproshaykaBot.Core.Settings.Stores;
using PoproshaykaBot.Core.Statistics;

namespace PoproshaykaBot.Core.Chat.Commands;

public sealed class HowManyMessagesCommand(
    IUserStatisticsRepository statistics,
    ChannelCatalogStore? channelCatalog = null) : IChatCommand
{
    public string Canonical => "сколькосообщений";
    public IReadOnlyCollection<string> Aliases => ["messages", "cc"];
    public string Description => "твой счётчик сообщений (по каналу вызова, 'группа <имя>' или 'все')";

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
        var targetDisplayName = lookup.IsSelf ? "У тебя" : $"У {viewer!.DisplayName}";
        var messageCount = (long?)viewer?.MessageCount ?? 0;

        var text = $"💬 {targetDisplayName} {FormattingUtils.FormatNumber(messageCount)} сообщений";
        return Task.FromResult<OutgoingMessage?>(OutgoingMessage.Reply(text, context.MessageId));
    }
}
