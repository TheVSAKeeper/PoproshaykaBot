﻿namespace PoproshaykaBot.Core;

public class InfrastructureSettings
{
    public int ChatHistoryMaxItems { get; set; } = 1000;
    public int SseKeepAliveSeconds { get; set; } = 30;
}