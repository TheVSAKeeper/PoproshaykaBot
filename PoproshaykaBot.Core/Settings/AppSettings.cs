using PoproshaykaBot.Core.Chat.Commands;
using PoproshaykaBot.Core.Users;
using PoproshaykaBot.Core.Chat;
using System.Text.Json.Serialization;

namespace PoproshaykaBot.Core.Settings;

public class AppSettings
{
    public TwitchSettings Twitch { get; set; } = new();

    [JsonIgnore]
    public MessageSettings ChatMessages => Twitch.Messages;

    [JsonIgnore]
    public int ChatHistoryMaxItems => Twitch.Infrastructure.ChatHistoryMaxItems;
    public SpecialCommandsSettings SpecialCommands { get; set; } = new();
    public RanksSettings Ranks { get; set; } = new();
}
