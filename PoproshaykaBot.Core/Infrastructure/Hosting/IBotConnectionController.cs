namespace PoproshaykaBot.Core.Infrastructure.Hosting;

public interface IBotConnectionController
{
    bool IsBusy { get; }

    void StartConnection();

    Task StopAsync(BotStopMode mode);
}
