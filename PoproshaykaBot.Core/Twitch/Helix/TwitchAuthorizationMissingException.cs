namespace PoproshaykaBot.Core.Twitch.Helix;

public sealed class TwitchAuthorizationMissingException(string message) : InvalidOperationException(message);
