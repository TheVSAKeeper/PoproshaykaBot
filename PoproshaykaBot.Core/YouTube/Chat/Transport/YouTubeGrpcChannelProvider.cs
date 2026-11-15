using Grpc.Net.Client;
using Microsoft.Extensions.DependencyInjection;
using PoproshaykaBot.Core.YouTube.Client;

namespace PoproshaykaBot.Core.YouTube.Chat.Transport;

internal sealed class YouTubeGrpcChannelProvider(HttpMessageHandler? customHandler) : IDisposable
{
    private readonly Lock _sync = new();
    private SocketsHttpHandler? _handler;
    private GrpcChannel? _channel;
    private bool _disposed;

    [ActivatorUtilitiesConstructor]
    public YouTubeGrpcChannelProvider()
        : this(null)
    {
    }

    public GrpcChannel GetChannel()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        lock (_sync)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);

            if (_channel is not null)
            {
                return _channel;
            }

            HttpMessageHandler effectiveHandler;

            if (customHandler is not null)
            {
                effectiveHandler = customHandler;
            }
            else
            {
                _handler = new SocketsHttpHandler
                {
                    EnableMultipleHttp2Connections = true,
                    KeepAlivePingDelay = TimeSpan.FromSeconds(30),
                    KeepAlivePingTimeout = TimeSpan.FromSeconds(15),
                    KeepAlivePingPolicy = HttpKeepAlivePingPolicy.WithActiveRequests,
                };
                effectiveHandler = _handler;
            }

            _channel = GrpcChannel.ForAddress(
                YouTubeEndpoints.GrpcApiBaseUri,
                new GrpcChannelOptions { HttpHandler = effectiveHandler, DisposeHttpClient = false });

            return _channel;
        }
    }

    public void Dispose()
    {
        GrpcChannel? channel;
        SocketsHttpHandler? handler;

        lock (_sync)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            channel = _channel;
            handler = _handler;
            _channel = null;
            _handler = null;
        }

        channel?.Dispose();
        handler?.Dispose();
    }
}
