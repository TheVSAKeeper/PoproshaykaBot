using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace PoproshaykaBot.Core.YouTube.Quota;

internal sealed class YouTubeQuotaDocument
{
    public const int CurrentSchemaVersion = 1;

    public static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    [JsonPropertyName("schemaVersion")]
    public int SchemaVersion { get; set; } = CurrentSchemaVersion;

    [JsonPropertyName("projects")]
    public Dictionary<string, YouTubeQuotaProjectRecord> Projects { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    public static YouTubeQuotaDocument CreateEmpty()
    {
        return new()
        {
            SchemaVersion = CurrentSchemaVersion,
            Projects = new(StringComparer.OrdinalIgnoreCase),
        };
    }

    public YouTubeQuotaDocument Clone()
    {
        var copy = CreateEmpty();
        copy.SchemaVersion = SchemaVersion;

        foreach (var (key, record) in Projects)
        {
            copy.Projects[key] = record.Clone();
        }

        return copy;
    }
}

internal sealed class YouTubeQuotaProjectRecord
{
    [JsonPropertyName("pacificDate")]
    public string PacificDate { get; set; } = string.Empty;

    [JsonPropertyName("dailyBudget")]
    public int DailyBudget { get; set; } = YouTubeSettings.DefaultDailyQuotaBudget;

    [JsonPropertyName("committedUnits")]
    public int CommittedUnits { get; set; }

    [JsonPropertyName("botCommittedUnits")]
    public int BotCommittedUnits { get; set; }

    [JsonPropertyName("broadcasterCommittedUnits")]
    public int BroadcasterCommittedUnits { get; set; }

    [JsonPropertyName("categoryUnits")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public Dictionary<string, int>? CategoryUnits { get; set; }

    [JsonPropertyName("exhaustedByServer")]
    public bool ExhaustedByServer { get; set; }

    [JsonPropertyName("updatedAtUtc")]
    public DateTimeOffset UpdatedAtUtc { get; set; }

    public YouTubeQuotaProjectRecord Clone()
    {
        return new()
        {
            PacificDate = PacificDate,
            DailyBudget = DailyBudget,
            CommittedUnits = CommittedUnits,
            BotCommittedUnits = BotCommittedUnits,
            BroadcasterCommittedUnits = BroadcasterCommittedUnits,
            CategoryUnits = CategoryUnits is null ? null : new Dictionary<string, int>(CategoryUnits, StringComparer.Ordinal),
            ExhaustedByServer = ExhaustedByServer,
            UpdatedAtUtc = UpdatedAtUtc,
        };
    }
}
