namespace PoproshaykaBot.Core;

public class AppSettings
{
    public TwitchSettings Twitch { get; set; } = new();
    public UiSettings Ui { get; set; } = new();
    public SpecialCommandsSettings SpecialCommands { get; set; } = new();
}
