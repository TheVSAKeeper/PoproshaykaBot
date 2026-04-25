namespace PoproshaykaBot.WinForms.Polls;

public sealed record PollsAvailability(bool IsAvailable, string? UnavailableReason)
{
    public static readonly PollsAvailability Unknown = new(false, null);

    public static PollsAvailability Available { get; } = new(true, null);

    public static PollsAvailability NotAffiliate { get; } =
        new(false, "Голосования Twitch доступны только каналам со статусом Affiliate или Partner.");

    public static PollsAvailability NoBroadcaster { get; } =
        new(false, "Не удалось определить канал для голосования.");
}
