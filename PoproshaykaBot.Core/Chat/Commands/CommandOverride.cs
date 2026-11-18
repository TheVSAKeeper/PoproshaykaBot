using System.Text.Json.Serialization;

namespace PoproshaykaBot.Core.Chat.Commands;

public sealed class CommandOverride
{
    private Dictionary<string, bool>? _enabledByPlatform;

    public bool Enabled { get; set; } = true;

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public Dictionary<string, bool>? EnabledByPlatform
    {
        get => _enabledByPlatform;
        set
        {
            _enabledByPlatform = value is null ? null : new(StringComparer.OrdinalIgnoreCase);
            if (value is not null)
            {
                foreach (var (key, enabled) in value)
                {
                    _enabledByPlatform![key] = enabled;
                }
            }
        }
    }

    public void SetPlatformEnabled(Platforms.StreamingPlatform platform, bool? enabled)
    {
        var key = platform.ToString();
        if (enabled is { } value)
        {
            (_enabledByPlatform ??= new(StringComparer.OrdinalIgnoreCase))[key] = value;
        }
        else if (_enabledByPlatform is { } overrides && overrides.Remove(key) && overrides.Count == 0)
        {
            _enabledByPlatform = null;
        }
    }

    [JsonConverter(typeof(CommandResponseTargetJsonConverter))]
    public CommandResponseTarget? ResponseTarget { get; set; }

    public CommandAccessLevel? Access { get; set; }
}
