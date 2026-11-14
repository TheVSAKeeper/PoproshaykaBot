using PoproshaykaBot.Core.Infrastructure.Events.Lifecycle;
using PoproshaykaBot.Core.Platforms;

namespace PoproshaykaBot.Core.Infrastructure.Hosting;

public interface IBotConnectionController
{
    bool IsBusy { get; }

    BotLifecyclePhase CurrentPhase { get; }

    bool IsPlatformBusy(StreamingPlatform platform);

    BotLifecyclePhase GetPhase(StreamingPlatform platform);

    void StartConnection();

    void StartConnection(StreamingPlatform platform);

    void CancelConnection();

    void CancelConnection(StreamingPlatform platform);

    Task WaitForConnectionAsync();

    Task WaitForConnectionAsync(StreamingPlatform platform);

    Task StopAsync(BotStopMode mode = BotStopMode.Graceful);

    Task StopAsync(StreamingPlatform platform, BotStopMode mode = BotStopMode.Graceful);
}
