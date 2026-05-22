using Microsoft.Extensions.Logging;
using Microsoft.Web.WebView2.Core;

namespace PoproshaykaBot.WinForms.Infrastructure;

public static class WebView2EnvironmentFactory
{
    private const int MaxAttempts = 10;
    private const int RetryDelayMs = 600;

    public static async Task<CoreWebView2Environment> CreateAsync(string userDataFolder, ILogger logger)
    {
        ArgumentException.ThrowIfNullOrEmpty(userDataFolder);
        ArgumentNullException.ThrowIfNull(logger);

        Directory.CreateDirectory(userDataFolder);

        for (var attempt = 1; attempt < MaxAttempts; attempt++)
        {
            try
            {
                return await CoreWebView2Environment.CreateAsync(null, userDataFolder).ConfigureAwait(true);
            }
            catch (Exception exception) when (exception is not WebView2RuntimeNotFoundException)
            {
                if (logger.IsEnabled(LogLevel.Debug))
                {
                    logger.LogDebug(exception,
                        "Папка данных WebView2 {Folder} недоступна (попытка {Attempt}/{Max}), повтор через {Delay} мс — вероятно, занята завершающимся процессом после обновления",
                        userDataFolder, attempt, MaxAttempts, RetryDelayMs);
                }

                await Task.Delay(RetryDelayMs).ConfigureAwait(true);
            }
        }

        return await CoreWebView2Environment.CreateAsync(null, userDataFolder).ConfigureAwait(true);
    }
}
