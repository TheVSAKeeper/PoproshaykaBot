using PoproshaykaBot.Core.Chat.Commands;
using PoproshaykaBot.Core.Platforms;
using PoproshaykaBot.Core.Users;
using System.Text.Json.Serialization;

namespace PoproshaykaBot.Core.Chat.History;

public sealed class ChatHistoryCacheDocument
{
    public const int CurrentSchemaVersion = 1;

    public int SchemaVersion { get; set; } = CurrentSchemaVersion;

    public StreamingPlatform Platform { get; set; }

    public string ChannelId { get; set; } = string.Empty;

    public string? SessionScopeId { get; set; }

    public string ChannelName { get; set; } = string.Empty;

    public DateTimeOffset? ClearedAtUtc { get; set; }

    public List<PersistedDeletedMessageTombstone> DeletedMessages { get; set; } = [];

    public List<PersistedAuthorModerationTombstone> ModeratedAuthors { get; set; } = [];

    public List<PersistedChatMessage> Messages { get; set; } = [];
}

public sealed class PersistedDeletedMessageTombstone
{
    public string MessageId { get; set; } = string.Empty;

    public DateTimeOffset RecordedAtUtc { get; set; }
}

public sealed class PersistedAuthorModerationTombstone
{
    public string UserId { get; set; } = string.Empty;

    public DateTimeOffset CutoffUtc { get; set; }

    public DateTimeOffset RecordedAtUtc { get; set; }
}

public sealed class PersistedChatMessage
{
    public StreamingPlatform Platform { get; set; }

    public string Channel { get; set; } = string.Empty;

    public string ChannelId { get; set; } = string.Empty;

    public string? ChatSessionId { get; set; }

    public string MessageId { get; set; } = string.Empty;

    public DateTime Timestamp { get; set; }

    public string UserId { get; set; } = string.Empty;

    public string DisplayName { get; set; } = string.Empty;

    public string Message { get; set; } = string.Empty;

    public ChatMessageType MessageType { get; set; }

    public UserStatus Status { get; set; } = UserStatus.None;

    public bool IsFirstTime { get; set; }

    public bool IsBot { get; set; }

    public PersistedCommandResponseMark? CommandResponse { get; set; }

    public string Color { get; set; } = string.Empty;

    public List<EmoteInfo> Emotes { get; set; } = [];

    public List<PersistedBadgeEntry> Badges { get; set; } = [];

    public Dictionary<string, string> BadgeUrls { get; set; } = new(StringComparer.Ordinal);
}

public sealed class PersistedBadgeEntry
{
    public string Key { get; set; } = string.Empty;

    public string Value { get; set; } = string.Empty;
}

public sealed class PersistedCommandResponseMark
{
    public string Canonical { get; set; } = string.Empty;

    [JsonConverter(typeof(CommandResponseTargetJsonConverter))]
    public CommandResponseTarget Target { get; set; }
}
