using PoproshaykaBot.WinForms.Twitch.Helix;

namespace PoproshaykaBot.WinForms.Chat;

/// <summary>
/// Сервис оформления чата: загрузка и кеширование глобальных эмодзи/бэйджей,
/// построение ссылок на изображения и извлечение данных из сообщений.
/// </summary>
public sealed class ChatDecorationsProvider(ITwitchHelixClient helix)
{
    private readonly SemaphoreSlim _loadLock = new(1, 1);

    private IReadOnlyDictionary<string, GlobalEmoteInfo>? _globalEmotes;
    private IReadOnlyDictionary<string, GlobalBadgeInfo>? _globalBadges;
    private volatile bool _isLoaded;

    public int GlobalEmotesCount => _globalEmotes?.Count ?? 0;
    public int GlobalBadgeSetsCount => _globalBadges?.Count ?? 0;

    /// <summary>
    /// Загружает глобальные эмодзи и бэйджи из Twitch API.
    /// </summary>
    public async Task LoadAsync()
    {
        if (_isLoaded)
        {
            return;
        }

        await _loadLock.WaitAsync();
        try
        {
            if (_isLoaded)
            {
                return;
            }

            var emotes = await helix.GetGlobalChatEmotesAsync();
            var badges = await helix.GetGlobalChatBadgesAsync();

            _globalEmotes = emotes;
            _globalBadges = badges;
            _isLoaded = true;
        }
        finally
        {
            _loadLock.Release();
        }
    }

    /// <summary>
    /// Извлекает список эмодзи из сообщения Twitch, подставляя корректные ссылки на изображения.
    /// </summary>
    /// <param name="chatMessage">Сообщение Twitch</param>
    /// <param name="emoteSizePixels">Желаемый размер эмодзи (в пикселях)</param>
    public List<EmoteInfo> ExtractEmotes(ChatMessage chatMessage, int emoteSizePixels)
    {
        ArgumentNullException.ThrowIfNull(chatMessage);

        if (!_isLoaded || chatMessage.Emotes.Count == 0)
        {
            return [];
        }

        var emotes = new List<EmoteInfo>();

        foreach (var emoteOccurrence in chatMessage.Emotes)
        {
            string imageUrl;

            var globalEmote = GetGlobalEmote(emoteOccurrence.EmoteId);

            if (globalEmote != null)
            {
                imageUrl = GetEmoteImageUrl(globalEmote, emoteSizePixels);
            }
            else
            {
                imageUrl = $"https://static-cdn.jtvnw.net/emoticons/v2/{emoteOccurrence.EmoteId}/default/light/1.0";
            }

            emotes.Add(new()
            {
                Id = emoteOccurrence.EmoteId,
                Name = emoteOccurrence.Name,
                ImageUrl = imageUrl,
                StartIndex = emoteOccurrence.StartIndex,
                EndIndex = emoteOccurrence.EndIndex,
            });
        }

        return emotes;
    }

    /// <summary>
    /// Извлекает ссылки на изображения бэйджей по их типу и версии.
    /// </summary>
    /// <param name="badges">Список пар (тип, версия) бэйджей из сообщения</param>
    /// <param name="badgeSizePixels">Желаемый размер бэйджа (в пикселях)</param>
    public Dictionary<string, string> ExtractBadgeUrls(IReadOnlyList<(string SetId, string BadgeId)> badges, int badgeSizePixels)
    {
        var badgeUrls = new Dictionary<string, string>();

        if (!_isLoaded)
        {
            return badgeUrls;
        }

        foreach (var (setId, badgeId) in badges)
        {
            var badgeVersion = GetBadgeVersion(setId, badgeId);

            if (badgeVersion == null)
            {
                continue;
            }

            var imageUrl = GetBadgeImageUrl(badgeVersion, badgeSizePixels);
            var key = $"{setId}/{badgeId}";
            badgeUrls[key] = imageUrl;
        }

        return badgeUrls;
    }

    private static string GetEmoteImageUrl(GlobalEmoteInfo emote, int sizePixels)
    {
        if (sizePixels <= 32)
        {
            return emote.Images.Url1x;
        }

        if (sizePixels <= 64)
        {
            return emote.Images.Url2x;
        }

        return emote.Images.Url4x;
    }

    private static string GetBadgeImageUrl(GlobalBadgeVersion badge, int sizePixels)
    {
        if (sizePixels <= 24)
        {
            return badge.ImageUrl1x;
        }

        if (sizePixels <= 48)
        {
            return badge.ImageUrl2x;
        }

        return badge.ImageUrl4x;
    }

    private GlobalEmoteInfo? GetGlobalEmote(string emoteId)
    {
        var emotesMap = _globalEmotes;
        return emotesMap != null && emotesMap.TryGetValue(emoteId, out var emote) ? emote : null;
    }

    private GlobalBadgeVersion? GetBadgeVersion(string badgeType, string version)
    {
        var badgesMap = _globalBadges;

        if (badgesMap == null)
        {
            return null;
        }

        return badgesMap.TryGetValue(badgeType, out var badgeSet)
            ? badgeSet.Versions.FirstOrDefault(badgeVersion => badgeVersion.Id == version)
            : null;
    }
}
