using PoproshaykaBot.Core.Platforms;
using PoproshaykaBot.Core.Twitch.Auth;

namespace PoproshaykaBot.Wpf.Infrastructure;

public enum PlatformWebChatLoginOutcome
{
    None = 0,
    Completed = 1,
    Canceled = 2,
    Unavailable = 3,
    Failed = 4,
}

public sealed record PlatformWebChatLoginDescriptor(
    StreamingPlatform Platform,
    string PlatformDisplayName,
    string StartUrl,
    string UserDataFolder,
    string CookieTargetUrl,
    IReadOnlyList<string> AllowedHosts,
    IReadOnlyList<string> AuthCookieNames,
    string ReadyInstructionTemplate,
    string SignedInInstructionTemplate);

public static class PlatformWebChatDescriptors
{
    public static PlatformWebChatLoginDescriptor Twitch { get; } = new(
        StreamingPlatform.Twitch,
        "Twitch",
        "https://www.twitch.tv/login",
        WebView2UserDataFolders.ResolveTwitchAccounts(),
        "https://www.twitch.tv",
        ["www.twitch.tv", "twitch.tv"],
        ["auth-token", "login"],
        "Войдите в Twitch для «{0}» – после входа кнопка «Готово» станет активной.",
        "Вход в Twitch для «{0}» выполнен. Нажмите «Готово», чтобы сохранить веб-сессию.");

    public static PlatformWebChatLoginDescriptor YouTube { get; } = new(
        StreamingPlatform.YouTube,
        "YouTube",
        "https://accounts.google.com/ServiceLogin?service=youtube&continue=https%3A%2F%2Fwww.youtube.com%2F",
        WebView2UserDataFolders.ResolveYouTubeAccounts(),
        "https://www.youtube.com",
        ["youtube.com"],
        ["SAPISID", "__Secure-3PAPISID", "SID", "SSID", "HSID"],
        "Войдите в Google для «{0}» и дождитесь загрузки YouTube – после входа кнопка «Готово» станет активной.",
        "Вход в YouTube выполнен. Выберите нужный канал для «{0}» и нажмите «Готово».");

    public static PlatformWebChatLoginDescriptor? ForPlatform(StreamingPlatform platform) =>
        platform switch
        {
            StreamingPlatform.Twitch => Twitch,
            StreamingPlatform.YouTube => YouTube,
            _ => null,
        };
}

public sealed record PlatformWebChatLoginRequest(
    PlatformWebChatLoginDescriptor Descriptor,
    string AccountId,
    string AccountLabel);

public sealed record PlatformWebChatLoginResult(
    PlatformWebChatLoginOutcome Outcome,
    string? BrowserProfileName = null,
    string? Message = null)
{
    public static PlatformWebChatLoginResult Completed(string browserProfileName) =>
        new(PlatformWebChatLoginOutcome.Completed, browserProfileName);

    public static PlatformWebChatLoginResult Canceled() =>
        new(PlatformWebChatLoginOutcome.Canceled);

    public static PlatformWebChatLoginResult Unavailable(string message) =>
        new(PlatformWebChatLoginOutcome.Unavailable, Message: message);

    public static PlatformWebChatLoginResult Failed(string message) =>
        new(PlatformWebChatLoginOutcome.Failed, Message: message);
}

public interface IPlatformWebChatLoginDialog
{
    Task<PlatformWebChatLoginResult> SignInAsync(PlatformWebChatLoginRequest request);
}
