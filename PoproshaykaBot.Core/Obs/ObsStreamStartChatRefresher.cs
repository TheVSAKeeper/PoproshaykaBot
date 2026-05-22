using Microsoft.Extensions.Logging;
using PoproshaykaBot.Core.Infrastructure.Hosting;
using PoproshaykaBot.Core.Settings.Stores;
using System.Text.Json;

namespace PoproshaykaBot.Core.Obs;

internal sealed class ObsStreamStartChatRefresher(
    IObsWebSocketClient client,
    ObsIntegrationStore store,
    ObsIntegrationService integration,
    ILogger<ObsStreamStartChatRefresher> logger)
    : IAppLifetimeComponent
{
    private const string StreamStateChangedEvent = "StreamStateChanged";

    private bool _lastActive;

    public string Name => "OBS авто-refresh чата при старте эфира";

    public int StartOrder => 12;

    public Task StartAsync(CancellationToken cancellationToken)
    {
        client.EventReceived += OnObsEventReceived;
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        client.EventReceived -= OnObsEventReceived;
        return Task.CompletedTask;
    }

    private void OnObsEventReceived(object? sender, ObsWebSocketEventArgs e)
    {
        if (!string.Equals(e.EventType, StreamStateChangedEvent, StringComparison.Ordinal))
        {
            return;
        }

        if (e.EventData is not { } data
            || !data.TryGetProperty("outputActive", out var activeElement)
            || activeElement.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
        {
            return;
        }

        var active = activeElement.GetBoolean();
        var wasActive = _lastActive;
        _lastActive = active;

        if (!active || wasActive)
        {
            return;
        }

        var settings = store.Load();
        if (!settings.Enabled || !settings.RefreshChatSourcesOnStreamStart)
        {
            return;
        }

        _ = RefreshSafeAsync();
    }

    private async Task RefreshSafeAsync()
    {
        try
        {
            var settings = store.Load();
            var refreshed = await integration
                .RefreshConfiguredChatSourcesAsync(settings, CancellationToken.None)
                .ConfigureAwait(false);

            if (refreshed > 0)
            {
                logger.LogInformation("Старт эфира OBS: обновлено чат-источников {Count}", refreshed);
            }
        }
        catch (Exception exception)
        {
            logger.LogWarning(exception,
                "Не удалось обновить чат-источники OBS при старте эфира");
        }
    }
}
