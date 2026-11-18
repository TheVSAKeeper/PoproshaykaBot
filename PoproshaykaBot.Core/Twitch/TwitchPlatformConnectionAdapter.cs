using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using PoproshaykaBot.Core.Debugging;
using PoproshaykaBot.Core.Infrastructure.Hosting;
using PoproshaykaBot.Core.Platforms;
using PoproshaykaBot.Core.Platforms.Auth;
using PoproshaykaBot.Core.Settings;
using PoproshaykaBot.Core.Settings.Stores;
using PoproshaykaBot.Core.Twitch.Auth;
using PoproshaykaBot.Core.Twitch.Chat;

namespace PoproshaykaBot.Core.Twitch;

internal sealed class TwitchPlatformConnectionAdapter(
    ITwitchOAuthService tokenService,
    ITargetChannelProvider targetChannelProvider,
    [FromKeyedServices(StreamingPlatform.Twitch)] PlatformHost platformHost,
    ILogger<TwitchPlatformConnectionAdapter> logger,
    ChannelCatalogStore? channelCatalogStore = null,
    PlatformChannelRegistry? channelRegistry = null,
    ChatIngestionService? chatIngestionService = null,
    AccountsStore? accountsStore = null)
    : IPlatformConnectionAdapter
{
    public StreamingPlatform Platform => StreamingPlatform.Twitch;

    public bool IsEnabled => true;

    public PlatformHost Host => platformHost;

    public async Task<string> BeginSessionAsync(IProgress<string> progress, CancellationToken cancellationToken)
    {
        progress.Report("Получение токена доступа...");
        logger.LogDebug("Запрос токена доступа Twitch");

        targetChannelProvider.BeginSession();
        var target = targetChannelProvider.Current;

        var activeEntry = !target.IsDebugSession
            ? channelCatalogStore?.GetActiveChannel(StreamingPlatform.Twitch)
            : null;

        var botContextId = AuthContextIds.Normalize(activeEntry?.BotContextId);
        var accessToken = AuthContextIds.IsDefault(botContextId)
            ? await tokenService.GetAccessTokenAsync(PlatformAuthRole.Bot, cancellationToken).ConfigureAwait(false)
            : await tokenService.GetAccessTokenAsync(PlatformAuthRole.Bot, botContextId, cancellationToken).ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();

        if (string.IsNullOrWhiteSpace(accessToken))
        {
            targetChannelProvider.EndSession();
            logger.LogError("Не удалось получить токен доступа (токен пуст или null)");
            throw new InvalidOperationException("Токен бота отсутствует или недействителен. Авторизуйте бота в настройках Twitch (вкладка «Бот»).");
        }

        VerifyBotIdentity(activeEntry, botContextId);

        if (activeEntry is not null && channelRegistry is not null)
        {
            var label = ResolveChannelLabel(activeEntry);
            targetChannelProvider.SetSessionLogin(label);
            channelRegistry.SetBinding(new(
                activeEntry.Key,
                label,
                botContextId,
                AuthContextIds.Normalize(activeEntry.BroadcasterContextId),
                Generation: 0));
            target = targetChannelProvider.Current;
        }

        progress.Report("Запуск компонентов бота...");

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
        channelRegistry?.Clear(StreamingPlatform.Twitch);
    }

    public async Task<string> RetargetSessionAsync(
        PlatformChatSessionTarget target,
        IProgress<string> progress,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(target);

        if (targetChannelProvider.Current.IsDebugSession)
        {
            return targetChannelProvider.Current.Login;
        }

        var botContextId = AuthContextIds.Normalize(target.Channel.BotContextId);
        var broadcasterContextId = AuthContextIds.Normalize(target.Channel.BroadcasterContextId);
        var accessToken = AuthContextIds.IsDefault(botContextId)
            ? await tokenService.GetAccessTokenAsync(PlatformAuthRole.Bot, cancellationToken).ConfigureAwait(false)
            : await tokenService.GetAccessTokenAsync(PlatformAuthRole.Bot, botContextId, cancellationToken).ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();

        if (string.IsNullOrWhiteSpace(accessToken))
        {
            throw new InvalidOperationException("Токен бота Twitch для целевого канала отсутствует или недействителен.");
        }

        VerifyBotIdentity(target.Channel, botContextId);

        var previousBotContextId = channelRegistry?.GetBinding(StreamingPlatform.Twitch)?.BotContextId;
        var label = ResolveChannelLabel(target.Channel);

        targetChannelProvider.SetSessionLogin(label);
        channelRegistry?.SetBinding(new(
            target.Channel.Key,
            label,
            botContextId,
            broadcasterContextId,
            target.SwitchGeneration));

        var botContextChanged = !string.IsNullOrEmpty(previousBotContextId)
            && !string.Equals(previousBotContextId, botContextId, StringComparison.Ordinal);

        if (botContextChanged && platformHost.IsRunning)
        {
            await platformHost.StopAsync(progress, cancellationToken).ConfigureAwait(false);
            using var _ = PlatformAuthRequestOptions.UseContext(botContextId);
            await platformHost.StartAsync(progress, cancellationToken).ConfigureAwait(false);
        }
        else if (chatIngestionService is not null)
        {
            await chatIngestionService.RetargetChannelAsync(target.Channel.Key, botContextId, cancellationToken).ConfigureAwait(false);
        }

        logger.LogInformation("Перенацеливание чата Twitch на канал {Channel} ({ChannelId})", label, target.Channel.ChannelId);
        return label;
    }

    private void VerifyBotIdentity(ChannelCatalogEntry? entry, string botContextId)
    {
        if (entry is null)
        {
            return;
        }

        if (entry.GetAccountAssignment(PlatformAuthRole.Bot) is not { IsVerified: true } assignment)
        {
            throw new InvalidOperationException("Подтвердите доступ аккаунта бота к каналу Twitch в настройках площадки.");
        }

        if (accountsStore is null)
        {
            return;
        }

        var account = accountsStore.Load(PlatformAuthRole.Bot, botContextId);
        if (!string.Equals(account.UserId, assignment.Account.AccountId, StringComparison.Ordinal))
        {
            throw new InvalidOperationException($"Контекст бота ({botContextId}) перепривязан к другому аккаунту Twitch – требуется повторная проверка доступа.");
        }
    }

    internal static string ResolveChannelLabel(ChannelCatalogEntry entry)
    {
        if (!string.IsNullOrWhiteSpace(entry.Handle))
        {
            return entry.Handle.TrimStart('@');
        }

        if (!string.IsNullOrWhiteSpace(entry.DisplayName))
        {
            return entry.DisplayName;
        }

        return entry.ChannelId;
    }
}
