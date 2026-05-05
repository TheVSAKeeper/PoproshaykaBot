using Microsoft.AspNetCore.Http;
using System.Threading.Channels;

namespace PoproshaykaBot.Core.Server;

public sealed class SseClientPipeline
{
    public SseClientPipeline(HttpResponse response, int capacity)
    {
        Response = response;
        Channel = System.Threading.Channels.Channel.CreateBounded<byte[]>(new BoundedChannelOptions(capacity)
        {
            FullMode = BoundedChannelFullMode.DropOldest,
            SingleReader = true,
            SingleWriter = false,
        });
    }

    public HttpResponse Response { get; }

    public Channel<byte[]> Channel { get; }

    public Task? WriterTask { get; set; }
}
