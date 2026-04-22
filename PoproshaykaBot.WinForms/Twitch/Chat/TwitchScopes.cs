namespace PoproshaykaBot.WinForms.Twitch.Chat;

public static class TwitchScopes
{
    public const string UserReadChat = "user:read:chat";
    public const string UserWriteChat = "user:write:chat";
    public const string UserBot = "user:bot";
    public const string ChannelBot = "channel:bot";
    public const string ChannelManageBroadcast = "channel:manage:broadcast";

    public static readonly IReadOnlyList<string> Required =
    [
        UserReadChat,
        UserWriteChat,
        UserBot,
        ChannelBot,
        ChannelManageBroadcast,
    ];

    public static bool SetEquals(IEnumerable<string> left, IEnumerable<string> right)
    {
        return new HashSet<string>(left, StringComparer.Ordinal)
            .SetEquals(new HashSet<string>(right, StringComparer.Ordinal));
    }
}
