using PoproshaykaBot.WinForms.Settings;
using System.Collections.Concurrent;

namespace PoproshaykaBot.WinForms.Chat;

public sealed class AudienceTracker(SettingsManager settingsManager)
{
    private readonly ConcurrentDictionary<string, string> _userIdToDisplayName = new();

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

        var added = _userIdToDisplayName.TryAdd(userId, displayName);

        if (!added)
        {
            _userIdToDisplayName[userId] = displayName;
        }

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

        return settings.Welcome.Replace("{username}", displayName);
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

        return settings.Farewell.Replace("{username}", displayName);
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
        var template = settings.Farewell;

        if (template.Contains("{usernames}"))
        {
            return template.Replace("{usernames}", list);
        }

        if (template.Contains("{username}"))
        {
            return template.Replace("{username}", list);
        }

        return template;
    }

    public void ClearAll()
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
