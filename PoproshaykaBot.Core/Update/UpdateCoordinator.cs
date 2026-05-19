using Microsoft.Extensions.Logging;
using PoproshaykaBot.Core.Infrastructure.Events;
using PoproshaykaBot.Core.Infrastructure.Events.Update;
using PoproshaykaBot.Core.Settings.Stores;

namespace PoproshaykaBot.Core.Update;

public sealed class UpdateCoordinator(
    UpdateChecker checker,
    IUpdateInstaller installer,
    UpdateStore store,
    IEventBus eventBus,
    IUpdateEnvironment environment,
    ILogger<UpdateCoordinator> logger)
    : IUpdateCoordinator
{
    private readonly object _syncLock = new();

    private UpdateCandidate? _latestCandidate;

    public UpdateKind Kind => environment.Kind;

    public bool IsUpdatable => environment.Kind switch
    {
        UpdateKind.Portable => true,
        UpdateKind.FrameworkDependent => store.Load().AllowFrameworkDependentUpdate,
        _ => false,
    };

    public string DefaultRepositorySlug => environment.RepositorySlug;

    public Version CurrentVersion => environment.CurrentVersion;

    public UpdateCandidate? LatestCandidate
    {
        get
        {
            lock (_syncLock)
            {
                return _latestCandidate;
            }
        }
    }

    public bool HasPreparedUpdate => installer.ReadPending() is not null;

    public async Task<UpdateCandidate?> CheckNowAsync(CancellationToken cancellationToken)
    {
        var settings = store.Load();

        UpdateCandidate? candidate;

        try
        {
            candidate = await checker.CheckAsync(settings.SkippedVersion, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            logger.LogWarning(exception, "Не удалось проверить наличие обновлений");
            return null;
        }

        settings.LastCheckUtc = DateTimeOffset.UtcNow;
        store.Save(settings);

        if (candidate is null)
        {
            return null;
        }

        lock (_syncLock)
        {
            _latestCandidate = candidate;
        }

        await eventBus
            .PublishAsync(new UpdateAvailable(candidate.Version.ToString(), candidate.NotesUrl), cancellationToken)
            .ConfigureAwait(false);

        return candidate;
    }

    public Task PrepareAsync(UpdateCandidate candidate, IProgress<int>? progress, CancellationToken cancellationToken)
    {
        return installer.PrepareAsync(candidate, progress, cancellationToken);
    }

    public void SkipVersion(string version)
    {
        ArgumentException.ThrowIfNullOrEmpty(version);

        var settings = store.Load();
        settings.SkippedVersion = version;
        store.Save(settings);

        lock (_syncLock)
        {
            if (_latestCandidate is not null
                && string.Equals(_latestCandidate.Version.ToString(), version, StringComparison.Ordinal))
            {
                _latestCandidate = null;
            }
        }

        logger.LogInformation("Версия {Version} помечена как пропущенная", version);
    }
}
