namespace PoproshaykaBot.Core.Statistics;

public sealed class BroadcastHistoryDocument
{
    public const int CurrentSchemaVersion = 2;

    public int SchemaVersion { get; set; } = 1;

    public List<BroadcastRecord> Broadcasts { get; set; } = [];

    public bool ProjectionOutOfDate { get; set; }
}
