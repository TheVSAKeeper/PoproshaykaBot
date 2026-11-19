using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using PoproshaykaBot.Core.Debugging;
using PoproshaykaBot.Core.Infrastructure.Hosting;
using PoproshaykaBot.Core.Platforms;
using PoproshaykaBot.Core.Platforms.Auth;
using PoproshaykaBot.Core.Settings;
using PoproshaykaBot.Core.Settings.Stores;
using PoproshaykaBot.Core.Streaming;
using PoproshaykaBot.Core.YouTube.Auth;
using PoproshaykaBot.Core.YouTube.Streaming;

namespace PoproshaykaBot.Core.YouTube;

internal sealed class YouTubePlatformConnectionAdapter(
    [FromKeyedServices(StreamingPlatform.YouTube)] IPlatformAuthService authService,
    YouTubeSettingsStore settingsStore,
    YouTubeAccountsStore accountsStore,
    YouTubeSessionContext sessionContext,
    PlatformChannelRegistry channelRegistry,
    ITargetChannelProvider targetChannelProvider,
    [FromKeyedServices(StreamingPlatform.YouTube)] PlatformHost platformHost,
    ILogger<YouTubePlatformConnectionAdapter> logger,
    ChannelCatalogStore? channelCatalogStore = null,
    YouTubeTargetChannelResolver? channelResolver = null)
    : IPlatformConnectionAdapter
{
    public StreamingPlatform Platform => StreamingPlatform.YouTube;

    public bool IsEnabled => settingsStore.Load().Enabled && !targetChannelProvider.Current.IsDebugSession;

    public PlatformHost Host => platformHost;

    public async Task<string> BeginSessionAsync(IProgress<string> progress, CancellationToken cancellationToken)
    {
        progress.Report("Проверка токена бота YouTube...");

        var activeEntry = channelCatalogStore?.GetActiveChannel(StreamingPlatform.YouTube);
        var botContextId = AuthContextIds.Normalize(activeEntry?.BotContextId);
        var broadcasterContextId = AuthContextIds.Normalize(activeEntry?.BroadcasterContextId);

        var token = AuthContextIds.IsDefault(botContextId)
            ? await authService.GetAccessTokenAsync(PlatformAuthRole.Bot, forceRefresh: false, cancellationToken).ConfigureAwait(false)
            : await authService.GetAccessTokenAsync(PlatformAuthRole.Bot, botContextId, forceRefresh: false, cancellationToken).ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();

        if (string.IsNullOrWhiteSpace(token))
        {
            logger.LogError("Не удалось получить токен доступа бота YouTube (контекст {ContextId}, токен пуст или отсутствует)", botContextId);
            throw new InvalidOperationException("Не удалось получить токен бота YouTube. Авторизуйте аккаунт бота в настройках YouTube.");
        }

        var settings = settingsStore.Load();
        var broadcaster = accountsStore.Load(PlatformAuthRole.Broadcaster, broadcasterContextId);
        var bot = accountsStore.Load(PlatformAuthRole.Bot, botContextId);
        VerifyBotAssignment(activeEntry, bot, botContextId);

        var channelLabel = activeEntry is not null
            ? ResolveCatalogLabel(activeEntry)
            : ResolveChannelLabel(settings.Channel, broadcaster.ChannelTitle, broadcaster.ChannelId, bot.ChannelId);
        var targetChannelId = activeEntry is not null
            ? activeEntry.ChannelId
            : await ResolveLegacyTargetChannelIdAsync(settings, broadcaster, botContextId, cancellationToken).ConfigureAwait(false);
        var botChannelId = string.IsNullOrWhiteSpace(bot.ChannelId) ? null : bot.ChannelId.Trim();
        var botChannelTitle = string.IsNullOrWhiteSpace(bot.ChannelTitle) ? null : bot.ChannelTitle.Trim();

        sessionContext.Begin(new(channelLabel, targetChannelId, botChannelId, botChannelTitle, botContextId, broadcasterContextId, 0));
        if (!string.IsNullOrWhiteSpace(targetChannelId))
        {
            channelRegistry.SetBinding(new(
                new ChannelKey(StreamingPlatform.YouTube, targetChannelId),
                channelLabel,
                botContextId,
                broadcasterContextId,
                0));
        }
        else
        {
            channelRegistry.SetChannel(StreamingPlatform.YouTube, channelLabel);
        }

        progress.Report("Запуск чтения чата YouTube...");
        logger.LogInformation("Запуск PlatformHost площадки {Platform} для канала {Channel}", Platform, channelLabel);
        return channelLabel;
    }

    public void EndSession()
    {
        sessionContext.Clear();
        channelRegistry.Clear(StreamingPlatform.YouTube);
    }

    public async Task<string> RetargetSessionAsync(
        PlatformChatSessionTarget target,
        IProgress<string> progress,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(target);

        var botContextId = AuthContextIds.Normalize(target.Channel.BotContextId);
        var broadcasterContextId = AuthContextIds.Normalize(target.Channel.BroadcasterContextId);

        var token = AuthContextIds.IsDefault(botContextId)
            ? await authService.GetAccessTokenAsync(PlatformAuthRole.Bot, forceRefresh: false, cancellationToken).ConfigureAwait(false)
            : await authService.GetAccessTokenAsync(PlatformAuthRole.Bot, botContextId, forceRefresh: false, cancellationToken).ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();

        if (string.IsNullOrWhiteSpace(token))
        {
            throw new InvalidOperationException("Не удалось получить токен бота YouTube для целевого канала.");
        }

        var bot = accountsStore.Load(PlatformAuthRole.Bot, botContextId);
        VerifyBotAssignment(target.Channel, bot, botContextId);

        var botChannelId = string.IsNullOrWhiteSpace(bot.ChannelId) ? null : bot.ChannelId.Trim();
        var botChannelTitle = string.IsNullOrWhiteSpace(bot.ChannelTitle) ? null : bot.ChannelTitle.Trim();
        var channelLabel = ResolveCatalogLabel(target.Channel);

        sessionContext.Begin(new(
            channelLabel,
            target.Channel.ChannelId,
            botChannelId,
            botChannelTitle,
            botContextId,
            broadcasterContextId,
            target.SwitchGeneration));

        channelRegistry.SetBinding(new(
            target.Channel.Key,
            channelLabel,
            botContextId,
            broadcasterContextId,
            target.SwitchGeneration));

        logger.LogInformation("Перенацеливание чата YouTube на канал {Channel} ({ChannelId})", channelLabel, target.Channel.ChannelId);
        return channelLabel;
    }

    private async Task<string?> ResolveLegacyTargetChannelIdAsync(
        YouTubeSettings settings,
        YouTubeAccountSettings broadcaster,
        string botContextId,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(settings.Channel))
        {
            return string.IsNullOrWhiteSpace(broadcaster.ChannelId) ? null : broadcaster.ChannelId.Trim();
        }

        if (YouTubeTargetChannelResolver.TryExtractDirectChannelId(settings.Channel, out var directId))
        {
            return directId;
        }

        if (channelResolver is null)
        {
            throw new InvalidOperationException($"Не удалось определить идентификатор канала YouTube «{settings.Channel.Trim()}».");
        }

        string? resolved;
        try
        {
            resolved = await channelResolver
                .ResolveAsync(settings, PlatformAuthRole.Bot, botContextId, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "Не удалось определить целевой канал YouTube {Channel} при запуске сессии", settings.Channel);
            throw new InvalidOperationException(
                $"Не удалось определить канал YouTube «{settings.Channel.Trim()}»: {StreamingErrorMessages.SafeMessage(ex)}",
                ex);
        }

        if (string.IsNullOrWhiteSpace(resolved))
        {
            throw new InvalidOperationException(
                $"Не удалось найти канал YouTube «{settings.Channel.Trim()}». Проверьте адрес канала и авторизацию бота.");
        }

        return resolved.Trim();
    }

    private static void VerifyBotAssignment(ChannelCatalogEntry? entry, YouTubeAccountSettings bot, string contextId)
    {
        if (entry is null)
        {
            return;
        }

        if (entry.GetAccountAssignment(PlatformAuthRole.Bot) is not { IsVerified: true } assignment)
        {
            throw new InvalidOperationException("Подтвердите доступ аккаунта бота к каналу YouTube в настройках площадки.");
        }

        if (!string.Equals(bot.ChannelId, assignment.Account.AccountId, StringComparison.Ordinal))
        {
            throw new InvalidOperationException($"Контекст бота ({contextId}) перепривязан к другому каналу YouTube – требуется повторная проверка доступа.");
        }
    }

    private static string ResolveCatalogLabel(ChannelCatalogEntry entry)
    {
        if (!string.IsNullOrWhiteSpace(entry.DisplayName))
        {
            return entry.DisplayName.Trim();
        }

        if (!string.IsNullOrWhiteSpace(entry.Handle))
        {
            return entry.Handle.Trim();
        }

        return entry.ChannelId;
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
