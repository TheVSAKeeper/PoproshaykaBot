using Microsoft.Extensions.Logging;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace PoproshaykaBot.WinForms.Twitch.Helix;

public sealed class TwitchHelixClient(IHttpClientFactory httpClientFactory, ILogger<TwitchHelixClient> logger) : ITwitchHelixClient
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task<UserInfo?> GetUserByLoginAsync(string login, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(login))
        {
            return null;
        }

        var dto = await GetSingleAsync<HelixUserDto>($"{TwitchEndpoints.HelixUsers}?login={Uri.EscapeDataString(login)}",
            cancellationToken);

        return dto is null ? null : MapUser(dto);
    }

    public async Task<IReadOnlyList<UserInfo>> GetUsersByIdsAsync(IEnumerable<string> ids, CancellationToken cancellationToken = default)
    {
        var idList = ids.Where(id => !string.IsNullOrWhiteSpace(id)).Distinct().Take(100).ToArray();
        if (idList.Length == 0)
        {
            return [];
        }

        var query = string.Join('&', idList.Select(id => $"id={Uri.EscapeDataString(id)}"));
        var data = await GetCollectionAsync<HelixUserDto>($"{TwitchEndpoints.HelixUsers}?{query}", cancellationToken);
        return data.Select(MapUser).ToArray();
    }

    public async Task<HelixStreamInfo?> GetStreamAsync(string broadcasterId, CancellationToken cancellationToken = default)
    {
        var dto = await GetSingleAsync<HelixStreamDto>($"{TwitchEndpoints.HelixStreams}?user_id={Uri.EscapeDataString(broadcasterId)}",
            cancellationToken);

        return dto is null ? null : MapStream(dto);
    }

    public async Task<ChannelInfo?> GetChannelInfoAsync(string broadcasterId, CancellationToken cancellationToken = default)
    {
        var dto = await GetSingleAsync<HelixChannelDto>($"{TwitchEndpoints.HelixChannels}?broadcaster_id={Uri.EscapeDataString(broadcasterId)}",
            cancellationToken);

        return dto is null ? null : MapChannel(dto);
    }

    public async Task PatchChannelAsync(string broadcasterId, PatchChannelRequest request, CancellationToken cancellationToken = default)
    {
        var ccl = request.ContentClassificationLabels?
            .Select(x => new HelixCclDto(x.Id, x.IsEnabled))
            .ToArray();

        var body = new HelixPatchChannelDto(request.Title,
            request.GameId,
            request.BroadcasterLanguage,
            request.Tags,
            ccl,
            request.IsBrandedContent);

        using var client = httpClientFactory.CreateClient(TwitchEndpoints.HelixHttpClientName);
        using var httpRequest = new HttpRequestMessage(HttpMethod.Patch,
            $"{TwitchEndpoints.HelixChannels}?broadcaster_id={Uri.EscapeDataString(broadcasterId)}")
        {
            Content = JsonContent.Create(body, options: JsonOptions),
        };

        using var response = await client.SendAsync(httpRequest, cancellationToken);
        await EnsureSuccessAsync(httpRequest, response, cancellationToken);
    }

    public async Task<IReadOnlyList<GameInfo>> SearchCategoriesAsync(string query, int first, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            return [];
        }

        var data = await GetCollectionAsync<HelixCategoryDto>($"{TwitchEndpoints.HelixSearchCategories}?query={Uri.EscapeDataString(query)}&first={first}",
            cancellationToken);

        return data.Select(c => new GameInfo(c.Id, c.Name, c.BoxArtUrl, null)).ToArray();
    }

    public async Task<GameInfo?> GetGameByIdAsync(string gameId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(gameId))
        {
            return null;
        }

        var dto = await GetSingleAsync<HelixGameDto>($"{TwitchEndpoints.HelixGames}?id={Uri.EscapeDataString(gameId)}",
            cancellationToken);

        return dto is null ? null : new GameInfo(dto.Id, dto.Name, dto.BoxArtUrl, dto.IgdbId);
    }

    public async Task SendChatMessageAsync(
        string broadcasterId,
        string senderId,
        string message,
        string? replyParentMessageId = null,
        CancellationToken cancellationToken = default)
    {
        var dto = new HelixSendChatMessageDto(broadcasterId, senderId, message, replyParentMessageId);

        using var client = httpClientFactory.CreateClient(TwitchEndpoints.HelixHttpClientName);
        using var httpRequest = new HttpRequestMessage(HttpMethod.Post, TwitchEndpoints.HelixChatMessages)
        {
            Content = JsonContent.Create(dto, options: JsonOptions),
        };

        using var response = await client.SendAsync(httpRequest, cancellationToken);
        await EnsureSuccessAsync(httpRequest, response, cancellationToken);

        var envelope = await response.Content.ReadFromJsonAsync<HelixChatMessageSendResponse>(JsonOptions, cancellationToken);
        var item = envelope?.Data?.FirstOrDefault();

        if (item is { IsSent: false })
        {
            var code = item.DropReason?.Code ?? "unknown";
            var reason = item.DropReason?.Message ?? string.Empty;
            throw new HelixMessageDroppedException(code, reason);
        }
    }

    public async Task<IReadOnlyDictionary<string, GlobalBadgeInfo>> GetGlobalChatBadgesAsync(CancellationToken cancellationToken = default)
    {
        var envelope = await GetRawAsync<HelixGlobalBadgesResponse>(TwitchEndpoints.HelixChatBadgesGlobal, cancellationToken);
        return (envelope?.Data ?? [])
            .ToDictionary(b => b.SetId,
                b => new GlobalBadgeInfo(b.SetId,
                    (b.Versions ?? []).Select(v => new GlobalBadgeVersion(v.Id, v.ImageUrl1x, v.ImageUrl2x, v.ImageUrl4x,
                        v.Title, v.Description, v.ClickAction, v.ClickUrl))
                    .ToArray()));
    }

    public async Task<IReadOnlyDictionary<string, GlobalEmoteInfo>> GetGlobalChatEmotesAsync(CancellationToken cancellationToken = default)
    {
        var envelope = await GetRawAsync<HelixGlobalEmotesResponse>(TwitchEndpoints.HelixChatEmotesGlobal, cancellationToken);
        return (envelope?.Data ?? [])
            .ToDictionary(e => e.Id,
                e => new GlobalEmoteInfo(e.Id,
                    e.Name,
                    new(e.Images?.Url1x ?? string.Empty,
                        e.Images?.Url2x ?? string.Empty,
                        e.Images?.Url4x ?? string.Empty),
                    e.Formats ?? [],
                    e.Scales ?? [],
                    e.ThemeModes ?? []));
    }

    public async Task<string> CreateEventSubSubscriptionAsync(
        string type,
        string version,
        IReadOnlyDictionary<string, string> condition,
        string sessionId,
        CancellationToken cancellationToken = default)
    {
        var body = new HelixEventSubSubscriptionRequest(type,
            version,
            condition,
            new("websocket", sessionId));

        using var client = httpClientFactory.CreateClient(TwitchEndpoints.HelixHttpClientName);
        using var httpRequest = new HttpRequestMessage(HttpMethod.Post, TwitchEndpoints.HelixEventSubSubscriptions)
        {
            Content = JsonContent.Create(body, options: JsonOptions),
        };

        using var response = await client.SendAsync(httpRequest, cancellationToken);
        await EnsureSuccessAsync(httpRequest, response, cancellationToken);

        var envelope = await response.Content.ReadFromJsonAsync<HelixEnvelope<HelixEventSubSubscriptionDto>>(JsonOptions, cancellationToken);
        var subscription = envelope?.Data?.FirstOrDefault();
        if (subscription is null)
        {
            throw new InvalidOperationException("EventSub подписка создана, но Twitch не вернул данные подписки.");
        }

        logger.LogInformation("Создана EventSub подписка {Id} типа {Type} (status={Status})",
            subscription.Id, subscription.Type, subscription.Status);

        return subscription.Id;
    }

    private static UserInfo MapUser(HelixUserDto dto)
    {
        return new(dto.Id, dto.Login, dto.DisplayName, dto.Type, dto.BroadcasterType,
            dto.Description, dto.ProfileImageUrl, dto.OfflineImageUrl, dto.CreatedAt);
    }

    private static HelixStreamInfo MapStream(HelixStreamDto dto)
    {
        return new(dto.Id, dto.UserId, dto.UserLogin, dto.UserName, dto.GameId, dto.GameName,
            dto.Type, dto.Title, dto.ViewerCount, dto.StartedAt, dto.Language, dto.ThumbnailUrl,
            dto.Tags ?? [], dto.IsMature);
    }

    private static ChannelInfo MapChannel(HelixChannelDto dto)
    {
        return new(dto.BroadcasterId, dto.BroadcasterLogin, dto.BroadcasterName, dto.BroadcasterLanguage,
            dto.GameId, dto.GameName, dto.Title, dto.Delay, dto.Tags ?? [],
            dto.ContentClassificationLabels ?? [], dto.IsBrandedContent);
    }

    private async Task EnsureSuccessAsync(
        HttpRequestMessage request,
        HttpResponseMessage response,
        CancellationToken cancellationToken)
    {
        if (response.IsSuccessStatusCode)
        {
            return;
        }

        logger.LogWarning("Helix {Method} {Path} вернул {Status}",
            request.Method,
            request.RequestUri?.AbsolutePath ?? string.Empty,
            (int)response.StatusCode);

        var body = await response.Content.ReadAsStringAsync(cancellationToken);

        string? twitchErrorMessage = null;
        try
        {
            var error = JsonSerializer.Deserialize<HelixErrorResponse>(body, JsonOptions);
            twitchErrorMessage = error?.Message;
        }
        catch (JsonException)
        {
        }

        throw new HelixRequestException(request.Method,
            request.RequestUri?.AbsolutePath ?? string.Empty,
            response.StatusCode,
            twitchErrorMessage,
            body);
    }

    private async Task<T?> GetRawAsync<T>(string requestUri, CancellationToken cancellationToken)
    {
        using var client = httpClientFactory.CreateClient(TwitchEndpoints.HelixHttpClientName);
        using var httpRequest = new HttpRequestMessage(HttpMethod.Get, requestUri);
        using var response = await client.SendAsync(httpRequest, cancellationToken);
        await EnsureSuccessAsync(httpRequest, response, cancellationToken);
        return await response.Content.ReadFromJsonAsync<T>(JsonOptions, cancellationToken);
    }

    private async Task<T?> GetSingleAsync<T>(string requestUri, CancellationToken cancellationToken)
    {
        var data = await GetCollectionAsync<T>(requestUri, cancellationToken);
        return data.FirstOrDefault();
    }

    private async Task<IReadOnlyList<T>> GetCollectionAsync<T>(string requestUri, CancellationToken cancellationToken)
    {
        using var client = httpClientFactory.CreateClient(TwitchEndpoints.HelixHttpClientName);
        using var httpRequest = new HttpRequestMessage(HttpMethod.Get, requestUri);
        using var response = await client.SendAsync(httpRequest, cancellationToken);
        await EnsureSuccessAsync(httpRequest, response, cancellationToken);

        var envelope = await response.Content.ReadFromJsonAsync<HelixEnvelope<T>>(JsonOptions, cancellationToken);
        return envelope?.Data ?? [];
    }

    private sealed record HelixErrorResponse(
        [property: JsonPropertyName("error")]
        string? Error,
        [property: JsonPropertyName("status")]
        int Status,
        [property: JsonPropertyName("message")]
        string? Message);
}
