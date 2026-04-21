using Microsoft.Extensions.Logging;
using PoproshaykaBot.WinForms.Settings;
using PoproshaykaBot.WinForms.Streaming;

namespace PoproshaykaBot.WinForms.Infrastructure.Hosting.Components;

internal sealed class StreamMonitoringHostedComponent(
    StreamStatusManager manager,
    SettingsManager settingsManager,
    ILogger<StreamMonitoringHostedComponent> logger)
    : IHostedComponent
{
    public string Name => "Инициализация мониторинга стрима...";

    public int StartOrder => 300;

    public async Task StartAsync(IProgress<string> progress, CancellationToken cancellationToken)
    {
        var settings = settingsManager.Current.Twitch;

        logger.LogDebug("Инициализация мониторинга стрима для канала {Channel}", settings.Channel);

        if (string.IsNullOrEmpty(settings.ClientId))
        {
            logger.LogWarning("Мониторинг стрима недоступен: не настроен Client ID");
            progress.Report("Client ID не установлен. Мониторинг стрима недоступен.");
            return;
        }

        if (string.IsNullOrEmpty(settings.AccessToken))
        {
            logger.LogWarning("Мониторинг стрима недоступен: не настроен Access Token");
            progress.Report("Access Token не установлен. Мониторинг стрима недоступен.");
            return;
        }

        try
        {
            await manager.StartMonitoringAsync(settings.Channel).ConfigureAwait(false);
            logger.LogInformation("Мониторинг стрима успешно запущен для канала {Channel}", settings.Channel);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Ошибка инициализации мониторинга стрима для канала {Channel}", settings.Channel);
            progress.Report($"Ошибка инициализации мониторинга стрима: {ex.Message}");
        }
    }

    public Task StopAsync(IProgress<string> progress, CancellationToken cancellationToken)
    {
        return manager.StopMonitoringAsync(cancellationToken);
    }
}
