using Microsoft.AspNetCore.Http;
using System.Threading.Channels;

namespace PoproshaykaBot.Core.Server;

public sealed class SseClientPipeline
{
    private static int _lastClientId;

    public SseClientPipeline(HttpResponse response, int capacity, bool supportsReloadOnClear = false)
    {
        ClientId = Interlocked.Increment(ref _lastClientId);
        Response = response;
        SupportsReloadOnClear = supportsReloadOnClear;
        Channel = System.Threading.Channels.Channel.CreateBounded<byte[]>(new BoundedChannelOptions(capacity)
        {
            FullMode = BoundedChannelFullMode.DropOldest,
            SingleReader = true,
            SingleWriter = false,
        });
    }

    public int ClientId { get; }

    public HttpResponse Response { get; }

    public bool SupportsReloadOnClear { get; }

    public Channel<byte[]> Channel { get; }

    public Task? WriterTask { get; set; }
}
