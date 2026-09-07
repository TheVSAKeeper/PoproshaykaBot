using Microsoft.Extensions.Logging;
using PoproshaykaBot.Core.Infrastructure;
using PoproshaykaBot.Core.Settings.Debugging;

namespace PoproshaykaBot.Core.Settings.Stores;

public sealed class DebugChannelStore
{
    private readonly JsonStore<DebugChannelSettings> _store;

    public DebugChannelStore(ILogger<DebugChannelStore>? logger = null, string? filePath = null)
    {
        var path = filePath ?? AppPaths.SettingsFile("debug-channel.json");
        _store = new(path, logger, describe: SettingsDescriber.Describe);

        logger?.LogDebug("DebugChannelStore инициализирован из {FilePath}", path);
    }

    public DebugChannelSettings Load()
    {
        return _store.Load();
    }

    public void Save(DebugChannelSettings value)
    {
        _store.Save(value);
    }
}
