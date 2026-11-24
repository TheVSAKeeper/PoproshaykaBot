using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using PoproshaykaBot.Core.Debugging;
using PoproshaykaBot.Core.Platforms.Auth;
using PoproshaykaBot.Core.Settings;
using PoproshaykaBot.Core.Settings.Stores;
using PoproshaykaBot.Core.Twitch;
using PoproshaykaBot.Core.YouTube;

namespace PoproshaykaBot.Core.Platforms;

public sealed class ChannelCatalogReconciler
{
    private readonly ChannelCatalogStore _catalogStore;
    private readonly IReadOnlyList<IPlatformChannelSeedProvider> _seedProviders;
    private readonly Dictionary<StreamingPlatform, IPlatformChannelSeedProvider> _providersByPlatform;
    private readonly ILogger<ChannelCatalogReconciler> _logger;

    [ActivatorUtilitiesConstructor]
    public ChannelCatalogReconciler(
        ChannelCatalogStore catalogStore,
        IEnumerable<IPlatformChannelSeedProvider> seedProviders,
        ILogger<ChannelCatalogReconciler> logger)
    {
        ArgumentNullException.ThrowIfNull(catalogStore);
        ArgumentNullException.ThrowIfNull(seedProviders);
        ArgumentNullException.ThrowIfNull(logger);

        _catalogStore = catalogStore;
        _seedProviders = seedProviders.ToArray();
        _providersByPlatform = _seedProviders.ToDictionary(static provider => provider.Platform);
        _logger = logger;
    }

    public ChannelCatalogReconciler(
        ChannelCatalogStore catalogStore,
        SettingsManager settingsManager,
        AccountsStore twitchAccountsStore,
        YouTubeSettingsStore youtubeSettingsStore,
        YouTubeAccountsStore youtubeAccountsStore,
        ITargetChannelProvider targetChannelProvider,
        ILogger<ChannelCatalogReconciler> logger)
        : this(
            catalogStore,
            [
                new TwitchChannelSeedProvider(settingsManager, twitchAccountsStore, targetChannelProvider),
                new YouTubeChannelSeedProvider(youtubeSettingsStore, youtubeAccountsStore),
            ],
            logger)
    {
    }

    public void ReconcileStartup()
    {
        foreach (var provider in _seedProviders)
        {
            ReconcilePlatform(provider.Platform);
        }
    }

    public void ReconcilePlatform(StreamingPlatform platform)
    {
        if (!_providersByPlatform.TryGetValue(platform, out var provider))
        {
            return;
        }

        var existingConfigured = _catalogStore.GetConfiguredChannels(platform);
        if (existingConfigured.Count > 0)
        {
            return;
        }

        if (provider.TryCreateSeed() is not { } seed)
        {
            return;
        }

        SeedChannel(
            seed.Platform,
            seed.ChannelId,
            seed.DisplayName,
            seed.Handle,
            seed.Url,
            seed.MonitoringEnabled,
            seed.BroadcasterSubjectId,
            seed.BotSubjectId);
    }

    public void MirrorActiveChannelToLegacySettings(ChannelCatalogEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);

        if (_providersByPlatform.TryGetValue(entry.Platform, out var provider))
        {
            provider.MirrorActiveChannel(entry);
        }
    }

    public ChannelCatalogEntry? ResolveActiveEntry(StreamingPlatform platform)
    {
        var configured = _catalogStore.GetConfiguredChannels(platform);
        if (configured.Count == 0)
        {
            return null;
        }

        var routing = _catalogStore.GetPlatformSettings(platform);
        if (!string.IsNullOrWhiteSpace(routing.ActiveChannelId))
        {
            var match = configured.FirstOrDefault(c => string.Equals(c.ChannelId, routing.ActiveChannelId, StringComparison.Ordinal));
            if (match is not null)
            {
                return match;
            }
        }

        return configured[0];
    }

    private void SeedChannel(
        StreamingPlatform platform,
        string channelId,
        string displayName,
        string handle,
        string url,
        bool monitoringEnabled,
        string? broadcasterSubjectId,
        string? botSubjectId)
    {
        _catalogStore.Mutate(settings =>
        {
            var entry = settings.Channels.FirstOrDefault(c =>
                c.Platform == platform && string.Equals(c.ChannelId, channelId, StringComparison.Ordinal));

            if (entry is null)
            {
                entry = new()
                {
                    Platform = platform,
                    ChannelId = channelId,
                    DisplayName = displayName,
                    Handle = handle,
                    ChannelUrl = url,
                    IsConfigured = true,
                    MonitoringEnabled = monitoringEnabled,
                    BroadcasterContextId = AuthContextIds.Default,
                    BroadcasterVerifiedSubjectId = string.IsNullOrWhiteSpace(broadcasterSubjectId) ? null : broadcasterSubjectId.Trim(),
                    BotContextId = AuthContextIds.Default,
                    BotVerifiedSubjectId = string.IsNullOrWhiteSpace(botSubjectId) ? null : botSubjectId.Trim(),
                };
                if (!string.IsNullOrWhiteSpace(displayName))
                {
                    entry.KnownAliases.Add(displayName);
                }

                settings.Channels.Add(entry);
            }
            else
            {
                entry.IsConfigured = true;
                entry.MonitoringEnabled |= monitoringEnabled;
                if (string.IsNullOrWhiteSpace(entry.Handle))
                {
                    entry.Handle = handle;
                }

                if (string.IsNullOrWhiteSpace(entry.ChannelUrl))
                {
                    entry.ChannelUrl = url;
                }

                entry.BroadcasterContextId ??= AuthContextIds.Default;
                entry.BotContextId ??= AuthContextIds.Default;
            }

            var platformSettings = settings.Platforms.FirstOrDefault(p => p.Platform == platform);
            if (platformSettings is null)
            {
                settings.Platforms.Add(new()
                {
                    Platform = platform,
                    ActiveChannelId = channelId,
                    SwitchMode = ChannelSwitchMode.Manual,
                });
            }
            else if (string.IsNullOrWhiteSpace(platformSettings.ActiveChannelId))
            {
                platformSettings.ActiveChannelId = channelId;
            }
        });

        _logger.LogInformation("Каталог каналов: перенесён канал {Platform} {ChannelId} ({DisplayName}) из прежних настроек",
            platform,
            channelId,
            displayName);
    }
}
