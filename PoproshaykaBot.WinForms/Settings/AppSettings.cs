using PoproshaykaBot.WinForms.Broadcast.Profiles;
using PoproshaykaBot.WinForms.Polls;
using PoproshaykaBot.WinForms.Twitch.Chat;
using PoproshaykaBot.WinForms.Users;

namespace PoproshaykaBot.WinForms.Settings;

public class AppSettings
{
    public TwitchSettings Twitch { get; set; } = new();
    public UiSettings Ui { get; set; } = new();
    public SpecialCommandsSettings SpecialCommands { get; set; } = new();
    public RanksSettings Ranks { get; set; } = new();
}

public class TwitchSettings
{
    public string Channel { get; set; } = "bobito217";

    public int MessagesAllowedInPeriod { get; set; } = 750;

    public int ThrottlingPeriodSeconds { get; set; } = 30;

    public string ClientId { get; set; } = string.Empty;

    public string ClientSecret { get; set; } = string.Empty;

    public string RedirectUri { get; set; } = "http://localhost:8080";

    public TwitchAccountSettings BotAccount { get; set; } = new()
    {
        Scopes =
        [
            TwitchScopes.UserReadChat,
            TwitchScopes.UserWriteChat,
            TwitchScopes.UserBot,
        ],
    };

    public TwitchAccountSettings BroadcasterAccount { get; set; } = new()
    {
        Scopes =
        [
            TwitchScopes.ChannelBot,
            TwitchScopes.ChannelManageBroadcast,
            TwitchScopes.ChannelManagePolls,
            TwitchScopes.ChannelReadPolls,
        ],
    };

    public int HttpServerPort { get; set; } = 8080;

    public bool HttpServerEnabled { get; set; } = true;

    public bool ObsOverlayEnabled { get; set; } = true;

    public MessageSettings Messages { get; set; } = new();

    public ObsChatSettings ObsChat { get; set; } = new();

    public AutoBroadcastSettings AutoBroadcast { get; set; } = new();

    public InfrastructureSettings Infrastructure { get; set; } = new();

    public BroadcastProfilesSettings BroadcastProfiles { get; set; } = new();

    public PollsSettings Polls { get; set; } = new();
}

public class TwitchAccountSettings
{
    public string AccessToken { get; set; } = string.Empty;

    public string RefreshToken { get; set; } = string.Empty;

    public string Login { get; set; } = string.Empty;

    public string UserId { get; set; } = string.Empty;

    public string[] Scopes { get; set; } = [];

    public string[] StoredScopes { get; set; } = [];
}

public class MessageSettings
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

public class DashboardTileSettings
{
    public string Id { get; set; } = string.Empty;
    public string TypeId { get; set; } = string.Empty;
    public int Order { get; set; }
    public int Row { get; set; }
    public int Column { get; set; }
    public int ColumnSpan { get; set; } = 1;
    public int RowSpan { get; set; } = 1;
    public bool IsVisible { get; set; } = true;
}

public class DashboardLayoutSettings
{
    public int ColumnCount { get; set; } = 4;
    public int RowCount { get; set; } = 3;
    public List<DashboardTileSettings> Tiles { get; set; } = [];
}

public class UiSettings
{
    public DashboardLayoutSettings? Dashboard { get; set; }
}

public class InfrastructureSettings
{
    public int ChatHistoryMaxItems { get; set; } = 1000;
    public int SseKeepAliveSeconds { get; set; } = 30;
    public List<GameCategoryCacheEntry> RecentCategories { get; set; } = [];
}

public class ObsChatSettings
{
    public AppColor BackgroundColor { get; set; } = AppColor.FromArgb(179, 0, 0, 0);
    public AppColor TextColor { get; set; } = AppColor.FromArgb(255, 255, 255);
    public AppColor UsernameColor { get; set; } = AppColor.FromArgb(145, 70, 255);
    public AppColor SystemMessageColor { get; set; } = AppColor.FromArgb(255, 204, 0);
    public AppColor TimestampColor { get; set; } = AppColor.FromArgb(153, 153, 153);

    public string FontFamily { get; set; } = "Arial, sans-serif";
    public int FontSize { get; set; } = 14;
    public bool FontBold { get; set; } = false;

    public int Padding { get; set; } = 5;
    public int Margin { get; set; } = 5;
    public int BorderRadius { get; set; } = 5;

    public int AnimationDuration { get; set; } = 300;
    public bool EnableAnimations { get; set; } = true;

    public int MaxMessages { get; set; } = 50;
    public bool ShowTimestamp { get; set; } = true;

    public int EmoteSizePixels { get; set; } = 28;
    public int BadgeSizePixels { get; set; } = 18;

    public bool ShowUserTypeBorders { get; set; } = true;
    public bool HighlightFirstTimeUsers { get; set; } = true;
    public bool HighlightMentions { get; set; } = true;
    public bool EnableMessageShadows { get; set; } = true;
    public bool EnableSpecialEffects { get; set; } = true;

