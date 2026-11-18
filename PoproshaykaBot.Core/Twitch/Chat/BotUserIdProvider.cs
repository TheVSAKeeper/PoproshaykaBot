using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using PoproshaykaBot.Core.Infrastructure.Events;
using PoproshaykaBot.Core.Infrastructure.Events.Lifecycle;
using PoproshaykaBot.Core.Platforms;
using PoproshaykaBot.Core.Platforms.Auth;
using PoproshaykaBot.Core.Twitch.Helix;

namespace PoproshaykaBot.Core.Twitch.Chat;

public sealed class BotUserIdProvider : IBotUserIdProvider, IDisposable
{
    private readonly ITwitchHelixClient _helix;
    private readonly ILogger<BotUserIdProvider> _logger;
    private readonly IDisposable _lifecycleSubscription;
    private readonly SemaphoreSlim _lock = new(1, 1);
    private readonly Dictionary<string, UserInfo> _cachedByContext = new(StringComparer.Ordinal);
    private long _generation;

    public BotUserIdProvider(
        [FromKeyedServices(TwitchEndpoints.HelixBotClient)]
        ITwitchHelixClient helix,
        IEventBus eventBus,
        ILogger<BotUserIdProvider> logger)
    {
        _helix = helix;
        _logger = logger;
        _lifecycleSubscription = eventBus.SubscribePlatform<PlatformConnectionChanged>(StreamingPlatform.Twitch, OnPlatformConnectionChanged);
    }

    public async Task<string?> GetAsync(CancellationToken cancellationToken)
    {
        var user = await GetUserAsync(cancellationToken).ConfigureAwait(false);
        return user?.Id;
    }

    public async Task<UserInfo?> GetUserAsync(CancellationToken cancellationToken)
    {
        var contextId = AuthContextIds.Normalize(PlatformAuthRequestOptions.CurrentContextId);
        await _lock.WaitAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            if (_cachedByContext.TryGetValue(contextId, out var cached))
            {
                return cached;
            }

            var generation = Interlocked.Read(ref _generation);
            var user = await _helix.GetAuthenticatedUserAsync(cancellationToken).ConfigureAwait(false);

            if (user == null)
            {
                _logger.LogWarning("Bot user не получен через /users (контекст {ContextId}) – ответ пустой", contextId);
                return null;
            }

            if (Interlocked.Read(ref _generation) == generation)
            {
                _cachedByContext[contextId] = user;
            }

            _logger.LogInformation("Bot user получен через токен (контекст {ContextId}): {UserId} ({Login})", contextId, user.Id, user.Login);
            return user;
        }
        finally
        {
            _lock.Release();
        }
    }

    public void Dispose()
    {
        _lifecycleSubscription.Dispose();
        _lock.Dispose();
    }

    private void OnPlatformConnectionChanged(PlatformConnectionChanged evt)
    {
        if (evt.Phase is BotLifecyclePhase.Disconnecting
            or BotLifecyclePhase.Disconnected
            or BotLifecyclePhase.Cancelled
            or BotLifecyclePhase.Failed)
        {
            Interlocked.Increment(ref _generation);
            if (_lock.Wait(0))
            {
                try
                {
                    _cachedByContext.Clear();
                }
                finally
                {
                    _lock.Release();
                }
            }
        }
    }
}
