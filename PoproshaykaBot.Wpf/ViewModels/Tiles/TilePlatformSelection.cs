using PoproshaykaBot.Core.Platforms;
using PoproshaykaBot.Core.Streaming;
using PoproshaykaBot.Wpf.Infrastructure.Platforms;

namespace PoproshaykaBot.Wpf.ViewModels.Tiles;

public sealed class TilePlatformSelection
{
    private readonly IChatPlatformRegistry _platformRegistry;
    private bool _isUserPinned;

    public TilePlatformSelection(IChatPlatformRegistry platformRegistry)
    {
        ArgumentNullException.ThrowIfNull(platformRegistry);

        _platformRegistry = platformRegistry;
        Refresh();
    }

    public IReadOnlyList<StreamingPlatform> AvailablePlatforms { get; private set; } = [StreamingPlatform.Twitch];

    public StreamingPlatform SelectedPlatform { get; private set; } = StreamingPlatform.Twitch;

    public bool HasMultiplePlatforms => AvailablePlatforms.Count > 1;

    public string SelectedDisplayName => GetSelectedPlatform()?.DisplayName ?? PlatformVisuals.GetDisplayName(SelectedPlatform);

    public IChatPlatform? GetSelectedPlatform() => _platformRegistry.Find(SelectedPlatform);

    public bool Refresh()
    {
        var previousSelected = SelectedPlatform;
        var platforms = _platformRegistry.All;
        var available = new List<StreamingPlatform>(platforms.Count);

        foreach (var platform in platforms)
        {
            if (platform.IsEnabled || platform.StreamStatus.CurrentStatus == StreamStatus.Online)
            {
                available.Add(platform.Id);
            }
        }

        if (available.Count == 0)
        {
            var first = platforms.Count > 0 ? platforms[0].Id : StreamingPlatform.Twitch;
            available.Add(first);
        }

        AvailablePlatforms = available;

        var selectedAvailable = AvailablePlatforms.Contains(SelectedPlatform);
        if (!selectedAvailable)
        {
            _isUserPinned = false;
            SelectedPlatform = FindFirstOnline(available) ?? available[0];
        }
        else if (!_isUserPinned && !IsOnline(SelectedPlatform) && FindFirstOnline(available) is { } livePlatform)
        {
            SelectedPlatform = livePlatform;
        }

        return SelectedPlatform != previousSelected;
    }

    public bool Cycle()
    {
        if (AvailablePlatforms.Count <= 1)
        {
            return false;
        }

        var currentIndex = -1;
        for (var i = 0; i < AvailablePlatforms.Count; i++)
        {
            if (AvailablePlatforms[i] == SelectedPlatform)
            {
                currentIndex = i;
                break;
            }
        }

        var nextIndex = (currentIndex + 1) % AvailablePlatforms.Count;
        var next = AvailablePlatforms[nextIndex];
        _isUserPinned = true;

        if (next == SelectedPlatform)
        {
            return false;
        }

        SelectedPlatform = next;
        return true;
    }

    public bool Select(StreamingPlatform platform)
    {
        if (!AvailablePlatforms.Contains(platform))
        {
            return false;
        }

        _isUserPinned = true;
        if (SelectedPlatform == platform)
        {
            return false;
        }

        SelectedPlatform = platform;
        return true;
    }

    private StreamingPlatform? FindFirstOnline(IReadOnlyList<StreamingPlatform> candidates)
    {
        foreach (var platformId in candidates)
        {
            if (IsOnline(platformId))
            {
                return platformId;
            }
        }

        return null;
    }

    private bool IsOnline(StreamingPlatform platformId)
    {
        return _platformRegistry.TryGet(platformId, out var platform)
            && platform.StreamStatus.CurrentStatus == StreamStatus.Online;
    }
}
