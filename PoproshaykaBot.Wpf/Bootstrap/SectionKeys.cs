namespace PoproshaykaBot.Wpf.Bootstrap;

public static class SectionKeys
{
    public const string Overview = "overview";
    public const string Users = "users";
    public const string Streams = "streams";
    public const string Logs = "logs";
    public const string Settings = "settings";

    public const string SelectedSuffix = ":selected";

    public const string UsersSelected = Users + SelectedSuffix;
    public const string StreamsSelected = Streams + SelectedSuffix;

    public static IReadOnlyList<string> All { get; } = [Overview, Users, Streams, Logs, Settings];

    public static IReadOnlyList<string> Selected { get; } = [UsersSelected, StreamsSelected];

    public static IReadOnlyDictionary<string, string> LegacyTitles { get; } = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["Обзор"] = Overview,
        ["Пользователи"] = Users,
        ["История стримов"] = Streams,
        ["Логи"] = Logs,
        ["Настройки"] = Settings,
    };

    public static bool IsSelectedCase(string key)
    {
        ArgumentNullException.ThrowIfNull(key);

        return Selected.Contains(key, StringComparer.OrdinalIgnoreCase);
    }

    public static string PageOf(string key)
    {
        ArgumentNullException.ThrowIfNull(key);

        return IsSelectedCase(key) ? key[..^SelectedSuffix.Length] : key;
    }
}
