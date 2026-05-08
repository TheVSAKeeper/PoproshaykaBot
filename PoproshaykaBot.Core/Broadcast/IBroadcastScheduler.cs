namespace PoproshaykaBot.Core.Broadcast;

public interface IBroadcastScheduler
{
    bool IsActive { get; }

    void Start(string channel);

    void Stop();
}
