using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using PoproshaykaBot.WinForms.Broadcast.Profiles;
using PoproshaykaBot.WinForms.Infrastructure.Hosting;
using PoproshaykaBot.WinForms.Infrastructure.Persistence;
using PoproshaykaBot.WinForms.Settings;
using PoproshaykaBot.WinForms.Twitch;
using PoproshaykaBot.WinForms.Twitch.Helix;
using System.Text.Encodings.Web;
using System.Text.Json;

namespace PoproshaykaBot.WinForms.Polls;

public sealed class PollHistoryStore(
    SettingsManager settingsManager,
    [FromKeyedServices(TwitchEndpoints.HelixBroadcasterClient)]
    ITwitchHelixClient helix,
    IBroadcasterIdProvider broadcasterIdProvider,
    ILogger<PollHistoryStore> logger,
    string? filePath = null)
    : IHostedComponent
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    private readonly string _filePath = filePath
                                        ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                                            "PoproshaykaBot",
                                            "polls-history.json");

    private readonly List<PollHistoryEntry> _entries = [];
    private readonly object _sync = new();
    private CancellationTokenSource? _backgroundCts;
    private bool _loaded;

    public string Name => "История голосований";

    public int StartOrder => 120;

    public static PollHistoryEntry BuildEntry(PollSnapshot snapshot, PollChoiceSnapshot? winner, bool winnerIsTie)
    {
        var ended = snapshot.EndedAtUtc ?? snapshot.EndsAtUtc;

        return new()
        {
            PollId = snapshot.PollId,
            SourceProfileId = snapshot.SourceProfileId,
            Title = snapshot.Title,
            FinalChoices = snapshot.Choices
                .Select(c => new PollHistoryChoice
                {
                    ChoiceId = c.ChoiceId,
                    Title = c.Title,
                    Votes = c.Votes,
                    ChannelPointsVotes = c.ChannelPointsVotes,
                    BitsVotes = c.BitsVotes,
                })
                .ToList(),
            StartedAtUtc = snapshot.StartedAtUtc,
            EndedAtUtc = ended,
            FinalStatus = snapshot.Status,
            WinnerChoiceId = winner?.ChoiceId,
            WinnerIsTie = winnerIsTie,
        };
    }

    public IReadOnlyList<PollHistoryEntry> GetAll()
    {
        EnsureLoaded();

        lock (_sync)
        {
            return _entries.ToList();
        }
    }

    public bool TryAdd(PollHistoryEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);
        EnsureLoaded();

        lock (_sync)
        {
            if (_entries.Any(e => string.Equals(e.PollId, entry.PollId, StringComparison.Ordinal)))
            {
                return false;
            }

            _entries.Add(entry);
            TruncateToMax();
            PersistNoThrow();
            return true;
        }
    }

    public async Task<int> BackfillAsync(CancellationToken cancellationToken)
    {
        EnsureLoaded();

        try
        {
            var broadcasterId = await broadcasterIdProvider.GetAsync(cancellationToken);

            if (string.IsNullOrEmpty(broadcasterId))
            {
                return 0;
            }

            var polls = await helix.GetPollsAsync(broadcasterId, null, 20, cancellationToken);
            var added = 0;

            foreach (var poll in polls)
            {
                if (poll.EndedAt is null)
                {
                    continue;
                }

                var snapshot = PollEventSubMapper.FromHelix(poll, null);

                if (snapshot.Status == PollSnapshotStatus.Active)
                {
                    continue;
                }

                var (winner, isTie) = PollEventSubMapper.DetectWinner(snapshot);
                var entry = BuildEntry(snapshot, winner, isTie);

                if (TryAdd(entry))
                {
                    added++;
                }
            }

            return added;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "PollHistoryStore: бэкфилл не выполнен");
            return 0;
        }
    }

    public Task StartAsync(IProgress<string> progress, CancellationToken cancellationToken)
    {
        EnsureLoaded();

        _backgroundCts?.Dispose();
        _backgroundCts = new();
        var token = _backgroundCts.Token;
        _ = Task.Run(() => BackfillAsync(token), CancellationToken.None);

        return Task.CompletedTask;
    }

    public Task StopAsync(IProgress<string> progress, CancellationToken cancellationToken)
    {
        if (_backgroundCts is not null)
        {
            _backgroundCts.Cancel();
            _backgroundCts.Dispose();
            _backgroundCts = null;
        }

        return Task.CompletedTask;
    }

    private void EnsureLoaded()
    {
        lock (_sync)
        {
            if (_loaded)
            {
                return;
            }

            _loaded = true;

            try
            {
                if (!File.Exists(_filePath))
                {
                    return;
                }

                var json = File.ReadAllText(_filePath);

                if (string.IsNullOrWhiteSpace(json))
                {
                    return;
                }

                var file = JsonSerializer.Deserialize<HistoryFile>(json, JsonOptions);

                if (file?.Entries is { Count: > 0 })
                {
                    _entries.AddRange(file.Entries);
                }
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "PollHistoryStore: ошибка чтения {FilePath}", _filePath);
            }
        }
    }

    private void TruncateToMax()
    {
        var max = Math.Max(1, settingsManager.Current.Twitch.Polls.HistoryMaxItems);

        if (_entries.Count <= max)
        {
            return;
        }

        _entries.RemoveRange(0, _entries.Count - max);
    }

    private void PersistNoThrow()
    {
        try
        {
            var payload = new HistoryFile(1, _entries);
            var json = JsonSerializer.Serialize(payload, JsonOptions);
            AtomicFile.Save(_filePath, json, logger);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "PollHistoryStore: не удалось сохранить историю");
        }
    }

    private sealed record HistoryFile(int Version, List<PollHistoryEntry> Entries);
}
