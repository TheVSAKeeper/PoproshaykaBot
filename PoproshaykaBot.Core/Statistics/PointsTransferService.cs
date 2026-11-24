using PoproshaykaBot.Core.Infrastructure.Events;
using PoproshaykaBot.Core.Infrastructure.Events.Moderation;

namespace PoproshaykaBot.Core.Statistics;

public sealed class PointsTransferService(
    IUserStatisticsRepository repository,
    IEventBus eventBus,
    StatisticsAutoSaver? autoSaver = null,
    UserStatisticsLoader? userLoader = null)
{
    public PointsTransferPreview Preview(PointsTransferRequest request)
    {
        return repository.PreviewTransfer(request);
    }

    public async Task<PointsTransferExecutionResult> TransferAsync(
        PointsTransferRequest request,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var result = repository.ExecuteTransfer(request);
        if (result.TransferredCount == 0)
        {
            return new(result, PersistedToDisk: true);
        }

        await eventBus.PublishAsync(
                new UserPointsTransferred(request.Source, request.Target, request.Mode, result),
                cancellationToken)
            .ConfigureAwait(false);

        if (autoSaver is null)
        {
            return new(result, PersistedToDisk: false);
        }

        try
        {
            bool persisted;
            if (userLoader is not null)
            {
                persisted = await autoSaver.SaveUnitNowAsync(userLoader.PersistenceUnit, cancellationToken)
                    .ConfigureAwait(false);
            }
            else
            {
                await autoSaver.SaveNowAsync(cancellationToken).ConfigureAwait(false);
                persisted = true;
            }

            return new(result, PersistedToDisk: persisted);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return new(result, PersistedToDisk: false, SaveError: ex.Message);
        }
    }
}
