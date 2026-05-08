namespace PoproshaykaBot.Core.Infrastructure.Events.Lifecycle;

public sealed record BotLifecyclePhaseChanged(BotLifecyclePhase Phase, Exception? Exception = null) : EventBase;
