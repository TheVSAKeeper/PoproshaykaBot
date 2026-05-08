namespace PoproshaykaBot.Core.Streaming;

public sealed class BotLifecycleAutomationSettings
{
    public bool AutoConnectOnStreamOnline { get; set; } = false;

    public bool AutoDisconnectOnStreamOffline { get; set; } = false;
}
