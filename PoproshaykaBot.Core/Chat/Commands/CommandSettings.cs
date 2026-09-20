using System.Text.Json.Serialization;

namespace PoproshaykaBot.Core.Chat.Commands;

public sealed class CommandSettings
{
    public const CommandResponseTarget KnownTargets = CommandResponseTarget.Chat | CommandResponseTarget.Overlay;

    public CommandResponseTarget DefaultResponseTarget { get; set; } = KnownTargets;

    [JsonObjectCreationHandling(JsonObjectCreationHandling.Populate)]
    public Dictionary<string, CommandOverride> Commands { get; } = new(StringComparer.OrdinalIgnoreCase);

    public bool IsEnabled(string canonical)
    {
        return !TryGetOverride(canonical, out var commandOverride) || commandOverride.Enabled;
    }

    public CommandResponseTarget ResolveResponseTarget(string canonical)
    {
        if (TryGetOverride(canonical, out var commandOverride) && commandOverride.ResponseTarget is { } target)
        {
            return target & KnownTargets;
        }

        return DefaultResponseTarget & KnownTargets;
    }

    private bool TryGetOverride(string canonical, out CommandOverride commandOverride)
    {
        if (string.IsNullOrWhiteSpace(canonical))
        {
            commandOverride = null!;
            return false;
        }

        return Commands.TryGetValue(canonical, out commandOverride!) && commandOverride is not null;
    }
}
