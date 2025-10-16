﻿namespace PoproshaykaBot.Core.Domain.Models.Settings;

public class TwitchSettings
{
    public string BotUsername { get; set; } = "thevsakeeper";

    public string Channel { get; set; } = "bobito217";

    public int MessagesAllowedInPeriod { get; set; } = 750;

    public int ThrottlingPeriodSeconds { get; set; } = 30;

    public string ClientId { get; set; } = string.Empty;

    public string ClientSecret { get; set; } = string.Empty;

    public string AccessToken { get; set; } = string.Empty;

    public string RefreshToken { get; set; } = string.Empty;

    public string RedirectUri { get; set; } = "http://localhost:8080";

    public string[] Scopes { get; set; } = ["chat:read", "chat:edit"];

    public int HttpServerPort { get; set; } = 8080;

    public bool HttpServerEnabled { get; set; } = true;

    public bool ObsOverlayEnabled { get; set; } = true;

    public MessageSettings Messages { get; set; } = new();

    public ObsChatSettings ObsChat { get; set; } = new();

    public AutoBroadcastSettings AutoBroadcast { get; set; } = new();

    public InfrastructureSettings Infrastructure { get; set; } = new();
}