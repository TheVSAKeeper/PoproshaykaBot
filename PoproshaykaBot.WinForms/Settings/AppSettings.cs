namespace PoproshaykaBot.WinForms.Settings;

public class AppSettings
{
    public TwitchSettings Twitch { get; set; } = new();
    public UiSettings Ui { get; set; } = new();
    public SpecialCommandsSettings SpecialCommands { get; set; } = new();
}

public class TwitchSettings
{
    public string BotUsername { get; set; } = "thevsakeeper";

    public string Channel { get; set; } = "bobito217";

    public int MessagesAllowedInPeriod { get; set; } = 750;

    public int ThrottlingPeriodSeconds { get; set; } = 30;

    public string ClientId { get; set; } = string.Empty;

    public string ClientSecret { get; set; } = string.Empty;

    public string AccessToken { get; set; } = string.Empty;

    public string RefreshToken { get; set; } = string.Empty;

    public string RedirectUri { get; set; } = "http://localhost:8080";

    public string[] Scopes { get; set; } = ["chat:read", "chat:edit"];

    public int HttpServerPort { get; set; } = 8080;

    public bool HttpServerEnabled { get; set; } = true;

    public bool ObsOverlayEnabled { get; set; } = true;

    public MessageSettings Messages { get; set; } = new();

    public ObsChatSettings ObsChat { get; set; } = new();

    public AutoBroadcastSettings AutoBroadcast { get; set; } = new();
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
}

public class UiSettings
{
    public bool ShowLogsPanel { get; set; } = true;
    public bool ShowChatPanel { get; set; } = true;
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
