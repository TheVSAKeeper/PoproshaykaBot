namespace PoproshaykaBot.Core.Chat.Commands;

[Flags]
public enum CommandResponseTarget
{
    None = 0,
    Chat = 1,
    Overlay = 2,

    // TODO: следующая степень двойки (8) зарезервирована под личное сообщение (whisper); добавлять,
    // когда у аккаунта бота появятся права user:manage:whispers и подтверждённый телефон
    Caller = 4,
}
