using Microsoft.AspNetCore.Http;
using System.Diagnostics.CodeAnalysis;

namespace PoproshaykaBot.Core.Server;

public sealed class SseClientRegistry
{
    private readonly Dictionary<HttpResponse, SseClientPipeline> _clients = new();
    private readonly object _lock = new();

    public int Count
    {
        get
        {
            lock (_lock)
            {
                return _clients.Count;
            }
        }
    }

    public int Add(HttpResponse response, SseClientPipeline pipeline)
    {
        lock (_lock)
        {
            _clients[response] = pipeline;
            return _clients.Count;
        }
    }

    public bool TryRemove(HttpResponse response, [NotNullWhen(true)] out SseClientPipeline? pipeline)
    {
        lock (_lock)
        {
            return _clients.Remove(response, out pipeline);
        }
    }

    public IReadOnlyList<SseClientPipeline> Snapshot()
    {
        lock (_lock)
        {
            if (_clients.Count == 0)
            {
                return Array.Empty<SseClientPipeline>();
            }

            return _clients.Values.ToList();
        }
    }

    public IReadOnlyList<SseClientPipeline> DrainAll()
    {
        lock (_lock)
        {
            if (_clients.Count == 0)
            {
                return [];
            }

            var list = _clients.Values.ToList();
            _clients.Clear();
            return list;
        }
    }
}
