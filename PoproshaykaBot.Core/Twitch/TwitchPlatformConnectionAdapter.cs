using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using PoproshaykaBot.Core.Debugging;
using PoproshaykaBot.Core.Infrastructure.Hosting;
using PoproshaykaBot.Core.Platforms;
using PoproshaykaBot.Core.Twitch.Auth;

namespace PoproshaykaBot.Core.Twitch;

internal sealed class TwitchPlatformConnectionAdapter(
    ITwitchOAuthService tokenService,
    ITargetChannelProvider targetChannelProvider,
    [FromKeyedServices(StreamingPlatform.Twitch)] PlatformHost platformHost,
    ILogger<TwitchPlatformConnectionAdapter> logger)
    : IPlatformConnectionAdapter
{
    public StreamingPlatform Platform => StreamingPlatform.Twitch;

    public bool IsEnabled => true;

    public PlatformHost Host => platformHost;

    public async Task<string> BeginSessionAsync(IProgress<string> progress, CancellationToken cancellationToken)
    {
        progress.Report("Получение токена доступа...");
        logger.LogDebug("Запрос токена доступа Twitch");

        var accessToken = await tokenService.GetAccessTokenAsync(TwitchOAuthRole.Bot, cancellationToken).ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();

        if (string.IsNullOrWhiteSpace(accessToken))
        {
            logger.LogError("Не удалось получить токен доступа (токен пуст или null)");
            throw new InvalidOperationException("Токен бота отсутствует или недействителен. Авторизуйте бота в настройках Twitch (вкладка «Бот»).");
        }

        progress.Report("Запуск компонентов бота...");
        targetChannelProvider.BeginSession();
        var target = targetChannelProvider.Current;

        if (target.IsDebugSession)
        {
            progress.Report(target.IsSendingAllowed
                ? $"Отладка: подключение к каналу {target.Login}"
                : $"Отладка: подключение к каналу {target.Login} без отправки сообщений");
        }

        logger.LogInformation("Запуск PlatformHost площадки {Platform} для канала {Channel}", Platform, target.Login);
        return target.Login;
    }

    public void EndSession()
    {
        targetChannelProvider.EndSession();
    }
}
