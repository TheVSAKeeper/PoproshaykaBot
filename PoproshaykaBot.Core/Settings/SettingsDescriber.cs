namespace PoproshaykaBot.Core.Settings;

public static class SettingsDescriber
{
    public const string Unset = "–";

    public static string Describe(AppSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);

        var twitch = settings.Twitch;
        var messages = twitch.Messages;
        var broadcast = twitch.AutoBroadcast;
        var lifecycle = twitch.BotLifecycleAutomation;

        return $"канал {Value(twitch.Channel)}, clientId {Present(twitch.ClientId)}, clientSecret {Present(twitch.ClientSecret)}, "
            + $"redirectUri {Value(twitch.RedirectUri)}, порт {twitch.HttpServerPort}, аккаунт чата {twitch.ChatDisplayAccount}, "
            + $"лимит {twitch.MessagesAllowedInPeriod} сообщений за {twitch.ThrottlingPeriodSeconds} с, "
            + $"приветствие {OnOff(messages.WelcomeEnabled)}, прощание {OnOff(messages.FarewellEnabled)}, "
            + $"подключение {OnOff(messages.ConnectionEnabled)}, отключение {OnOff(messages.DisconnectionEnabled)}, "
            + $"наказание {OnOff(messages.PunishmentEnabled)}, поощрение {OnOff(messages.RewardEnabled)}, "
            + $"авторассылка {OnOff(broadcast.AutoBroadcastEnabled)} каждые {broadcast.BroadcastIntervalMinutes} мин, "
            + $"уведомления о стриме {OnOff(broadcast.StreamStatusNotificationsEnabled)}, "
            + $"автоподключение на онлайн {OnOff(lifecycle.AutoConnectOnStreamOnline)}, "
            + $"автоотключение на офлайн {OnOff(lifecycle.AutoDisconnectOnStreamOffline)}, "
            + $"спец-команды: пользователей {settings.SpecialCommands.AllowedUsers.Count}, рангов {settings.Ranks.Ranks.Count}";
    }

    public static string OnOff(bool value)
    {
        return value ? "вкл" : "выкл";
    }

    public static string Present(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? "нет" : "есть";
    }

    public static string Value(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? Unset : value;
    }

    public static string Moment(DateTimeOffset? value)
    {
        return value is { } moment
            ? moment.UtcDateTime.ToString("u")
            : Unset;
    }
}
