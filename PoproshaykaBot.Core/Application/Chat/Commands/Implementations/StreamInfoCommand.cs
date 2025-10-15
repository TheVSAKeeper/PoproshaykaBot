using PoproshaykaBot.Core.Application.Streaming;
using PoproshaykaBot.Core.Domain.Models.Chat;
using PoproshaykaBot.Core.Domain.Models.Stream;

namespace PoproshaykaBot.Core.Application.Chat.Commands.Implementations;

public sealed class StreamInfoCommand(StreamStatusManager streamStatusManager) : IChatCommand
{
    public string Canonical => "стрим";
    public IReadOnlyCollection<string> Aliases => ["stream", "онлайн", "он"];
    public string Description => "информация о текущем стриме";

    public bool CanExecute(CommandContext context)
    {
        return true;
    }

    public OutgoingMessage Execute(CommandContext context)
    {
        var info = streamStatusManager.CurrentStream;

        if (info == null)
        {
            var text = streamStatusManager.CurrentStatus == StreamStatus.Online
                ? "Стрим онлайн, но детали временно недоступны"
                : "Сейчас стрим офлайн";

            return OutgoingMessage.Reply(text, context.MessageId);
        }

        var duration = DateTime.UtcNow - info.StartedAt;
        var hours = (int)duration.TotalHours;
        var minutes = duration.Minutes;

        var title = string.IsNullOrWhiteSpace(info.Title) ? "Без названия" : info.Title;
        var game = string.IsNullOrWhiteSpace(info.GameName) ? "Без категории" : info.GameName;

        var textFull = $"🔴 {title} | Игра: {game} | Зрителей: {info.ViewerCount} | В эфире: {hours:0}ч {minutes:00}м";
        return OutgoingMessage.Reply(textFull, context.MessageId);
    }
}
