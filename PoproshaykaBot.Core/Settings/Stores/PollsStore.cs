using Microsoft.Extensions.Logging;
using PoproshaykaBot.Core.Infrastructure;
using PoproshaykaBot.Core.Polls;

namespace PoproshaykaBot.Core.Settings.Stores;

public class PollsStore
{
    private readonly JsonStore<PollsSettings> _store;

    public PollsStore(ILogger<PollsStore>? logger = null, string? filePath = null, SettingsWriteGate? gate = null)
    {
        var path = filePath ?? AppPaths.SettingsFile("polls.json");
        _store = new(path, logger, describe: SettingsDescriber.Describe, gate: gate);

        if (logger?.IsEnabled(LogLevel.Debug) == true)
        {
            logger.LogDebug("PollsStore инициализирован из {FilePath} (профилей: {ProfileCount})",
                path,
                _store.Load().Profiles.Count);
        }
    }

    public virtual PollsSettings Load()
    {
        return _store.Load();
    }

    public virtual void Mutate(Action<PollsSettings> mutator)
    {
        _store.Mutate(mutator);
    }

    public virtual TResult Mutate<TResult>(Func<PollsSettings, TResult> mutator)
    {
        return _store.Mutate(mutator);
    }

    public virtual void Save(PollsSettings value)
    {
        _store.Save(value);
    }
}
