namespace PoproshaykaBot.Wpf.Bootstrap;

public static class SectionKeys
{
    public const string Overview = "overview";
    public const string Users = "users";
    public const string Streams = "streams";
    public const string Commands = "commands";
    public const string Logs = "logs";
    public const string Diagnostics = "diagnostics";
    public const string Settings = "settings";

    public const string SelectedSuffix = ":selected";
    public const string CardsSuffix = ":cards";

    public const string UsersSelected = Users + SelectedSuffix;
    public const string StreamsSelected = Streams + SelectedSuffix;
    public const string StreamsCards = Streams + CardsSuffix;

    public static IReadOnlyList<string> All { get; } = [Overview, Users, Streams, Commands, Logs, Diagnostics, Settings];

    public static IReadOnlyList<string> Selected { get; } = [UsersSelected, StreamsSelected];

    public static IReadOnlyList<string> Cards { get; } = [StreamsCards];

    public static IReadOnlyDictionary<string, string> LegacyTitles { get; } = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["Обзор"] = Overview,
        ["Пользователи"] = Users,
        ["История стримов"] = Streams,
        ["Команды"] = Commands,
        ["Логи"] = Logs,
        ["Настройки"] = Settings,
    };

    public static bool IsSelectedCase(string key)
    {
        ArgumentNullException.ThrowIfNull(key);

        return Selected.Contains(key, StringComparer.OrdinalIgnoreCase);
    }

    public static bool IsCardsCase(string key)
    {
        ArgumentNullException.ThrowIfNull(key);

        return Cards.Contains(key, StringComparer.OrdinalIgnoreCase);
    }

    public static string PageOf(string key)
    {
        ArgumentNullException.ThrowIfNull(key);

        if (IsSelectedCase(key))
        {
            return key[..^SelectedSuffix.Length];
        }

        return IsCardsCase(key) ? key[..^CardsSuffix.Length] : key;
    }
}
