using Microsoft.Extensions.Logging;
using PoproshaykaBot.Core.Infrastructure;
using PoproshaykaBot.Core.Platforms;
using PoproshaykaBot.Core.Settings.Migrations.LegacyImport;

namespace PoproshaykaBot.Core.Settings.Stores;

public sealed class ChannelCatalogStore
{
    private readonly JsonStore<ChannelsSettings> _store;

    public ChannelCatalogStore(
        ILogger<ChannelCatalogStore>? logger = null,
        string? filePath = null,
        SettingsWriteGate? gate = null)
    {
        var path = filePath ?? AppPaths.SettingsFile(LegacyDataCatalog.ChannelsFileName);
        _store = new(path, logger, describe: Describe, gate: gate);
    }

    public event EventHandler? Changed;

    public ChannelsSettings Load()
    {
        return _store.Load();
    }

    public bool Save(ChannelsSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);

        var normalized = Normalize(settings);
        var written = _store.Save(normalized);
        Changed?.Invoke(this, EventArgs.Empty);
        return written;
    }

    public bool Mutate(Action<ChannelsSettings> mutator)
    {
        ArgumentNullException.ThrowIfNull(mutator);

        var written = _store.Mutate(settings =>
        {
            mutator(settings);
            var normalized = Normalize(settings);
            settings.Channels = normalized.Channels;
            settings.Groups = normalized.Groups;
        });

        Changed?.Invoke(this, EventArgs.Empty);
        return written;
    }

    public IReadOnlyList<ChannelGroupDefinition> GetGroups()
    {
        return Normalize(_store.Load()).Groups;
    }

    public ChannelGroupDefinition? FindGroup(string idOrName)
    {
        if (string.IsNullOrWhiteSpace(idOrName))
        {
            return null;
        }

        var trimmed = idOrName.Trim();
        var groups = GetGroups();
        return groups.FirstOrDefault(g => string.Equals(g.Id, trimmed, StringComparison.OrdinalIgnoreCase))
            ?? groups.FirstOrDefault(g => string.Equals(g.Name, trimmed, StringComparison.OrdinalIgnoreCase));
    }

    public static IReadOnlySet<ChannelKey> DeduplicateMembers(IEnumerable<ChannelGroupMember> members)
    {
        var set = new HashSet<ChannelKey>();
        foreach (var member in members)
        {
            if (Enum.IsDefined(member.Platform) && !string.IsNullOrWhiteSpace(member.ChannelId))
            {
                set.Add(new ChannelKey(member.Platform, member.ChannelId.Trim()));
            }
        }

        return set;
    }

    public string? ResolveDisplayName(ChannelKey key)
    {
        var entry = _store.Load().Channels.FirstOrDefault(c =>
            c.Platform == key.Platform
            && string.Equals(c.ChannelId, key.ChannelId, StringComparison.Ordinal));

        return string.IsNullOrWhiteSpace(entry?.DisplayName) ? null : entry.DisplayName;
    }

    public void EnsureChannelKnown(ChannelKey key, string? displayName)
    {
        if (string.IsNullOrWhiteSpace(displayName))
        {
            return;
        }

        var trimmed = displayName.Trim();
        var current = _store.Load();
        var existing = current.Channels.FirstOrDefault(c =>
            c.Platform == key.Platform
            && string.Equals(c.ChannelId, key.ChannelId, StringComparison.Ordinal));

        if (existing is not null
            && string.Equals(existing.DisplayName, trimmed, StringComparison.Ordinal)
            && (existing.KnownAliases.Contains(trimmed, StringComparer.OrdinalIgnoreCase)
                || string.Equals(existing.DisplayName, trimmed, StringComparison.OrdinalIgnoreCase)))
        {
            return;
        }

        Mutate(settings =>
        {
            var entry = settings.Channels.FirstOrDefault(c =>
                c.Platform == key.Platform
                && string.Equals(c.ChannelId, key.ChannelId, StringComparison.Ordinal));

            if (entry is null)
            {
                settings.Channels.Add(new()
                {
                    Platform = key.Platform,
                    ChannelId = key.ChannelId,
                    DisplayName = trimmed,
                    KnownAliases = [trimmed],
                });
                return;
            }

            if (!string.IsNullOrWhiteSpace(entry.DisplayName)
                && !string.Equals(entry.DisplayName, trimmed, StringComparison.OrdinalIgnoreCase)
                && !entry.KnownAliases.Contains(entry.DisplayName, StringComparer.OrdinalIgnoreCase))
            {
                entry.KnownAliases.Add(entry.DisplayName);
            }

            entry.DisplayName = trimmed;
            if (!entry.KnownAliases.Contains(trimmed, StringComparer.OrdinalIgnoreCase))
            {
                entry.KnownAliases.Add(trimmed);
            }
        });
    }

    public static string Describe(ChannelsSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        return $"каналов {settings.Channels.Count}, групп {settings.Groups.Count}";
    }

    private static ChannelsSettings Normalize(ChannelsSettings settings)
    {
        var channels = new Dictionary<ChannelKey, ChannelCatalogEntry>();
        foreach (var entry in settings.Channels)
        {
            if (!Enum.IsDefined(entry.Platform) || string.IsNullOrWhiteSpace(entry.ChannelId))
            {
                continue;
            }

            var id = entry.ChannelId.Trim();
            var key = new ChannelKey(entry.Platform, id);
            var aliases = entry.KnownAliases
                .Where(static a => !string.IsNullOrWhiteSpace(a))
                .Select(static a => a.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (channels.TryGetValue(key, out var existing))
            {
                if (!string.IsNullOrWhiteSpace(entry.DisplayName))
                {
                    existing.DisplayName = entry.DisplayName.Trim();
                }

                foreach (var alias in aliases)
                {
                    if (!existing.KnownAliases.Contains(alias, StringComparer.OrdinalIgnoreCase))
                    {
                        existing.KnownAliases.Add(alias);
                    }
                }
            }
            else
            {
                channels[key] = new()
                {
                    Platform = entry.Platform,
                    ChannelId = id,
                    DisplayName = entry.DisplayName.Trim(),
                    KnownAliases = aliases,
                };
            }
        }

        var groups = new List<ChannelGroupDefinition>();
        var seenGroupIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var group in settings.Groups)
        {
            if (string.IsNullOrWhiteSpace(group.Name))
            {
                continue;
            }

            var id = string.IsNullOrWhiteSpace(group.Id)
                ? Guid.NewGuid().ToString("N")
                : group.Id.Trim();

            if (!seenGroupIds.Add(id))
            {
                continue;
            }

            var members = DeduplicateMembers(group.Channels)
                .OrderBy(static k => k.Platform)
                .ThenBy(static k => k.ChannelId, StringComparer.Ordinal)
                .Select(static k => new ChannelGroupMember
                {
                    Platform = k.Platform,
                    ChannelId = k.ChannelId,
                })
                .ToList();

            groups.Add(new()
            {
                Id = id,
                Name = group.Name.Trim(),
                Channels = members,
            });
        }

        return new()
        {
            Channels = [.. channels.Values],
            Groups = groups,
        };
    }
}
