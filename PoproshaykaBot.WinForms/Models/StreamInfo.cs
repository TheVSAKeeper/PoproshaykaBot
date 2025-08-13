namespace PoproshaykaBot.WinForms.Models;

/// <summary>
/// Описание текущего стрима канала (заголовок, игра, зрители и т.п.).
/// </summary>
public class StreamInfo
{
    public string Id { get; set; } = string.Empty;
    public string UserId { get; set; } = string.Empty;
    public string UserLogin { get; set; } = string.Empty;
    public string UserName { get; set; } = string.Empty;

    public string GameId { get; set; } = string.Empty;
    public string GameName { get; set; } = string.Empty;

    public string Title { get; set; } = string.Empty;
    public string Language { get; set; } = string.Empty;
    public int ViewerCount { get; set; }
    public DateTime StartedAt { get; set; }
    public string ThumbnailUrl { get; set; } = string.Empty;
    public IReadOnlyList<string> Tags { get; set; } = Array.Empty<string>();
    public bool IsMature { get; set; }
}
