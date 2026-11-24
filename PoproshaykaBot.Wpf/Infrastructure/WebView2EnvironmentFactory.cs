using Microsoft.Extensions.Logging;
using Microsoft.Web.WebView2.Core;
using System.IO;

namespace PoproshaykaBot.Wpf.Infrastructure;

public static class WebView2EnvironmentFactory
{
    internal const string DefaultBrowserArguments = "--disk-cache-size=52428800 --media-cache-size=26214400";

    private const int MaxAttempts = 10;
    private const int RetryDelayMs = 600;

    internal static CoreWebView2EnvironmentOptions CreateEnvironmentOptions()
    {
        return new CoreWebView2EnvironmentOptions
        {
            AdditionalBrowserArguments = DefaultBrowserArguments,
        };
    }

    internal static void TryDeleteProfileDirectory(string? userDataFolder, string? profileName, ILogger? logger = null)
    {
        if (string.IsNullOrWhiteSpace(userDataFolder) || string.IsNullOrWhiteSpace(profileName))
        {
            return;
        }

        try
        {
            var profilePath = Path.Combine(userDataFolder, "EBWebView", profileName);
            if (Directory.Exists(profilePath))
            {
                Directory.Delete(profilePath, recursive: true);
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            if (logger is not null && logger.IsEnabled(LogLevel.Debug))
            {
                logger.LogDebug(exception, "Не удалось удалить временный каталог профиля WebView2 {ProfileName}", profileName);
            }
        }
    }

    public static async Task<CoreWebView2Environment> CreateAsync(
        string userDataFolder,
        ILogger logger,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrEmpty(userDataFolder);
        ArgumentNullException.ThrowIfNull(logger);

        Directory.CreateDirectory(userDataFolder);

        var attempt = 0;

        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            attempt++;

            try
            {
                return await CoreWebView2Environment.CreateAsync(null, userDataFolder, CreateEnvironmentOptions())
                    .ConfigureAwait(true);
            }
            catch (Exception exception) when (attempt < MaxAttempts && exception is not WebView2RuntimeNotFoundException)
            {
                if (logger.IsEnabled(LogLevel.Debug))
                {
                    logger.LogDebug(exception,
                        "Папка данных WebView2 {Folder} недоступна (попытка {Attempt}/{Max}), повтор через {Delay} мс – вероятно, занята завершающимся процессом после обновления",
                        userDataFolder, attempt, MaxAttempts, RetryDelayMs);
                }

                await Task.Delay(RetryDelayMs, cancellationToken).ConfigureAwait(true);
            }
        }
    }
}
