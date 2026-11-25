using PoproshaykaBot.Core.Broadcast;
using PoproshaykaBot.Core.Chat;
using PoproshaykaBot.Core.Polls;
using PoproshaykaBot.Core.Settings;
using PoproshaykaBot.Core.Settings.Stores;

namespace PoproshaykaBot.Wpf.ViewModels;

internal enum BotMessageAudience
{
    EventChat = 0,
    ConnectedChats = 1,
    ProfileTargets = 2,
    Twitch = 3,
}

internal sealed record BotMessageValues(bool Enabled, string Text, int? Interval = null);

internal sealed record BotMessageRule(
    string Key,
    string Group,
    string Title,
    string Trigger,
    BotMessageAudience Audience,
    string ToggleLabel,
    IReadOnlyDictionary<string, string> Examples,
    Func<BotMessageValues> Read,
    Func<BotMessageValues, BotMessageValues, bool> Save,
    string? DefaultForBlank = null,
    string? IntervalLabel = null,
    int MinimumInterval = 1,
    int MaximumInterval = int.MaxValue)
{
    public bool? DefaultEnabled { get; init; }

    public string BlankHint => DefaultForBlank is null
        ? "Пустой текст пропускает публикацию."
        : "При сохранении пустого поля вернётся стандартный текст.";

    public static IReadOnlyList<BotMessageRule> Create(SettingsManager settings, PollsStore polls)
    {
        const string audience = "Зрители";
        const string connection = "Бот в чате";
        const string stream = "Эфир";
        const string points = "Баллы";
        const string poll = "Опросы";
        const string streamToggle = "Уведомления об эфире (общее для трёх сообщений)";
        var defaults = new MessageSettings();
        var noExamples = new Dictionary<string, string>(StringComparer.Ordinal);
        var viewer = new Dictionary<string, string>(StringComparer.Ordinal) { ["username"] = "Алекс" };
        var farewell = new Dictionary<string, string>(StringComparer.Ordinal) { ["username"] = "Алекс", ["usernames"] = "Алекс, Маша" };
        var pointExamples = new Dictionary<string, string>(StringComparer.Ordinal) { ["username"] = "Алекс", ["points"] = "50" };
        var stats = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["duration"] = "1 ч 20 мин", ["messages"] = "240", ["chatters"] = "38",
            ["peakViewers"] = "52", ["avgViewers"] = "33", ["title"] = "Вечерний эфир",
            ["game"] = "Minecraft", ["channel"] = "example",
        };
        var periodic = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["counter"] = "3", ["title"] = "Вечерний эфир", ["game"] = "Minecraft", ["viewers"] = "42",
        };
        var pollExamples = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["title"] = "Во что играем?", ["choices"] = "Minecraft, Terraria", ["leader"] = "Minecraft",
            ["leaderVotes"] = "12", ["totalVotes"] = "20", ["durationLeft"] = "45 с",
            ["winner"] = "", ["winnerVotes"] = "0", ["status"] = "активен",
        };
        var finalizedPoll = new Dictionary<string, string>(pollExamples, StringComparer.Ordinal)
        {
            ["winner"] = "Minecraft", ["winnerVotes"] = "12", ["status"] = "завершён",
        };

        return
        [
            Chat("welcome", audience, "Приветствие зрителя",
                "После первого подходящего сообщения зрителя за сессию чата. Публикуется отдельно от ответа команды.",
                viewer, m => m.WelcomeEnabled, (m, v) => m.WelcomeEnabled = v,
                m => m.Welcome, (m, v) => m.Welcome = v, defaults.Welcome),
            Chat("farewell", audience, "Прощание со зрителями",
                "При отключении бота, если за сессию были активные зрители. Этот же текст и переключатель использует !пока. {usernames} подставляется при коллективном прощании.",
                farewell, m => m.FarewellEnabled, (m, v) => m.FarewellEnabled = v,
                m => m.Farewell, (m, v) => m.Farewell = v, defaults.Farewell),
            Chat("connect", connection, "Подключение бота",
                "Когда бот успешно входит в чат площадки.",
                noExamples, m => m.ConnectionEnabled, (m, v) => m.ConnectionEnabled = v,
                m => m.Connection, (m, v) => m.Connection = v, defaults.Connection),
            Chat("disconnect", connection, "Отключение бота",
                "Когда бот начинает выходить из подключённого чата. Может объединиться с прощанием со зрителями.",
                noExamples, m => m.DisconnectionEnabled, (m, v) => m.DisconnectionEnabled = v,
                m => m.Disconnection, (m, v) => m.Disconnection = v, defaults.Disconnection),
            Broadcast("stream-start", "Начало эфира",
                "При начале первого эфира. Если чат площадки был недоступен, уведомление может прийти при его подключении. Обнаружение уже идущего эфира само по себе уведомление не публикует.",
                noExamples, b => b.StreamStartMessage, (b, v) => b.StreamStartMessage = v),
            Broadcast("stream-stop", "Окончание эфира",
                "При окончании последнего эфира. Уведомление и итог эфира обрабатываются отдельно.",
                noExamples, b => b.StreamStopMessage, (b, v) => b.StreamStopMessage = v),
            Broadcast("stream-stats", "Итог эфира",
                "После завершения и сохранения сессии с итоговой статистикой.",
                stats, b => b.StreamEndStatsMessage, (b, v) => b.StreamEndStatsMessage = v),
            App("broadcast", stream, "Периодическая рассылка",
                "По таймеру после запуска рассылки или вручную из этого правила. Автозапуск проверяется при следующем событии эфира или готовности чата.",
                BotMessageAudience.ProfileTargets, "Запускать рассылку автоматически", periodic,
                s => s.Twitch.AutoBroadcast, b => b.AutoBroadcastEnabled, (b, v) => b.AutoBroadcastEnabled = v,
                b => b.BroadcastMessageTemplate, (b, v) => b.BroadcastMessageTemplate = v,
                intervalLabel: "Интервал рассылки, мин",
                getInterval: b => Math.Max(1, b.BroadcastIntervalMinutes),
                setInterval: (b, v) => b.BroadcastIntervalMinutes = v),
            Chat("punishment", points, "Снятие баллов",
                "После снятия баллов. Чат получит сообщение, если открытый канал совпадает с каналом события.",
                pointExamples, m => m.PunishmentEnabled, (m, v) => m.PunishmentEnabled = v,
                m => m.PunishmentMessage, (m, v) => m.PunishmentMessage = v),
            Chat("reward", points, "Начисление баллов",
                "После начисления баллов. Чат получит сообщение, если открытый канал совпадает с каналом события.",
                pointExamples, m => m.RewardEnabled, (m, v) => m.RewardEnabled = v,
                m => m.RewardMessage, (m, v) => m.RewardMessage = v),
            Poll("poll-start", "Начало опроса", "Когда опрос опубликован в Twitch.", pollExamples,
                t => t.StartEnabled, (t, v) => t.StartEnabled = v, t => t.StartTemplate, (t, v) => t.StartTemplate = v),
            Poll("poll-progress", "Ход опроса", "При обновлении голосов с учётом интервала объявлений.", pollExamples,
                t => t.ProgressEnabled, (t, v) => t.ProgressEnabled = v,
                t => t.ProgressTemplate, (t, v) => t.ProgressTemplate = v,
                "Минимальный интервал объявлений, с",
                t => Math.Clamp(t.ProgressAnnounceIntervalSeconds, 15, 3600),
                (t, v) => t.ProgressAnnounceIntervalSeconds = v),
            Poll("poll-end", "Результат опроса", "Когда Twitch завершает подсчёт голосов.", finalizedPoll,
                t => t.EndEnabled, (t, v) => t.EndEnabled = v, t => t.EndTemplate, (t, v) => t.EndTemplate = v),
            Poll("poll-terminated", "Досрочное завершение опроса", "Когда опрос прерван.", pollExamples,
                t => t.TerminatedEnabled, (t, v) => t.TerminatedEnabled = v,
                t => t.TerminatedTemplate, (t, v) => t.TerminatedTemplate = v),
            Poll("poll-archived", "Архивирование опроса", "Когда Twitch переводит опрос в архив.", pollExamples,
                t => t.ArchivedEnabled, (t, v) => t.ArchivedEnabled = v,
                t => t.ArchivedTemplate, (t, v) => t.ArchivedTemplate = v),
        ];

        BotMessageRule Chat(
            string key, string group, string title, string trigger, IReadOnlyDictionary<string, string> examples,
            Func<MessageSettings, bool> enabled, Action<MessageSettings, bool> setEnabled,
            Func<MessageSettings, string> text, Action<MessageSettings, string> setText,
            string? defaultForBlank = null)
            => App(key, group, title, trigger, BotMessageAudience.EventChat, "Публиковать в чат",
                examples, s => s.ChatMessages, enabled, setEnabled, text, setText, defaultForBlank) with
            {
                DefaultEnabled = defaultForBlank is null ? null : enabled(defaults),
            };

        BotMessageRule Broadcast(
            string key, string title, string trigger, IReadOnlyDictionary<string, string> examples,
            Func<AutoBroadcastSettings, string> text, Action<AutoBroadcastSettings, string> setText)
            => App(key, stream, title, trigger, BotMessageAudience.ConnectedChats, streamToggle, examples,
                s => s.Twitch.AutoBroadcast, b => b.StreamStatusNotificationsEnabled,
                (b, v) => b.StreamStatusNotificationsEnabled = v, text, setText);

        BotMessageRule App<T>(
            string key, string group, string title, string trigger, BotMessageAudience targets, string toggle,
            IReadOnlyDictionary<string, string> examples, Func<AppSettings, T> select,
            Func<T, bool> enabled, Action<T, bool> setEnabled, Func<T, string> text, Action<T, string> setText,
            string? defaultForBlank = null, string? intervalLabel = null, Func<T, int>? getInterval = null,
            Action<T, int>? setInterval = null)
            => Build(key, group, title, trigger, targets, toggle, examples, () => select(settings.Current),
                change => settings.MutateIfWritable(s => change(select(s))), enabled, setEnabled, text, setText,
                value => string.IsNullOrWhiteSpace(value) && defaultForBlank is not null ? defaultForBlank : value.Trim(),
                defaultForBlank, intervalLabel, getInterval, setInterval);

        BotMessageRule Poll(
            string key, string title, string trigger, IReadOnlyDictionary<string, string> examples,
            Func<PollChatTemplatesSettings, bool> enabled, Action<PollChatTemplatesSettings, bool> setEnabled,
            Func<PollChatTemplatesSettings, string> text, Action<PollChatTemplatesSettings, string> setText,
            string? intervalLabel = null, Func<PollChatTemplatesSettings, int>? getInterval = null,
            Action<PollChatTemplatesSettings, int>? setInterval = null)
            => Build(key, poll, title, trigger, BotMessageAudience.Twitch, "Публиковать в чат", examples,
                () => polls.Load().ChatTemplates, change => polls.MutatePersisted(s => change(s.ChatTemplates)),
                enabled, setEnabled, text, setText, value => value, null, intervalLabel, getInterval, setInterval, 15, 3600);
    }

    private static BotMessageRule Build<T>(
        string key, string group, string title, string trigger, BotMessageAudience targets, string toggle,
        IReadOnlyDictionary<string, string> examples, Func<T> read, Func<Action<T>, bool> save,
        Func<T, bool> enabled, Action<T, bool> setEnabled, Func<T, string> text, Action<T, string> setText,
        Func<string, string> normalize, string? defaultForBlank, string? intervalLabel,
        Func<T, int>? getInterval, Action<T, int>? setInterval, int minimum = 1, int maximum = int.MaxValue)
    {
        return new(key, group, title, trigger, targets, toggle, examples, () =>
        {
            var settings = read();
            return new(enabled(settings), text(settings), getInterval?.Invoke(settings));
        }, (original, draft) => save(settings =>
        {
            if (draft.Enabled != original.Enabled)
            {
                setEnabled(settings, draft.Enabled);
            }

            if (draft.Text != original.Text)
            {
                setText(settings, normalize(draft.Text));
            }

            if (draft.Interval != original.Interval && draft.Interval is { } interval)
            {
                setInterval?.Invoke(settings, interval);
            }
        }), defaultForBlank, intervalLabel, minimum, maximum);
    }
}
