using Microsoft.Extensions.Logging;
using PoproshaykaBot.Core.Chat.Commands;
using PoproshaykaBot.Core.Infrastructure;

namespace PoproshaykaBot.Core.Settings.Stores;

public sealed class CommandSettingsStore
{
    private readonly JsonStore<CommandSettings> _store;

    public CommandSettingsStore(ILogger<CommandSettingsStore>? logger = null, string? filePath = null)
    {
        _store = new(filePath ?? AppPaths.SettingsFile("commands.json"), logger, describe: SettingsDescriber.Describe);
    }

    public CommandSettings Load()
    {
        return _store.Load();
    }

    public void Mutate(Action<CommandSettings> mutator)
    {
        _store.Mutate(mutator);
    }
}
