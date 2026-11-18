using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using PoproshaykaBot.Core.Debugging;
using PoproshaykaBot.Core.Platforms.Auth;
using PoproshaykaBot.Core.Settings.Stores;
using PoproshaykaBot.Core.Twitch;
using PoproshaykaBot.Core.Twitch.Helix;

namespace PoproshaykaBot.Core.Polls;

/// <summary>
/// Кеширует результат проверки «доступны ли голосования Twitch на канале».
/// Twitch отдаёт 403 на Polls API и на подписки <c>channel.poll.*</c>, если у канала
/// нет статуса Affiliate или Partner. Проверяем один раз на сессию.
/// </summary>
public class PollsAvailabilityService(
    [FromKeyedServices(TwitchEndpoints.HelixBotClient)]
    ITwitchHelixClient helix,
    IBroadcasterIdProvider broadcasterIdProvider,
    ITargetChannelProvider targetChannelProvider,
    AccountsStore accountsStore,
    ILogger<PollsAvailabilityService>? logger = null,
    TwitchChannelAuthorization? authorization = null)
{
    private readonly SemaphoreSlim _lock = new(1, 1);
    private AvailabilitySnapshot? _cached;

    private sealed record AvailabilitySnapshot(string Channel, bool HasBroadcasterAuth, PollsAvailability Value);

    public virtual async Task<PollsAvailability> GetAsync(CancellationToken cancellationToken)
    {
        var target = targetChannelProvider.Current;

        if (target.IsForeign)
        {
            logger?.LogInformation("PollsAvailability: отладка на чужом канале {Channel} – голосования недоступны", target.Login);
            return PollsAvailability.ForeignChannel;
        }

        var broadcasterAccount = authorization is null
            ? accountsStore.LoadBroadcaster()
            : authorization.GetAccount(PlatformAuthRole.Broadcaster);
        var hasBroadcasterAuth = broadcasterAccount is not null
            && (!string.IsNullOrWhiteSpace(broadcasterAccount.AccessToken)
                || !string.IsNullOrWhiteSpace(broadcasterAccount.RefreshToken));
        if (ReadCached(target.Login, hasBroadcasterAuth) is { } cached)
        {
            return cached;
        }

        await _lock.WaitAsync(cancellationToken);

        try
        {
            if (ReadCached(target.Login, hasBroadcasterAuth) is { } refreshed)
            {
                return refreshed;
            }

            var result = await ComputeAsync(hasBroadcasterAuth, cancellationToken);
            _cached = new(target.Login, hasBroadcasterAuth, result);
            return result;
        }
        finally
        {
            _lock.Release();
        }
    }

    public virtual void Reset()
    {
        _cached = null;
    }

    private PollsAvailability? ReadCached(string channel, bool hasBroadcasterAuth) =>
        _cached is { } cached && cached.Channel == channel && cached.HasBroadcasterAuth == hasBroadcasterAuth
            ? cached.Value
            : null;

    private async Task<PollsAvailability> ComputeAsync(bool hasBroadcasterAuth, CancellationToken cancellationToken)
    {
        try
        {
            if (!hasBroadcasterAuth)
            {
                logger?.LogInformation("PollsAvailability: токен стримера отсутствует – голосования недоступны");
                return PollsAvailability.NoBroadcasterToken;
            }

            var broadcasterId = await broadcasterIdProvider.GetAsync(cancellationToken);

            if (string.IsNullOrEmpty(broadcasterId))
            {
                return PollsAvailability.NoBroadcaster;
            }

            var users = await helix.GetUsersByIdsAsync([broadcasterId], cancellationToken);
            var broadcaster = users is [var first, ..] ? first : null;

            if (broadcaster is null)
            {
                return PollsAvailability.NoBroadcaster;
            }

            var tier = broadcaster.BroadcasterType ?? string.Empty;

            if (string.Equals(tier, "affiliate", StringComparison.OrdinalIgnoreCase)
                || string.Equals(tier, "partner", StringComparison.OrdinalIgnoreCase))
            {
                logger?.LogInformation("PollsAvailability: канал {Login} имеет статус '{Tier}' – голосования доступны",
                    broadcaster.Login, tier);

                return PollsAvailability.Available;
            }

            logger?.LogInformation("PollsAvailability: канал {Login} не Affiliate/Partner (tier='{Tier}') – голосования недоступны",
                broadcaster.Login, tier);

            return PollsAvailability.NotAffiliate;
        }
        catch (Exception ex)
        {
            logger?.LogWarning(ex, "PollsAvailability: не удалось проверить статус канала – считаем доступным, ошибки будут при вызове API");
            return PollsAvailability.Available;
        }
    }
}
