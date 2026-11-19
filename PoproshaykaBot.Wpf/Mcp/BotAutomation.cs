using Microsoft.Extensions.Logging;
using PoproshaykaBot.Core.Broadcast;
using PoproshaykaBot.Core.Debugging;
using PoproshaykaBot.Core.Infrastructure.Events.Lifecycle;
using PoproshaykaBot.Core.Infrastructure.Hosting;
using PoproshaykaBot.Core.Obs;
using PoproshaykaBot.Core.Platforms;
using PoproshaykaBot.Core.Server;
using PoproshaykaBot.Core.Settings;
using PoproshaykaBot.Core.Streaming;
using PoproshaykaBot.Wpf.Bootstrap;

namespace PoproshaykaBot.Wpf.Mcp;

public sealed class BotAutomation(
    IBotConnectionController connectionManager,
    ITargetChannelProvider targetChannelProvider,
    IStreamStatus streamStatus,
    IHttpServerStatus httpServer,
    IObsSceneController obsIntegration,
    SettingsManager settingsManager,
    ILogger<BotAutomation> logger,
    IChatPlatformRegistry? platformRegistry = null,
    IBroadcastState? broadcastState = null)
{
    public BotStateDto GetState()
    {
        var target = targetChannelProvider.Current;
        var stream = streamStatus.CurrentStream;
        var obs = obsIntegration.CurrentStatus;
        var port = httpServer.Port ?? settingsManager.Current.Twitch.HttpServerPort;
        var platforms = BuildPlatformStates();
        var broadcast = broadcastState is null
            ? null
            : new BotStateDto.BroadcastStateDto(
                broadcastState.IsAnyLive,
                broadcastState.CurrentBroadcastId,
                [.. broadcastState.LivePlatforms.Order()]);

        return new BotStateDto(
            AppInfo.Version,
            connectionManager.CurrentPhase,
            connectionManager.IsBusy,
            new BotStateDto.TargetChannelDto(
                target.Login,
                target.OwnChannel,
                target.IsDebugSession,
                target.IsForeign,
                target.IsSendingAllowed,
                target.IsProfileIsolated),
            ToStreamDto(streamStatus.CurrentStatus, stream),
            new BotStateDto.HttpServerStateDto(httpServer.IsRunning, port, httpServer.IsRunning ? $"http://localhost:{port}/chat" : null),
            new BotStateDto.ObsStateDto(obs.IsConnected, obs.ObsVersion, obs.ErrorMessage),
            platforms,
            broadcast);
    }

    public BotLifecyclePhase Connect(StreamingPlatform? platform = null)
    {
        if (platform is { } target)
        {
            return ConnectPlatform(target);
        }

        var phase = connectionManager.CurrentPhase;

        if (phase is BotLifecyclePhase.Connecting or BotLifecyclePhase.Connected)
        {
            throw new InvalidOperationException($"Бот уже в фазе {phase} – подключать нечего.");
        }

        if (phase == BotLifecyclePhase.Disconnecting)
        {
            throw new InvalidOperationException("Бот сейчас отключается – дождитесь фазы Disconnected и повторите.");
        }

        logger.McpBotConnectRequested();

        try
        {
            connectionManager.StartConnection();
        }
        catch (InvalidOperationException exception)
        {
            throw new InvalidOperationException("Подключение уже запущено кем-то другим – проверьте фазу через get_bot_state.", exception);
        }

        return connectionManager.CurrentPhase;
    }

    public async Task<BotLifecyclePhase> DisconnectAsync(StreamingPlatform? platform = null)
    {
        if (platform is { } target)
        {
            return await DisconnectPlatformAsync(target);
        }

        var phase = connectionManager.CurrentPhase;

        if (phase == BotLifecyclePhase.Connecting)
        {
            logger.McpBotDisconnectRequested();
            connectionManager.CancelConnection();
            await connectionManager.WaitForConnectionAsync();

            phase = connectionManager.CurrentPhase;

            if (phase != BotLifecyclePhase.Connected)
            {
                return phase;
            }
        }
        else if (phase == BotLifecyclePhase.Connected)
        {
            logger.McpBotDisconnectRequested();
        }
        else
        {
            throw new InvalidOperationException($"Бот в фазе {phase} – отключать нечего.");
        }

        await connectionManager.StopAsync(BotStopMode.Graceful);

        return connectionManager.CurrentPhase;
    }

    private BotLifecyclePhase ConnectPlatform(StreamingPlatform platform)
    {
        var phase = connectionManager.GetPhase(platform);

        if (phase is BotLifecyclePhase.Connecting or BotLifecyclePhase.Connected)
        {
            throw new InvalidOperationException($"Площадка {platform} уже в фазе {phase} – подключать нечего.");
        }

        if (phase == BotLifecyclePhase.Disconnecting)
        {
            throw new InvalidOperationException($"Площадка {platform} сейчас отключается – дождитесь фазы Disconnected и повторите.");
        }

        logger.McpBotConnectRequested();

        try
        {
            connectionManager.StartConnection(platform);
        }
        catch (InvalidOperationException exception)
        {
            throw new InvalidOperationException("Подключение уже запущено кем-то другим – проверьте фазу через get_bot_state.", exception);
        }

        return connectionManager.GetPhase(platform);
    }

    private async Task<BotLifecyclePhase> DisconnectPlatformAsync(StreamingPlatform platform)
    {
        var phase = connectionManager.GetPhase(platform);

        if (phase == BotLifecyclePhase.Connecting)
        {
            logger.McpBotDisconnectRequested();
            connectionManager.CancelConnection(platform);
            await connectionManager.WaitForConnectionAsync(platform);

            phase = connectionManager.GetPhase(platform);

            if (phase != BotLifecyclePhase.Connected)
            {
                return phase;
            }
        }
        else if (phase == BotLifecyclePhase.Connected)
        {
            logger.McpBotDisconnectRequested();
        }
        else
        {
            throw new InvalidOperationException($"Площадка {platform} в фазе {phase} – отключать нечего.");
        }

        await connectionManager.StopAsync(platform, BotStopMode.Graceful);

        return connectionManager.GetPhase(platform);
    }

    private IReadOnlyList<BotStateDto.PlatformStateDto>? BuildPlatformStates()
    {
        if (platformRegistry is null)
        {
            return null;
        }

        var list = new List<BotStateDto.PlatformStateDto>(platformRegistry.All.Count);
        foreach (var platform in platformRegistry.All)
        {
            var status = platform.StreamStatus;
            var currentStream = status.CurrentStream;
            var phase = connectionManager.GetPhase(platform.Id);
            list.Add(new BotStateDto.PlatformStateDto(
                platform.Id,
                platform.DisplayName,
                platform.IsEnabled,
                platform.IsConnected,
                phase,
                connectionManager.IsPlatformBusy(platform.Id),
                platform.ConfiguredChannel,
                ToStreamDto(status.CurrentStatus, currentStream),
                platform.BuildStreamUrl(currentStream),
                platform.CaptureChatStatus(phase)));
        }

        return list;
    }

    private static BotStateDto.StreamStateDto ToStreamDto(StreamStatus status, StreamInfo? stream)
    {
        return new BotStateDto.StreamStateDto(
            status,
            stream?.Title,
            stream?.GameName,
            stream?.ViewerCount,
            status == StreamStatus.Online ? stream?.StartedAt : null);
    }
}
