namespace PoproshaykaBot.WinForms.Twitch.Helix;

public sealed class TwitchAuthorizationMissingException(string message) : InvalidOperationException(message);
