using System.Text.Json;
using Microsoft.Extensions.Logging;
using PoproshaykaBot.Core.Chat.Commands;
using PoproshaykaBot.Core.Infrastructure;

namespace PoproshaykaBot.Core.Settings.Stores;

public sealed class CommandSettingsStore
{
    private readonly JsonStore<CommandSettings> _store;

    public CommandSettingsStore(
        ILogger<CommandSettingsStore>? logger = null,
        string? filePath = null)
    {
        _store = new(filePath ?? AppPaths.SettingsFile(SettingsFileNames.CommandSettingsFileName),
            logger,
            parser: json => Parse(json, logger),
            describe: Describe);
    }

    public static string Describe(CommandSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);

        var overrides = settings.Commands.Values.Where(x => x is not null).ToList();
        var disabled = overrides.Count(x => !x.Enabled);
        var retargeted = overrides.Count(x => x.ResponseTarget is not null);
        var restricted = overrides.Count(x => x.Access is not null);

        return $"цель ответа по умолчанию {settings.DefaultResponseTarget}, переопределений {overrides.Count} "
            + $"(выключено {disabled}, со своей целью {retargeted}, со своими правами {restricted})";
    }

    public CommandSettings Load()
    {
        return _store.Load();
    }

    public bool Mutate(Action<CommandSettings> mutator)
    {
        return _store.Mutate(mutator);
    }

    private static CommandSettings? Parse(string json, ILogger? logger)
    {
        var settings = JsonSerializer.Deserialize<CommandSettings>(json, JsonStoreOptions.Default);

        if (settings is null)
        {
            return null;
        }

        foreach (var (from, to) in CommandRenames.MoveRenamed(settings.Commands))
        {
            logger?.LogInformation("Настройки команды {From} перенесены к её новому имени {To}", from, to);
        }

        return settings;
    }
}
