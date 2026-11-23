using PoproshaykaBot.Core.Diagnostics;
using PoproshaykaBot.Core.Infrastructure.Events.Lifecycle;
using PoproshaykaBot.Core.Platforms;
using PoproshaykaBot.Core.Streaming;

namespace PoproshaykaBot.Wpf.Mcp;

public sealed record BotStateDto(
    string AppVersion,
    BotLifecyclePhase Phase,
    bool IsBusy,
    BotStateDto.TargetChannelDto TargetChannel,
    BotStateDto.StreamStateDto Stream,
    BotStateDto.HttpServerStateDto HttpServer,
    BotStateDto.ObsStateDto Obs,
    IReadOnlyList<BotStateDto.PlatformStateDto>? Platforms = null,
    BotStateDto.BroadcastStateDto? Broadcast = null)
{
    public sealed record TargetChannelDto(
        string Login,
        string OwnChannel,
        bool IsDebugSession,
        bool IsForeign,
        bool IsSendingAllowed,
        bool IsProfileIsolated,
        bool RecordsUserData = true);

    public sealed record StreamStateDto(
        StreamStatus Status,
        string? Title,
        string? GameName,
        int? ViewerCount,
        DateTime? StartedAt);

    public sealed record HttpServerEndpointsDto(
        string Chat,
        string Events,
        string History,
        string ChatSettings);

    public sealed record HttpServerStateDto(
        bool IsRunning,
        int Port,
        string? ChatUrl,
        int? ConfiguredPort = null,
        bool PortMismatch = false,
        HttpServerEndpointsDto? Endpoints = null);

    public sealed record ObsStateDto(
        bool IsConnected,
        string? Version,
        string? LastError,
        string? ObsWebSocketVersion = null,
        bool? IsEnabled = null);

    public sealed record PlatformStateDto(
        StreamingPlatform Platform,
        string DisplayName,
        bool IsEnabled,
        bool IsConnected,
        BotLifecyclePhase Phase,
        bool IsBusy,
        string? ConfiguredChannel,
        StreamStateDto Stream,
        string? StreamUrl,
        ChatConnectionStatus? Chat = null,
        string? ActiveChannelId = null);

    public sealed record BroadcastStateDto(
        bool IsAnyLive,
        Guid? CurrentBroadcastId,
        IReadOnlyList<StreamingPlatform> LivePlatforms);
}
