using System.Text.Json;
using System.Text.Json.Serialization;

namespace PoproshaykaBot.WinForms.Twitch.EventSub;

internal sealed record EventSubMessageDto(
    [property: JsonPropertyName("metadata")]
    EventSubMetadataDto Metadata,
    [property: JsonPropertyName("payload")]
    JsonElement Payload);

internal sealed record EventSubMetadataDto(
    [property: JsonPropertyName("message_id")]
    string MessageId,
    [property: JsonPropertyName("message_type")]
    string MessageType,
    [property: JsonPropertyName("message_timestamp")]
    DateTime MessageTimestamp,
    [property: JsonPropertyName("subscription_type")]
    string? SubscriptionType,
    [property: JsonPropertyName("subscription_version")]
    string? SubscriptionVersion);

internal sealed record EventSubSessionPayloadDto(
    [property: JsonPropertyName("session")]
    EventSubSessionDto Session);

internal sealed record EventSubSessionDto(
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("status")] string Status,
    [property: JsonPropertyName("keepalive_timeout_seconds")]
    int? KeepaliveTimeoutSeconds,
    [property: JsonPropertyName("reconnect_url")]
    string? ReconnectUrl,
    [property: JsonPropertyName("connected_at")]
    DateTime ConnectedAt);

internal sealed record EventSubRevocationPayloadDto(
    [property: JsonPropertyName("subscription")]
    EventSubRevocationSubscriptionDto Subscription);

internal sealed record EventSubRevocationSubscriptionDto(
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("type")] string Type,
    [property: JsonPropertyName("status")] string Status);

internal sealed record EventSubStreamOnlinePayloadDto(
    [property: JsonPropertyName("event")] EventSubStreamOnlineEventDto? Event);

internal sealed record EventSubStreamOnlineEventDto(
    [property: JsonPropertyName("id")]
    string Id,
    [property: JsonPropertyName("broadcaster_user_id")]
    string BroadcasterUserId,
    [property: JsonPropertyName("broadcaster_user_login")]
    string BroadcasterUserLogin,
    [property: JsonPropertyName("broadcaster_user_name")]
    string BroadcasterUserName,
    [property: JsonPropertyName("type")]
    string Type,
    [property: JsonPropertyName("started_at")]
    DateTime StartedAt);
