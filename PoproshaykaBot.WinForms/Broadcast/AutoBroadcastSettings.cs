namespace PoproshaykaBot.WinForms.Broadcast;

public sealed class AutoBroadcastSettings
{
    public bool AutoBroadcastEnabled { get; set; } = false;

    public bool StreamStatusNotificationsEnabled { get; set; } = true;

    public string StreamStartMessage { get; set; } = "🔴 Стрим запущен! Начинаю рассылку.";

    public string StreamStopMessage { get; set; } = "⚫ Стрим завершен. Рассылка остановлена.";

    public string StreamEndStatsMessage { get; set; } = "📊 Стрим завершён! Длительность: {duration}, сообщений: {messages}, активных зрителей: {chatters}, пик: {peakViewers}, средний онлайн: {avgViewers}. Играли в {game}.";

    public int BroadcastIntervalMinutes { get; set; } = 15;

    public string BroadcastMessageTemplate { get; set; } = "Присылайте деняк, пожалуйста, {counter} раз прошу. https://bob217.ru/donate/";
}
