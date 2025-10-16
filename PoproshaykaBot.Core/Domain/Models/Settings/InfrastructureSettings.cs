namespace PoproshaykaBot.Core.Domain.Models.Settings;

public class InfrastructureSettings
{
    public int ChatHistoryMaxItems { get; set; } = 1000;
    public int SseKeepAliveSeconds { get; set; } = 30;
}
