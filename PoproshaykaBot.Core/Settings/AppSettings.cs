using PoproshaykaBot.Core.Chat.Commands;
using PoproshaykaBot.Core.Users;

namespace PoproshaykaBot.Core.Settings;

public class AppSettings
{
    public TwitchSettings Twitch { get; set; } = new();
    public SpecialCommandsSettings SpecialCommands { get; set; } = new();
    public RanksSettings Ranks { get; set; } = new();
}
