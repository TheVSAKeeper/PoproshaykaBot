using PoproshaykaBot.Core.Domain.Models.Chat;

namespace PoproshaykaBot.Core.Application.Chat.Commands.Implementations;

public sealed class DonateCommand : IChatCommand
{
    public string Canonical => "деньги";
    public IReadOnlyCollection<string> Aliases => ["donate", "донат"];
    public string Description => "информация о донате";

    public bool CanExecute(CommandContext context)
    {
        return true;
    }

    public OutgoingMessage Execute(CommandContext context)
    {
        var text = "Принимаем криптой, СБП, куаркод справа снизу, подробнее можно узнать в телеге https://t.me/bobito217";
        return OutgoingMessage.Normal(text);
    }
}
