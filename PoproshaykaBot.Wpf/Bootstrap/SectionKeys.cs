namespace PoproshaykaBot.Wpf.Bootstrap;

/// <summary>
/// Ключи секций рейла. Устойчивое имя страницы задаётся здесь и живёт на самом
/// <see cref="KeepShell.ViewModels.NavigationItem.Key" />, а подпись остаётся текстом, который можно
/// переписать: навигация и запомненная страница переживают правку подписи, поиск по подписи — нет.
/// </summary>
public static class SectionKeys
{
    public const string Overview = "overview";
    public const string Users = "users";
    public const string Streams = "streams";
    public const string Logs = "logs";
    public const string Settings = "settings";

    public static IReadOnlyList<string> All { get; } = [Overview, Users, Streams, Logs, Settings];

    /// <summary>
    /// Подписи, под которыми страницы запоминались до перехода на ключи. Без этого первый запуск
    /// после обновления открывал бы не ту страницу, на которой человек закончил.
    /// </summary>
    public static IReadOnlyDictionary<string, string> LegacyTitles { get; } = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["Обзор"] = Overview,
        ["Пользователи"] = Users,
        ["История стримов"] = Streams,
        ["Логи"] = Logs,
        ["Настройки"] = Settings,
    };
}
