namespace PoproshaykaBot.WinForms.Models;

/// <summary>
/// Статус стрима
/// </summary>
public enum StreamStatus
{
    /// <summary>
    /// Статус неизвестен (не подключен к EventSub)
    /// </summary>
    Unknown,

    /// <summary>
    /// Стрим онлайн
    /// </summary>
    Online,

    /// <summary>
    /// Стрим офлайн
    /// </summary>
    Offline,
}
