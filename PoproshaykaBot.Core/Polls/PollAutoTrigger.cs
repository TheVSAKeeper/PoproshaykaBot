namespace PoproshaykaBot.Core.Polls;

public sealed class PollAutoTrigger
{
    public PollAutoTriggerEvent Event { get; set; } = PollAutoTriggerEvent.None;

    public Guid? BroadcastProfileId { get; set; }

    public int CooldownMinutes { get; set; }
}
