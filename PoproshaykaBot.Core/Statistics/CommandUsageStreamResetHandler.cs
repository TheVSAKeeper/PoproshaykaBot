using Microsoft.Extensions.Logging;
using PoproshaykaBot.Core.Debugging;
using PoproshaykaBot.Core.Infrastructure.Events;
using PoproshaykaBot.Core.Infrastructure.Events.Streaming;

namespace PoproshaykaBot.Core.Statistics;

public sealed class CommandUsageStreamResetHandler : IEventHandler<BroadcastStarted>, IEventSubscriber, IDisposable
{
    private readonly CommandUsageRepository _repository;
    private readonly ITargetChannelProvider? _targetChannelProvider;
    private readonly ILogger<CommandUsageStreamResetHandler> _logger;
    private readonly IDisposable _subscription;

    public CommandUsageStreamResetHandler(
        CommandUsageRepository repository,
        IEventBus eventBus,
        ILogger<CommandUsageStreamResetHandler> logger,
        ITargetChannelProvider? targetChannelProvider = null)
    {
        _repository = repository;
        _targetChannelProvider = targetChannelProvider;
        _logger = logger;
        _subscription = eventBus.Subscribe(this);
    }

    public Task HandleAsync(BroadcastStarted @event, CancellationToken cancellationToken)
    {
        if (_targetChannelProvider is not null && !_targetChannelProvider.Current.RecordsUserData)
        {
            _logger.LogDebug("Сброс счётчиков команд пропущен – режим отладки без записи данных");
            return Task.CompletedTask;
        }

        var broadcastId = @event.BroadcastId.ToString("D");

        if (_repository.ResetStreamCounters(broadcastId))
        {
            _logger.LogInformation("Счётчики команд за эфир обнулены (эфир {BroadcastId})", broadcastId);
        }

        return Task.CompletedTask;
    }

    public void Dispose()
    {
        _subscription.Dispose();
    }
}
