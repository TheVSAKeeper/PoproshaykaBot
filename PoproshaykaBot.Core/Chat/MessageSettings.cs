namespace PoproshaykaBot.Core.Chat;

public sealed class MessageSettings
{
    public bool WelcomeEnabled { get; set; } = false;

    public string Welcome { get; set; } = "Добро пожаловать в чат, {username}! 👋";

    public bool FarewellEnabled { get; set; } = false;

    public string Farewell { get; set; } = "До свидания, {username}! Увидимся позже! ❤️";

    public bool ConnectionEnabled { get; set; } = true;

    public string Connection { get; set; } = "ЭЩКЕРЕ";

    public bool DisconnectionEnabled { get; set; } = true;

    public string Disconnection { get; set; } = "Пока-пока! ❤️";

    public bool PunishmentEnabled { get; set; } = true;

    public string PunishmentMessage { get; set; } = "🏴‍☠️ ВНИМАНИЕ! Пользователь @{username} был лично наказан СЕРЁГОЙ ПИРАТОМ! ⚔️ Убрано {count} сообщений из статистики. 💀 #пиратская_справедливость";

    public string PunishmentNotification { get; set; } = "🏴‍☠️ Пользователя {username} лично наказал СЕРЁГА ПИРАТ! ⚔️ Убрано {count} сообщений. 💀";

    public bool RewardEnabled { get; set; } = false;

    public string RewardMessage { get; set; } = "🎉 ВНИМАНИЕ! Пользователь @{username} был поощрен СЕРЁГОЙ ПИРАТОМ! 🏆 Добавлено {count} сообщений в статистику. ⭐ #пиратская_щедрость";

    public string RewardNotification { get; set; } = "🎉 Пользователя {username} поощрил СЕРЁГА ПИРАТ! 🏆 Добавлено {count} сообщений. ⭐";
    public string DonateCommandMessage { get; set; } = "Принимаем криптой, СБП, куаркод справа снизу, подробнее можно узнать в телеге https://t.me/bobito217";
}
