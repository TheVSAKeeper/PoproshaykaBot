namespace PoproshaykaBot.Core.Chat;

public sealed class MessageSettings
{
    private const string PirateFlag = "\U0001F3F4\u200D\u2620\uFE0F";

    public bool WelcomeEnabled { get; set; } = false;

    public string Welcome { get; set; } = "Добро пожаловать в чат, {username}! 👋";

    public bool FarewellEnabled { get; set; } = false;

    public string Farewell { get; set; } = "До свидания, {username}! Увидимся позже! ❤️";

    public bool ConnectionEnabled { get; set; } = true;

    public string Connection { get; set; } = "ЭЩКЕРЕ";

    public bool DisconnectionEnabled { get; set; } = true;

    public string Disconnection { get; set; } = "Пока-пока! ❤️";

    public bool PunishmentEnabled { get; set; } = true;

    public string PunishmentMessage { get; set; } = $"{PirateFlag} ВНИМАНИЕ! Пользователь @{{username}} был лично наказан СЕРЁГОЙ ПИРАТОМ! ⚔️ Убрано {{points}} из статистики. 💀 #пиратская_справедливость";

    public string PunishmentNotification { get; set; } = $"{PirateFlag} Пользователя {{username}} лично наказал СЕРЁГА ПИРАТ! ⚔️ Убрано {{points}}. 💀";

    public bool RewardEnabled { get; set; } = false;

    public string RewardMessage { get; set; } = "🎉 ВНИМАНИЕ! Пользователь @{username} был поощрен СЕРЁГОЙ ПИРАТОМ! 🏆 Добавлено {points} в статистику. ⭐ #пиратская_щедрость";

    public string RewardNotification { get; set; } = "🎉 Пользователя {username} поощрил СЕРЁГА ПИРАТ! 🏆 Добавлено {points}. ⭐";
    public string DonateCommandMessage { get; set; } = "Принимаем криптой, СБП, куаркод справа снизу, подробнее можно узнать в телеге https://t.me/bobito217";
}
