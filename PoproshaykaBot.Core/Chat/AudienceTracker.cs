using System.Collections.Concurrent;
using PoproshaykaBot.Core.Platforms;
using PoproshaykaBot.Core.Settings;

namespace PoproshaykaBot.Core.Chat;

public sealed class AudienceTracker(SettingsManager settingsManager)
{
    private readonly Lock _sync = new();
    private readonly Dictionary<StreamingPlatform, ChannelAudience> _byPlatform = [];

    public PlatformAudienceTracker ForPlatform(StreamingPlatform platform, ChannelKey? channel = null)
    {
        lock (_sync)
        {
            if (_byPlatform.TryGetValue(platform, out var active))
            {
                return channel is null || channel == active.Channel
                    ? active.Tracker
                    : new PlatformAudienceTracker(settingsManager);
            }

            var tracker = new PlatformAudienceTracker(settingsManager);
            _byPlatform.Add(platform, new(channel, tracker));
            return tracker;
        }
    }

    internal PlatformAudienceTracker? ForIncomingMessage(StreamingPlatform platform, ChannelKey? channel, ChatOrigin? origin)
    {
        lock (_sync)
        {
            if (origin is { IsCurrent: false })
            {
                return null;
            }

            if (_byPlatform.TryGetValue(platform, out var active) && (channel is null || channel == active.Channel))
            {
                return active.Tracker;
            }

            var tracker = new PlatformAudienceTracker(settingsManager);
            _byPlatform[platform] = new(channel, tracker);
            return tracker;
        }
    }

    public void Clear(StreamingPlatform platform)
    {
        lock (_sync)
        {
            if (_byPlatform.Remove(platform, out var active))
            {
                active.Tracker.Clear();
            }
        }
    }

    public void ClearAll()
    {
        lock (_sync)
        {
            foreach (var active in _byPlatform.Values)
            {
                active.Tracker.Clear();
            }

            _byPlatform.Clear();
        }
    }

    private sealed record ChannelAudience(ChannelKey? Channel, PlatformAudienceTracker Tracker);
}

public sealed class PlatformAudienceTracker(SettingsManager settingsManager)
{
    private const string UsernamePlaceholder = "username";

    private readonly ConcurrentDictionary<string, string> _userIdToDisplayName = new(StringComparer.Ordinal);

    public int ActiveUserCount => _userIdToDisplayName.Count;

    public bool OnUserMessage(string userId, string displayName)
    {
        if (string.IsNullOrWhiteSpace(userId))
        {
            throw new ArgumentException("ID пользователя не может быть пустым", nameof(userId));
        }

        displayName = displayName?.Trim() ?? string.Empty;

        if (string.IsNullOrWhiteSpace(displayName))
        {
            displayName = userId;
        }

        var added = true;
        _userIdToDisplayName.AddOrUpdate(userId,
            displayName,
            (_, _) =>
            {
                added = false;
                return displayName;
            });

        return added;
    }

    public string? CreateWelcome(string displayName)
    {
        var settings = settingsManager.Current.Twitch.Messages;

        if (!settings.WelcomeEnabled)
        {
            return null;
        }

        if (string.IsNullOrWhiteSpace(settings.Welcome))
        {
            return null;
        }

        return MessageTemplate.For(settings.Welcome).With(UsernamePlaceholder, displayName).Render();
    }

    public string? CreateFarewell(string userId, string displayName)
    {
        var settings = settingsManager.Current.Twitch.Messages;

        _userIdToDisplayName.TryRemove(userId, out _);

        if (!settings.FarewellEnabled)
        {
            return null;
        }

        if (string.IsNullOrWhiteSpace(settings.Farewell))
        {
            return null;
        }

        return MessageTemplate.For(settings.Farewell).With(UsernamePlaceholder, displayName).Render();
    }

    public string? CreateCollectiveFarewell()
    {
        var settings = settingsManager.Current.Twitch.Messages;

        if (!settings.FarewellEnabled)
        {
            return null;
        }

        var names = GetActiveDisplayNames();

        if (names.Count == 0)
        {
            return null;
        }

        var list = string.Join(", ", names);
        var template = MessageTemplate.For(settings.Farewell);

        if (template.Contains("usernames"))
        {
            return template.With("usernames", list).Render();
        }

        if (template.Contains(UsernamePlaceholder))
        {
            return template.With(UsernamePlaceholder, list).Render();
        }

        return template.Render();
    }

    public void Clear()
    {
        _userIdToDisplayName.Clear();
    }

    public string BuildActiveUsersSummary(int inlineLimit = 10, int listPreviewCount = 8)
    {
        var names = GetActiveDisplayNames();
        var count = names.Count;

        if (count == 0)
        {
            return "В чате пока нет активных пользователей";
        }

        if (count <= inlineLimit)
        {
            var list = string.Join(", ", names);
            return $"👥 В чате ({count}): {list}";
        }
        else
        {
            var list = string.Join(", ", names.Take(listPreviewCount));
            return $"👥 В чате ({count}): {list} +{count - listPreviewCount}";
        }
    }

    private List<string> GetActiveDisplayNames()
    {
        return _userIdToDisplayName.Values.ToList();
    }
}
