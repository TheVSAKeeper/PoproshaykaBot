using Microsoft.Extensions.Logging;
using PoproshaykaBot.Core.Debugging;
using PoproshaykaBot.Core.Platforms.Auth;
using PoproshaykaBot.Core.Settings;
using PoproshaykaBot.Core.Settings.Stores;
using PoproshaykaBot.Core.YouTube.Auth;
using PoproshaykaBot.Core.YouTube.Streaming;

namespace PoproshaykaBot.Core.Platforms;

public sealed class ChannelCatalogReconciler(
    ChannelCatalogStore catalogStore,
    SettingsManager settingsManager,
    AccountsStore twitchAccountsStore,
    YouTubeSettingsStore youtubeSettingsStore,
    YouTubeAccountsStore youtubeAccountsStore,
    ITargetChannelProvider targetChannelProvider,
    ILogger<ChannelCatalogReconciler> logger)
{
    public void ReconcileStartup()
    {
        ReconcileTwitchFromLegacy();
        ReconcileYouTubeFromLegacy();
    }

    public void ReconcileTwitchFromLegacy()
    {
        var twitchChannel = settingsManager.Current.Twitch.Channel?.Trim();
        if (string.IsNullOrWhiteSpace(twitchChannel))
        {
            return;
        }

        var existingConfigured = catalogStore.GetConfiguredChannels(StreamingPlatform.Twitch);
        if (existingConfigured.Count > 0)
        {
            return;
        }

        var broadcaster = twitchAccountsStore.Load(PlatformAuthRole.Broadcaster);
        if (string.IsNullOrWhiteSpace(broadcaster.UserId)
            || !string.Equals(broadcaster.Login, twitchChannel, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        SeedChannel(
            StreamingPlatform.Twitch,
            broadcaster.UserId.Trim(),
            broadcaster.Login,
            broadcaster.Login,
            $"https://twitch.tv/{broadcaster.Login}",
            monitoringEnabled: true,
            broadcasterSubjectId: broadcaster.UserId.Trim(),
            botSubjectId: twitchAccountsStore.Load(PlatformAuthRole.Bot).UserId);
    }

    public void ReconcileYouTubeFromLegacy()
    {
        var existingConfigured = catalogStore.GetConfiguredChannels(StreamingPlatform.YouTube);
        if (existingConfigured.Count > 0)
        {
            return;
        }

        var ytSettings = youtubeSettingsStore.Load();
        var broadcaster = youtubeAccountsStore.Load(PlatformAuthRole.Broadcaster);
        var rawInput = ytSettings.Channel?.Trim();
        string? channelId = null;
        var handle = string.Empty;

        if (!string.IsNullOrWhiteSpace(rawInput) && YouTubeTargetChannelResolver.TryExtractDirectChannelId(rawInput, out var directId))
        {
            channelId = directId;
            handle = rawInput;
        }
        else if (string.IsNullOrWhiteSpace(rawInput) && !string.IsNullOrWhiteSpace(broadcaster.ChannelId))
        {
            channelId = broadcaster.ChannelId.Trim();
            handle = broadcaster.ChannelTitle ?? string.Empty;
        }

        if (string.IsNullOrWhiteSpace(channelId))
        {
            return;
        }

        var displayName = !string.IsNullOrWhiteSpace(broadcaster.ChannelTitle)
            && string.Equals(broadcaster.ChannelId, channelId, StringComparison.Ordinal)
            ? broadcaster.ChannelTitle
            : (!string.IsNullOrWhiteSpace(handle) ? handle : channelId);

        SeedChannel(
            StreamingPlatform.YouTube,
            channelId,
            displayName,
            handle,
            $"https://youtube.com/channel/{channelId}",
            monitoringEnabled: ytSettings.Enabled,
            broadcasterSubjectId: string.Equals(broadcaster.ChannelId, channelId, StringComparison.Ordinal) ? channelId : null,
            botSubjectId: youtubeAccountsStore.Load(PlatformAuthRole.Bot).ChannelId);
    }

    public void MirrorActiveChannelToLegacySettings(ChannelCatalogEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);

        if (entry.Platform == StreamingPlatform.Twitch)
        {
            if (targetChannelProvider.Current.IsDebugSession)
            {
                return;
            }

            var targetLogin = !string.IsNullOrWhiteSpace(entry.Handle) ? entry.Handle : entry.DisplayName;
            if (!string.IsNullOrWhiteSpace(targetLogin)
                && !string.Equals(settingsManager.Current.Twitch.Channel, targetLogin, StringComparison.OrdinalIgnoreCase))
            {
                settingsManager.Mutate(settings => settings.Twitch.Channel = targetLogin);
            }

            return;
        }

        if (entry.Platform == StreamingPlatform.YouTube)
        {
            var current = youtubeSettingsStore.Load();
            if (!string.Equals(current.Channel, entry.ChannelId, StringComparison.Ordinal))
            {
                youtubeSettingsStore.Mutate(settings => settings.Channel = entry.ChannelId);
            }
        }
    }

    public ChannelCatalogEntry? ResolveActiveEntry(StreamingPlatform platform)
    {
        var configured = catalogStore.GetConfiguredChannels(platform);
        if (configured.Count == 0)
        {
            return null;
        }

        var routing = catalogStore.GetPlatformSettings(platform);
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
        catalogStore.Mutate(settings =>
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

        logger.LogInformation("Каталог каналов: перенесён канал {Platform} {ChannelId} ({DisplayName}) из прежних настроек",
            platform,
            channelId,
            displayName);
    }
}
