namespace PoproshaykaBot.Wpf.Bootstrap;

public static class SectionKeys
{
    public const string Overview = "overview";
    public const string Users = "users";
    public const string Streams = "streams";
    public const string Logs = "logs";
    public const string Settings = "settings";

    public static IReadOnlyList<string> All { get; } = [Overview, Users, Streams, Logs, Settings];

    public static IReadOnlyDictionary<string, string> LegacyTitles { get; } = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["Обзор"] = Overview,
        ["Пользователи"] = Users,
        ["История стримов"] = Streams,
        ["Логи"] = Logs,
        ["Настройки"] = Settings,
    };
}
