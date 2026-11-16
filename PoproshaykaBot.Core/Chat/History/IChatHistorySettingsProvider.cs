using PoproshaykaBot.Core.Settings.Chat;

namespace PoproshaykaBot.Core.Chat.History;

public interface IChatHistorySettingsProvider
{
    ChatSettings GetCurrent();

    event Action<ChatSettings>? Changed;
}
