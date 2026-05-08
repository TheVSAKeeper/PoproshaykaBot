namespace PoproshaykaBot.Core.Streaming;

/// <summary>
/// Статус стрима
/// </summary>
public enum StreamStatus
{
    /// <summary>
    /// Статус неизвестен (не подключен к EventSub)
    /// </summary>
    Unknown = 0,

    /// <summary>
    /// Стрим онлайн
    /// </summary>
    Online = 1,

    /// <summary>
    /// Стрим офлайн
    /// </summary>
    Offline = 2,
}
