using Microsoft.Extensions.Logging;
using PoproshaykaBot.Core.Infrastructure;
using PoproshaykaBot.Core.Platforms;
using PoproshaykaBot.Core.Settings.Migrations.LegacyImport;
using System.Text.Json;

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
        return Normalize(_store.Load());
    }

    public bool VerifyPersisted() => _store.VerifyPersisted(static (current, persisted) =>
        string.Equals(JsonSerializer.Serialize(current, JsonStoreOptions.Default),
            JsonSerializer.Serialize(persisted, JsonStoreOptions.Default), StringComparison.Ordinal));

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
            settings.Platforms = normalized.Platforms;
        });

        Changed?.Invoke(this, EventArgs.Empty);
        return written;
    }

    public IReadOnlyList<ChannelGroupDefinition> GetGroups()
    {
        return Normalize(_store.Load()).Groups;
    }

    public PlatformChannelRoutingSettings GetPlatformSettings(StreamingPlatform platform)
    {
        var settings = Normalize(_store.Load());
        return settings.Platforms.FirstOrDefault(p => p.Platform == platform)
            ?? new PlatformChannelRoutingSettings { Platform = platform };
    }

    public IReadOnlyList<ChannelCatalogEntry> GetConfiguredChannels(StreamingPlatform platform)
    {
        return Normalize(_store.Load())
            .Channels
            .Where(c => c.Platform == platform && c.IsConfigured)
            .ToArray();
    }

    public IReadOnlyList<ChannelCatalogEntry> GetMonitoredChannels(StreamingPlatform platform)
    {
        var settings = Normalize(_store.Load());
        var routing = settings.Platforms.FirstOrDefault(p => p.Platform == platform);
        var activeId = routing?.ActiveChannelId;
        var preselectedId = routing?.PreselectedNextChannelId;

        return settings.Channels
            .Where(c => c.Platform == platform
                && c.IsConfigured
                && (c.MonitoringEnabled
                    || string.Equals(c.ChannelId, activeId, StringComparison.Ordinal)
                    || string.Equals(c.ChannelId, preselectedId, StringComparison.Ordinal)))
            .ToArray();
    }

    public ChannelCatalogEntry? GetActiveChannel(StreamingPlatform platform)
    {
        var settings = Normalize(_store.Load());
        var activeId = settings.Platforms.FirstOrDefault(p => p.Platform == platform)?.ActiveChannelId;
        var configured = settings.Channels.Where(c => c.Platform == platform && c.IsConfigured);
        return configured.FirstOrDefault(c => c.ChannelId == activeId) ?? configured.FirstOrDefault();
    }

    public ChannelCatalogEntry? FindEntry(ChannelKey key)
    {
        if (!key.IsValid)
        {
            return null;
        }

        return _store.Load().Channels.FirstOrDefault(c =>
            c.Platform == key.Platform
            && string.Equals(c.ChannelId, key.ChannelId, StringComparison.Ordinal));
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
            && string.Equals(existing.DisplayName, trimmed, StringComparison.OrdinalIgnoreCase)
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
        var configured = settings.Channels.Count(static c => c.IsConfigured);
        var monitored = settings.Channels.Count(static c => c.IsConfigured && c.MonitoringEnabled);
        var platforms = settings.Platforms.Count == 0
            ? "режимы по умолчанию"
            : string.Join(", ", settings.Platforms.Select(static p =>
                $"{p.Platform}: активный={SettingsDescriber.Value(p.ActiveChannelId)}, режим={p.SwitchMode}, следующий={SettingsDescriber.Value(p.PreselectedNextChannelId)}"));

        return $"каналов {settings.Channels.Count} (настроенных {configured}, мониторинг {monitored}), групп {settings.Groups.Count}; {platforms}";
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

                if (!string.IsNullOrWhiteSpace(entry.Handle))
                {
                    existing.Handle = entry.Handle.Trim();
                }

                if (!string.IsNullOrWhiteSpace(entry.ChannelUrl))
                {
                    existing.ChannelUrl = entry.ChannelUrl.Trim();
                }

                existing.IsConfigured |= entry.IsConfigured;
                existing.MonitoringEnabled |= entry.MonitoringEnabled;

                if (!string.IsNullOrWhiteSpace(entry.BroadcasterAccountId))
                {
                    existing.BroadcasterAccountId = entry.BroadcasterAccountId.Trim();
                }

                if (!string.IsNullOrWhiteSpace(entry.BroadcasterContextId))
                {
                    existing.BroadcasterContextId = entry.BroadcasterContextId.Trim();
                }

                if (!string.IsNullOrWhiteSpace(entry.BroadcasterVerifiedSubjectId))
                {
                    existing.BroadcasterVerifiedSubjectId = entry.BroadcasterVerifiedSubjectId.Trim();
                }

                if (!string.IsNullOrWhiteSpace(entry.BotContextId))
                {
                    existing.BotContextId = entry.BotContextId.Trim();
                }

                if (!string.IsNullOrWhiteSpace(entry.BotAccountId))
                {
                    existing.BotAccountId = entry.BotAccountId.Trim();
                }

                if (!string.IsNullOrWhiteSpace(entry.BotVerifiedSubjectId))
                {
                    existing.BotVerifiedSubjectId = entry.BotVerifiedSubjectId.Trim();
                }

                foreach (var param in entry.Parameters)
                {
                    if (!string.IsNullOrWhiteSpace(param.Key))
                    {
                        existing.Parameters[param.Key.Trim()] = param.Value ?? string.Empty;
                    }
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
                var parameters = new Dictionary<string, string>(StringComparer.Ordinal);
                foreach (var param in entry.Parameters)
                {
                    if (!string.IsNullOrWhiteSpace(param.Key))
                    {
                        parameters[param.Key.Trim()] = param.Value ?? string.Empty;
                    }
                }

                channels[key] = new()
                {
                    Platform = entry.Platform,
                    ChannelId = id,
                    DisplayName = entry.DisplayName?.Trim() ?? string.Empty,
                    Handle = entry.Handle?.Trim() ?? string.Empty,
                    ChannelUrl = entry.ChannelUrl?.Trim() ?? string.Empty,
                    KnownAliases = aliases,
                    IsConfigured = entry.IsConfigured,
                    MonitoringEnabled = entry.MonitoringEnabled,
                    BroadcasterContextId = NormalizeOptional(entry.BroadcasterContextId),
                    BroadcasterAccountId = NormalizeOptional(entry.BroadcasterAccountId),
                    BroadcasterVerifiedSubjectId = NormalizeOptional(entry.BroadcasterVerifiedSubjectId),
                    BotContextId = NormalizeOptional(entry.BotContextId),
                    BotAccountId = NormalizeOptional(entry.BotAccountId),
                    BotVerifiedSubjectId = NormalizeOptional(entry.BotVerifiedSubjectId),
                    Parameters = parameters,
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

        var platforms = new Dictionary<StreamingPlatform, PlatformChannelRoutingSettings>();
        foreach (var platformSettings in settings.Platforms)
        {
            if (!Enum.IsDefined(platformSettings.Platform))
            {
                continue;
            }

            var activeChannelId = NormalizeOptional(platformSettings.ActiveChannelId);
            var preselectedNext = NormalizeOptional(platformSettings.PreselectedNextChannelId);
            if (string.Equals(preselectedNext, activeChannelId, StringComparison.Ordinal))
            {
                preselectedNext = null;
            }

            var switchMode = Enum.IsDefined(platformSettings.SwitchMode)
                ? platformSettings.SwitchMode
                : ChannelSwitchMode.Manual;

            platforms[platformSettings.Platform] = new()
            {
                Platform = platformSettings.Platform,
                ActiveChannelId = activeChannelId,
                SwitchMode = switchMode,
                PreselectedNextChannelId = preselectedNext,
            };
        }

        return new()
        {
            Channels = [.. channels.Values],
            Groups = groups,
            Platforms = [.. platforms.Values.OrderBy(static p => p.Platform)],
        };
    }

    private static string? NormalizeOptional(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }
}
