using PoproshaykaBot.WinForms.Models;
using TwitchLib.Api;
using TwitchLib.Api.Helix.Models.Chat.Badges;
using TwitchLib.Api.Helix.Models.Chat.Emotes;
using TwitchLib.Client.Models;

namespace PoproshaykaBot.WinForms.Chat;

/// <summary>
/// Сервис оформления чата: загрузка и кеширование глобальных эмодзи/бэйджей,
/// построение ссылок на изображения и извлечение данных из сообщений.
/// </summary>
public sealed class ChatDecorationsProvider(TwitchAPI twitchApi)
{
    private readonly TwitchAPI _twitchApi = twitchApi ?? throw new ArgumentNullException(nameof(twitchApi));
    private Dictionary<string, GlobalEmote>? _globalEmotes;
    private Dictionary<string, BadgeEmoteSet>? _globalBadges;
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

        var emotesResponse = await _twitchApi.Helix.Chat.GetGlobalEmotesAsync();
        var badgesResponse = await _twitchApi.Helix.Chat.GetGlobalChatBadgesAsync();

        var localEmotes = new Dictionary<string, GlobalEmote>();
        var localBadges = new Dictionary<string, BadgeEmoteSet>();

        if (emotesResponse?.GlobalEmotes != null)
        {
            foreach (var emote in emotesResponse.GlobalEmotes)
            {
                localEmotes[emote.Id] = emote;
            }
        }

        if (badgesResponse?.EmoteSet != null)
        {
            foreach (var badgeSet in badgesResponse.EmoteSet)
            {
                localBadges[badgeSet.SetId] = badgeSet;
            }
        }

        _globalEmotes = localEmotes;
        _globalBadges = localBadges;
        _isLoaded = true;
    }

    /// <summary>
    /// Извлекает список эмодзи из сообщения Twitch, подставляя корректные ссылки на изображения.
    /// </summary>
    /// <param name="chatMessage">Сообщение Twitch</param>
    /// <param name="emoteSizePixels">Желаемый размер эмодзи (в пикселях)</param>
    public List<EmoteInfo> ExtractEmotes(ChatMessage chatMessage, int emoteSizePixels)
    {
        ArgumentNullException.ThrowIfNull(chatMessage);

        if (chatMessage.EmoteSet?.Emotes == null || _isLoaded == false)
        {
            return [];
        }

        var emotes = new List<EmoteInfo>();

        foreach (var emote in chatMessage.EmoteSet.Emotes)
        {
            string imageUrl;

            var globalEmote = GetGlobalEmote(emote.Id);

            if (globalEmote != null)
            {
                imageUrl = GetEmoteImageUrl(globalEmote, emoteSizePixels);
            }
            else
            {
                imageUrl = emote.ImageUrl;
            }

            emotes.Add(new()
            {
                Id = emote.Id,
                Name = emote.Name,
                ImageUrl = imageUrl,
                StartIndex = emote.StartIndex,
                EndIndex = emote.EndIndex,
            });
        }

        return emotes;
    }

    /// <summary>
    /// Извлекает ссылки на изображения бэйджей по их типу и версии.
    /// </summary>
    /// <param name="badges">Список пар (тип, версия) бэйджей из сообщения</param>
    /// <param name="badgeSizePixels">Желаемый размер бэйджа (в пикселях)</param>
    public Dictionary<string, string> ExtractBadgeUrls(List<KeyValuePair<string, string>> badges, int badgeSizePixels)
    {
        var badgeUrls = new Dictionary<string, string>();

        if (_isLoaded == false)
        {
            return badgeUrls;
        }

        foreach (var badge in badges)
        {
            var badgeVersion = GetBadgeVersion(badge.Key, badge.Value);

            if (badgeVersion == null)
            {
                continue;
            }

            var imageUrl = GetBadgeImageUrl(badgeVersion, badgeSizePixels);
            var key = $"{badge.Key}/{badge.Value}";
            badgeUrls[key] = imageUrl;
        }

        return badgeUrls;
    }

    private static string GetEmoteImageUrl(GlobalEmote emote, int sizePixels)
    {
        if (sizePixels <= 32)
        {
            return emote.Images.Url1X;
        }

        if (sizePixels <= 64)
        {
            return emote.Images.Url2X;
        }

        return emote.Images.Url4X;
    }

    private static string GetBadgeImageUrl(BadgeVersion badge, int sizePixels)
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

    private GlobalEmote? GetGlobalEmote(string emoteId)
    {
        var emotesMap = _globalEmotes;
        return emotesMap != null && emotesMap.TryGetValue(emoteId, out var emote) ? emote : null;
    }

    private BadgeVersion? GetBadgeVersion(string badgeType, string version)
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