    public bool EnableSmoothScroll { get; set; } = true;
    public int ScrollAnimationDuration { get; set; } = 300;
    public bool AutoScrollEnabled { get; set; } = true;
    public int ScrollToBottomThreshold { get; set; } = 100;

    public string UserMessageAnimation { get; set; } = MessageAnimationType.SlideInRight;
    public string BotMessageAnimation { get; set; } = MessageAnimationType.FadeInUp;
    public string SystemMessageAnimation { get; set; } = MessageAnimationType.FadeInUp;
    public string BroadcasterMessageAnimation { get; set; } = MessageAnimationType.SlideInLeft;
    public string FirstTimeUserMessageAnimation { get; set; } = MessageAnimationType.BounceIn;

    public bool EnableMessageFadeOut { get; set; } = true;
    public int MessageLifetimeSeconds { get; set; } = 30;
    public string FadeOutAnimationType { get; set; } = MessageAnimationType.FadeOut;
    public int FadeOutAnimationDurationMs { get; set; } = 1000;
}

public static class MessageAnimationType
{
    public const string None = "no-animation";
    public const string SlideInRight = "slide-in-right";
    public const string SlideInLeft = "slide-in-left";
    public const string FadeInUp = "fade-in-up";
    public const string BounceIn = "bounce-in";

    public const string FadeOut = "fade-out";
    public const string SlideOutLeft = "slide-out-left";
    public const string SlideOutRight = "slide-out-right";
    public const string ScaleDown = "scale-down";
    public const string ShrinkUp = "shrink-up";

    public static readonly string[] DisplayNames =
    [
        "Без анимации",
        "Скольжение справа",
        "Скольжение слева",
        "Затухание сверху",
        "Прыжок",
        "Исчезновение",
        "Выскользнуть влево",
        "Выскользнуть вправо",
        "Уменьшение",
        "Свернуться вверх",
    ];
}

// TODO: Костыль из-за того, что сериализатор не умеет работать с системным Color
public class AppColor(byte a, byte r, byte g, byte b)
{
    private AppColor(byte r, byte g, byte b) : this(255, r, g, b)
    {
    }

    public byte A { get; set; } = a;
    public byte R { get; set; } = r;
    public byte G { get; set; } = g;
    public byte B { get; set; } = b;

    public static implicit operator Color(AppColor appColor)
    {
        return Color.FromArgb(appColor.A, appColor.R, appColor.G, appColor.B);
    }

    public static implicit operator AppColor(Color color)
    {
        return new(color.A, color.R, color.G, color.B);
    }

    public static AppColor FromArgb(byte a, byte r, byte g, byte b)
    {
        return new(a, r, g, b);
    }

    public static AppColor FromArgb(byte r, byte g, byte b)
    {
        return new(r, g, b);
    }
}

public class AutoBroadcastSettings
{
    public bool AutoBroadcastEnabled { get; set; } = false;

    public bool StreamStatusNotificationsEnabled { get; set; } = true;

    public string StreamStartMessage { get; set; } = "🔴 Стрим запущен! Начинаю рассылку.";

    public string StreamStopMessage { get; set; } = "⚫ Стрим завершен. Рассылка остановлена.";

    public int BroadcastIntervalMinutes { get; set; } = 15;

    public string BroadcastMessageTemplate { get; set; } = "Присылайте деняк, пожалуйста, {counter} раз прошу. https://bob217.ru/donate/";
}

public class SpecialCommandsSettings
{
    public decimal X2IllsonCoins { get; set; } = 93.94m;

    public decimal X2IllsonPurchasePrice { get; set; } = 33.08m;

    public List<string> AllowedUsers { get; set; } = ["qp_illson"];

    public string SuccessMessage { get; set; } = "Держи бро! 👋";

    public string UnauthorizedMessage { get; set; } = "Ты новенький? 🤔 Подрасти сначала для таких вещей.";
}

public sealed class RanksSettings
{
    public List<UserRank> Ranks { get; set; } =
    [
        new("♔", "КОРОЛЬ", 5000),

        new("♛", "ФЕРЗЬ", 4000, 1),
        new("♛", "ФЕРЗЬ", 3500, 2),
        new("♛", "ФЕРЗЬ", 3000, 3),

        new("♜", "ЛАДЬЯ", 2500, 1),
        new("♜", "ЛАДЬЯ", 2000, 2),
        new("♜", "ЛАДЬЯ", 1500, 3),

        new("♝", "СЛОН", 1200, 1),
        new("♝", "СЛОН", 1000, 2),
        new("♝", "СЛОН", 800, 3),

        new("♞", "КОНЬ", 600, 1),
        new("♞", "КОНЬ", 450, 2),
        new("♞", "КОНЬ", 300, 3),

        new("♟", "ПЕШКА", 200, 1),
        new("♟", "ПЕШКА", 100, 2),
        new("♟", "ПЕШКА", 0, 3),
    ];
}
