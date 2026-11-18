using PoproshaykaBot.Core.Platforms;

namespace PoproshaykaBot.Core.Infrastructure.Hosting;

public interface IPlatformConnectionAdapter
{
    StreamingPlatform Platform { get; }

    bool IsEnabled { get; }

    PlatformHost Host { get; }

    Task<string> BeginSessionAsync(IProgress<string> progress, CancellationToken cancellationToken);

    void EndSession();

    Task<string> RetargetSessionAsync(
        PlatformChatSessionTarget target,
        IProgress<string> progress,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(target);
        return BeginSessionAsync(progress, cancellationToken);
    }
}
