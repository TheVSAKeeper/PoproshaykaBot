namespace PoproshaykaBot.WinForms.Broadcast.Profiles;

public sealed class GameCategoryCacheEntry
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public DateTimeOffset LastUsedAt { get; set; }
}
