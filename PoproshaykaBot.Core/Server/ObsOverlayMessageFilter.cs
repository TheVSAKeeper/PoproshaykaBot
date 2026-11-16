using PoproshaykaBot.Core.Chat;
using PoproshaykaBot.Core.Chat.Commands;
using PoproshaykaBot.Core.Settings.Chat;

namespace PoproshaykaBot.Core.Server;

internal static class ObsOverlayMessageFilter
{
    public static bool Allows(ChatMessageData entry, ChatSettings chatSettings)
    {
        ArgumentNullException.ThrowIfNull(entry);
        ArgumentNullException.ThrowIfNull(chatSettings);

        if (entry.CommandResponse is { } mark && !mark.Target.GoesToOverlay())
        {
            return false;
        }

        return !entry.IsHistorical || chatSettings.ShowHistoryInObs;
    }
}
