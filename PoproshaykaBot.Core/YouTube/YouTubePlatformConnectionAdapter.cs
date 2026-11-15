using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using PoproshaykaBot.Core.Debugging;
using PoproshaykaBot.Core.Infrastructure.Hosting;
using PoproshaykaBot.Core.Platforms;
using PoproshaykaBot.Core.Platforms.Auth;
using PoproshaykaBot.Core.Settings.Stores;

namespace PoproshaykaBot.Core.YouTube;

internal sealed class YouTubePlatformConnectionAdapter(
    [FromKeyedServices(StreamingPlatform.YouTube)] IPlatformAuthService authService,
    YouTubeSettingsStore settingsStore,
    YouTubeAccountsStore accountsStore,
    YouTubeSessionContext sessionContext,
    PlatformChannelRegistry channelRegistry,
    ITargetChannelProvider targetChannelProvider,
    [FromKeyedServices(StreamingPlatform.YouTube)] PlatformHost platformHost,
    ILogger<YouTubePlatformConnectionAdapter> logger)
    : IPlatformConnectionAdapter
{
    public StreamingPlatform Platform => StreamingPlatform.YouTube;

    public bool IsEnabled => settingsStore.Load().Enabled && !targetChannelProvider.Current.IsDebugSession;

    public PlatformHost Host => platformHost;

    public async Task<string> BeginSessionAsync(IProgress<string> progress, CancellationToken cancellationToken)
    {
        progress.Report("Проверка токена бота YouTube...");

        var token = await authService.GetAccessTokenAsync(PlatformAuthRole.Bot, forceRefresh: false, cancellationToken)
            .ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();

        if (string.IsNullOrWhiteSpace(token))
        {
            logger.LogError("Не удалось получить токен доступа бота YouTube (токен пуст или отсутствует)");
            throw new InvalidOperationException("Не удалось получить токен бота YouTube. Авторизуйте аккаунт бота в настройках YouTube.");
        }

        var settings = settingsStore.Load();
        var broadcaster = accountsStore.Load(PlatformAuthRole.Broadcaster);
        var bot = accountsStore.Load(PlatformAuthRole.Bot);

        var channelLabel = ResolveChannelLabel(settings.Channel, broadcaster.ChannelTitle, broadcaster.ChannelId, bot.ChannelId);
        var targetChannelId = string.IsNullOrWhiteSpace(broadcaster.ChannelId) ? null : broadcaster.ChannelId.Trim();
        var botChannelId = string.IsNullOrWhiteSpace(bot.ChannelId) ? null : bot.ChannelId.Trim();
        var botChannelTitle = string.IsNullOrWhiteSpace(bot.ChannelTitle) ? null : bot.ChannelTitle.Trim();

        sessionContext.Begin(new(channelLabel, targetChannelId, botChannelId, botChannelTitle));
        channelRegistry.SetChannel(StreamingPlatform.YouTube, channelLabel);

        progress.Report("Запуск чтения чата YouTube...");
        logger.LogInformation("Запуск PlatformHost площадки {Platform} для канала {Channel}", Platform, channelLabel);
        return channelLabel;
    }

    public void EndSession()
    {
        sessionContext.Clear();
        channelRegistry.Clear(StreamingPlatform.YouTube);
    }

    private static string ResolveChannelLabel(
        string configuredChannel,
        string broadcasterTitle,
        string broadcasterId,
        string botId)
    {
        if (!string.IsNullOrWhiteSpace(configuredChannel))
        {
            return configuredChannel.Trim();
        }

        if (!string.IsNullOrWhiteSpace(broadcasterTitle))
        {
            return broadcasterTitle.Trim();
        }

        if (!string.IsNullOrWhiteSpace(broadcasterId))
        {
            return broadcasterId.Trim();
        }

        return string.IsNullOrWhiteSpace(botId) ? "youtube" : botId.Trim();
    }
}
