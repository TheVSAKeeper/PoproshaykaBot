using System.Collections.Frozen;
using System.Diagnostics.CodeAnalysis;

namespace PoproshaykaBot.Core.Platforms;

public sealed class ChatPlatformRegistry : IChatPlatformRegistry
{
    private readonly FrozenDictionary<StreamingPlatform, IChatPlatform> _byId;

    public ChatPlatformRegistry(IEnumerable<IChatPlatform> platforms)
    {
        ArgumentNullException.ThrowIfNull(platforms);

        var ordered = platforms.OrderBy(p => p.Id).ToArray();
        var map = new Dictionary<StreamingPlatform, IChatPlatform>(ordered.Length);

        foreach (var platform in ordered)
        {
            if (!map.TryAdd(platform.Id, platform))
            {
                throw new ArgumentException($"Дубликат регистрации площадки: {platform.Id}", nameof(platforms));
            }
        }

        All = ordered;
        _byId = map.ToFrozenDictionary();
    }

    public IReadOnlyList<IChatPlatform> All { get; }

    public bool TryGet(StreamingPlatform platform, [NotNullWhen(true)] out IChatPlatform? chatPlatform)
    {
        return _byId.TryGetValue(platform, out chatPlatform);
    }

    public IChatPlatform? Find(StreamingPlatform platform)
    {
        return _byId.GetValueOrDefault(platform);
    }

    public IChatPlatform GetRequired(StreamingPlatform platform)
    {
        if (_byId.TryGetValue(platform, out var chatPlatform))
        {
            return chatPlatform;
        }

        throw new KeyNotFoundException($"Площадка {platform} не зарегистрирована");
    }

    public IReadOnlyList<IChatPlatform> GetEnabled()
    {
        return All.Where(p => p.IsEnabled).ToArray();
    }

    public IReadOnlyList<IChatPlatform> GetConnected()
    {
        return All.Where(p => p.IsConnected).ToArray();
    }
}
