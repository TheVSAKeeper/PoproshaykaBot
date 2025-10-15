namespace PoproshaykaBot.Core.Domain.Models.Settings;

public class AutoBroadcastSettings
{
    public bool AutoBroadcastEnabled { get; set; } = false;

    public bool StreamStatusNotificationsEnabled { get; set; } = true;

    public string StreamStartMessage { get; set; } = "🔴 Стрим запущен! Начинаю рассылку.";

    public string StreamStopMessage { get; set; } = "⚫ Стрим завершен. Рассылка остановлена.";

    public int BroadcastIntervalMinutes { get; set; } = 15;

    public string BroadcastMessageTemplate { get; set; } = "Присылайте деняк, пожалуйста, {counter} раз прошу. https://bob217.ru/donate/";
}