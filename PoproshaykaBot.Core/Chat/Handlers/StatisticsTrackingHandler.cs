using PoproshaykaBot.Core.Infrastructure.Events;
using PoproshaykaBot.Core.Infrastructure.Events.Chat;
using PoproshaykaBot.Core.Statistics;

namespace PoproshaykaBot.Core.Chat.Handlers;

public sealed class StatisticsTrackingHandler : IEventHandler<ChatMessageReceived>, IEventSubscriber, IDisposable
{
    private readonly IUserStatisticsRepository _userStatistics;
    private readonly IBotStatisticsRepository _botStatistics;
    private readonly IDisposable _subscription;

    public StatisticsTrackingHandler(
        IUserStatisticsRepository userStatistics,
        IBotStatisticsRepository botStatistics,
        IEventBus eventBus)
    {
        _userStatistics = userStatistics;
        _botStatistics = botStatistics;
        _subscription = eventBus.Subscribe(this);
    }

    public Task HandleAsync(ChatMessageReceived @event, CancellationToken cancellationToken)
    {
        if (@event.IsBot)
        {
            return Task.CompletedTask;
        }

        _userStatistics.TrackMessage(@event.UserId, @event.Username);
        _botStatistics.IncrementMessagesProcessed();
        return Task.CompletedTask;
    }

    public void Dispose()
    {
        _subscription.Dispose();
    }
}
