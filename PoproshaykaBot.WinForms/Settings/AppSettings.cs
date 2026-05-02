using PoproshaykaBot.WinForms.Chat.Commands;
using PoproshaykaBot.WinForms.Settings.Ui;
using PoproshaykaBot.WinForms.Users;

namespace PoproshaykaBot.WinForms.Settings;

public class AppSettings
{
    public TwitchSettings Twitch { get; set; } = new();
    public SpecialCommandsSettings SpecialCommands { get; set; } = new();
    public RanksSettings Ranks { get; set; } = new();
}
