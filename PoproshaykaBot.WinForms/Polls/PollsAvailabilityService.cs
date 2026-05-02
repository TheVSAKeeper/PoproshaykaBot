using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using PoproshaykaBot.WinForms.Broadcast.Profiles;
using PoproshaykaBot.WinForms.Settings.Stores;
using PoproshaykaBot.WinForms.Twitch;
using PoproshaykaBot.WinForms.Twitch.Helix;

namespace PoproshaykaBot.WinForms.Polls;

/// <summary>
/// Кеширует результат проверки «доступны ли голосования Twitch на канале».
/// Twitch отдаёт 403 на Polls API и на подписки <c>channel.poll.*</c>, если у канала
/// нет статуса Affiliate или Partner. Проверяем один раз на сессию.
/// </summary>
public class PollsAvailabilityService(
    [FromKeyedServices(TwitchEndpoints.HelixBotClient)]
    ITwitchHelixClient helix,
    IBroadcasterIdProvider broadcasterIdProvider,
    AccountsStore accountsStore,
    ILogger<PollsAvailabilityService>? logger = null)
{
    private readonly SemaphoreSlim _lock = new(1, 1);
    private PollsAvailability? _cached;

    public virtual async Task<PollsAvailability> GetAsync(CancellationToken cancellationToken)
    {
        if (_cached is not null)
        {
            return _cached;
        }

        await _lock.WaitAsync(cancellationToken);

        try
        {
            if (_cached is not null)
            {
                return _cached;
            }

            _cached = await ComputeAsync(cancellationToken);
            return _cached;
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

    private async Task<PollsAvailability> ComputeAsync(CancellationToken cancellationToken)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(accountsStore.LoadBroadcaster().AccessToken))
            {
                logger?.LogInformation("PollsAvailability: токен стримера отсутствует — голосования недоступны");
                return PollsAvailability.NoBroadcasterToken;
            }

            var broadcasterId = await broadcasterIdProvider.GetAsync(cancellationToken);

            if (string.IsNullOrEmpty(broadcasterId))
            {
                return PollsAvailability.NoBroadcaster;
            }

            var users = await helix.GetUsersByIdsAsync([broadcasterId], cancellationToken);
            var broadcaster = users.FirstOrDefault();

            if (broadcaster is null)
            {
                return PollsAvailability.NoBroadcaster;
            }

            var tier = broadcaster.BroadcasterType ?? string.Empty;

            if (string.Equals(tier, "affiliate", StringComparison.OrdinalIgnoreCase)
                || string.Equals(tier, "partner", StringComparison.OrdinalIgnoreCase))
            {
                logger?.LogInformation("PollsAvailability: канал {Login} имеет статус '{Tier}' — голосования доступны",
                    broadcaster.Login, tier);

                return PollsAvailability.Available;
            }

            logger?.LogInformation("PollsAvailability: канал {Login} не Affiliate/Partner (tier='{Tier}') — голосования недоступны",
                broadcaster.Login, tier);

            return PollsAvailability.NotAffiliate;
        }
        catch (Exception ex)
        {
            logger?.LogWarning(ex, "PollsAvailability: не удалось проверить статус канала — считаем доступным, ошибки будут при вызове API");
            return PollsAvailability.Available;
        }
    }
}
