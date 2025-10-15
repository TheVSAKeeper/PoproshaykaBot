using PoproshaykaBot.Core.Domain.Models.Ui;

namespace PoproshaykaBot.Core.Domain.Models.Settings;

public class AppSettings
{
    public TwitchSettings Twitch { get; set; } = new();
    public UiSettings Ui { get; set; } = new();
    public SpecialCommandsSettings SpecialCommands { get; set; } = new();
}
