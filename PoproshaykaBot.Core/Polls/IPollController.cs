namespace PoproshaykaBot.Core.Polls;

public interface IPollController
{
    Task<PollSnapshot?> StartAsync(PollProfile profile, CancellationToken cancellationToken);

    Task<bool> EndAsync(bool showResult, CancellationToken cancellationToken);
}
