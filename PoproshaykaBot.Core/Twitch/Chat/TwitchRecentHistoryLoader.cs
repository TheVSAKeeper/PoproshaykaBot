using Microsoft.Extensions.Logging;
using PoproshaykaBot.Core.Chat;
using PoproshaykaBot.Core.Chat.History;
using PoproshaykaBot.Core.Debugging;
using PoproshaykaBot.Core.Infrastructure.Events;
using PoproshaykaBot.Core.Infrastructure.Events.Chat;
using PoproshaykaBot.Core.Platforms;
using PoproshaykaBot.Core.Platforms.Auth;
using PoproshaykaBot.Core.Settings;

namespace PoproshaykaBot.Core.Twitch.Chat;

public sealed class TwitchRecentHistoryLoader : IEventHandler<ChatIngestionStarted>, IEventSubscriber, IDisposable
{
    private readonly ITwitchRecentMessagesClient _client;
    private readonly SettingsManager _settingsManager;
    private readonly IChatHistorySettingsProvider _historySettingsProvider;
    private readonly PlatformChannelRegistry _channelRegistry;
    private readonly ITargetChannelProvider _targetChannelProvider;
    private readonly IBotUserIdProvider _botUserIdProvider;
    private readonly IEventBus _eventBus;
    private readonly ILogger<TwitchRecentHistoryLoader> _logger;
    private readonly IDisposable _subscription;
    private readonly CancellationTokenSource _disposeCts = new();
    private bool _disposed;

    public TwitchRecentHistoryLoader(
        ITwitchRecentMessagesClient client,
        SettingsManager settingsManager,
        IChatHistorySettingsProvider historySettingsProvider,
        PlatformChannelRegistry channelRegistry,
        ITargetChannelProvider targetChannelProvider,
        IBotUserIdProvider botUserIdProvider,
        IEventBus eventBus,
        ILogger<TwitchRecentHistoryLoader> logger)
    {
        _client = client;
        _settingsManager = settingsManager;
        _historySettingsProvider = historySettingsProvider;
        _channelRegistry = channelRegistry;
        _targetChannelProvider = targetChannelProvider;
        _botUserIdProvider = botUserIdProvider;
        _eventBus = eventBus;
        _logger = logger;
        _subscription = eventBus.SubscribePlatform<ChatIngestionStarted>(StreamingPlatform.Twitch, this);
    }

    public Task HandleAsync(ChatIngestionStarted @event, CancellationToken cancellationToken)
    {
        if (_disposed || @event.Platform != StreamingPlatform.Twitch)
        {
            return Task.CompletedTask;
        }

        if (!_settingsManager.Current.Twitch.FetchRecentMessages)
        {
            return Task.CompletedTask;
        }

        if (!TryResolveConsistentTarget(@event.ChannelId, out var channelLogin, out var channelId, out var botContextId))
        {
            return Task.CompletedTask;
        }

        var historySettings = _historySettingsProvider.GetCurrent();
        var limit = Math.Min(TwitchRecentMessagesClient.DefaultLimit, Math.Max(1, historySettings.HistoryMaxItems));

        _eventBus.ContinueAfterPublish(() => LoadAndPublishAsync(channelLogin, channelId, botContextId, limit));
        return Task.CompletedTask;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _subscription.Dispose();
        _disposeCts.Cancel();
        _disposeCts.Dispose();
    }

    private bool TryResolveConsistentTarget(
        string? eventChannelId,
        out string channelLogin,
        out string channelId,
        out string? botContextId)
    {
        channelLogin = string.Empty;
        channelId = string.Empty;
        botContextId = null;

        var normalizedEventId = string.IsNullOrWhiteSpace(eventChannelId) ? null : eventChannelId.Trim();
        var binding = _channelRegistry.GetBinding(StreamingPlatform.Twitch);
        if (binding is not null)
        {
            var bindingId = binding.Key.ChannelId?.Trim() ?? string.Empty;
            var bindingLogin = binding.ChannelLabel?.Trim() ?? string.Empty;
            if (!string.IsNullOrEmpty(bindingId)
                && !string.IsNullOrEmpty(bindingLogin)
                && (normalizedEventId is null || string.Equals(bindingId, normalizedEventId, StringComparison.Ordinal)))
            {
                channelId = bindingId;
                channelLogin = bindingLogin;
                botContextId = binding.BotContextId;
                return true;
            }

            if (normalizedEventId is not null && !string.IsNullOrEmpty(bindingId))
            {
                return false;
            }
        }

        if (normalizedEventId is null)
        {
            return false;
        }

        var targetLogin = _targetChannelProvider.Current.Login?.Trim() ?? string.Empty;
        if (!string.IsNullOrEmpty(targetLogin))
        {
            channelId = normalizedEventId;
            channelLogin = targetLogin;
            botContextId = binding?.BotContextId;
            return true;
        }

        return false;
    }

    private async Task LoadAndPublishAsync(string channelLogin, string channelId, string? botContextId, int limit)
    {
        try
        {
            var token = _disposeCts.Token;
            string? botUserId = null;
            try
            {
                using var _ = PlatformAuthRequestOptions.UseContext(botContextId);
                botUserId = await _botUserIdProvider.GetAsync(token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Не удалось получить userId бота перед загрузкой недавней истории Twitch");
            }

            var messages = await _client.FetchAsync(channelLogin, channelId, botUserId, limit, token).ConfigureAwait(false);
            if (messages.Count == 0 || token.IsCancellationRequested)
            {
                return;
            }

            var currentBinding = _channelRegistry.GetBinding(StreamingPlatform.Twitch);
            if (currentBinding is not null
                && !string.IsNullOrWhiteSpace(currentBinding.Key.ChannelId)
                && !string.Equals(currentBinding.Key.ChannelId, channelId, StringComparison.Ordinal))
            {
                return;
            }

            var matchingMessages = messages
                .Where(item => string.IsNullOrWhiteSpace(item.Message.ChannelId)
                    || string.Equals(item.Message.ChannelId, channelId, StringComparison.Ordinal))
                .ToList();

            if (matchingMessages.Count == 0)
            {
                return;
            }

            var scope = ChatHistoryScope.ForTwitch(channelId, channelLogin);
            await _eventBus.PublishAsync(
                new RawChatHistoryReceived(scope, matchingMessages, AllowExternalHistory: true),
                token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (_disposeCts.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            _logger.LogWarning(exception, "Ошибка фоновой загрузки недавней истории Twitch для канала {Channel}", channelLogin);
        }
    }
}
