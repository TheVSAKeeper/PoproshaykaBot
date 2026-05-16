using Microsoft.Extensions.Logging;
using PoproshaykaBot.Core.Settings;
using PoproshaykaBot.Core.Settings.Obs;
using System.Globalization;
using System.Text.Json;

namespace PoproshaykaBot.Core.Obs;

public sealed class ObsIntegrationService(
    IObsWebSocketClient client,
    SettingsManager settingsManager,
    ILogger<ObsIntegrationService> logger)
    : IDisposable
{
    private static readonly TimeSpan OperationTimeout = TimeSpan.FromSeconds(5);

    private static readonly Action<ILogger, string, string, string, string, Exception?> LogBrowserSourceProvisioned =
        LoggerMessage.Define<string, string, string, string>(LogLevel.Information,
            new(1, nameof(LogBrowserSourceProvisioned)),
            "OBS Browser Source {Action}: сцена {SceneName}, источник {SourceName}, URL {Url}");

    private static readonly Action<ILogger, string, Exception?> LogBrowserSourceRefreshFailed =
        LoggerMessage.Define<string>(LogLevel.Debug,
            new(2, nameof(LogBrowserSourceRefreshFailed)),
            "Не удалось обновить Browser Source {SourceName} через refreshnocache");

    private readonly SemaphoreSlim _operationGate = new(1, 1);

    public ObsConnectionSnapshot CurrentStatus { get; private set; } = ObsConnectionSnapshot.Disconnected();

    public async Task<ObsConnectionSnapshot> ConnectAsync(ObsIntegrationSettings settings, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(settings);

        await _operationGate.WaitAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            using var cts = CreateTimeoutToken(cancellationToken);
            try
            {
                var snapshot = await client.ConnectAsync(CreateOptions(settings), cts.Token).ConfigureAwait(false);
                CurrentStatus = await RefreshVersionSnapshotAsync(snapshot, cts.Token).ConfigureAwait(false);
                return CurrentStatus;
            }
            catch (Exception exception)
            {
                var safeMessage = ToSafeMessage(exception);
                CurrentStatus = ObsConnectionSnapshot.Disconnected(safeMessage);
                logger.LogWarning(exception, "Не удалось подключиться к OBS WebSocket: {Message}", safeMessage);
                return CurrentStatus;
            }
        }
        finally
        {
            _operationGate.Release();
        }
    }

    public async Task DisconnectAsync(CancellationToken cancellationToken)
    {
        await _operationGate.WaitAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            await client.DisconnectAsync(cancellationToken).ConfigureAwait(false);
            CurrentStatus = ObsConnectionSnapshot.Disconnected();
        }
        finally
        {
            _operationGate.Release();
        }
    }

    public async Task<IReadOnlyList<string>> ListScenesAsync(ObsIntegrationSettings settings, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(settings);

        await _operationGate.WaitAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            using var cts = CreateTimeoutToken(cancellationToken);
            await EnsureConnectedAsync(settings, cts.Token).ConfigureAwait(false);

            var sceneList = await GetSceneListAsync(cts.Token).ConfigureAwait(false);
            return sceneList.Select(scene => scene.Name).ToArray();
        }
        finally
        {
            _operationGate.Release();
        }
    }

    public async Task<ObsProvisionResult> ProvisionBrowserSourceAsync(
        ObsIntegrationSettings settings,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(settings);

        await _operationGate.WaitAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            using var cts = CreateTimeoutToken(cancellationToken);
            await EnsureConnectedAsync(settings, cts.Token).ConfigureAwait(false);
            await EnsureBrowserSourceKindAsync(cts.Token).ConfigureAwait(false);

            var sourceName = NormalizeSourceName(settings.SourceName);
            var sceneName = await ResolveSceneNameAsync(settings.SceneName, cts.Token).ConfigureAwait(false);
            var overlayUrl = BuildOverlayUrl();
            var browserSettings = new
            {
                url = overlayUrl,
                width = ClampDimension(settings.Width),
                height = ClampDimension(settings.Height),
                shutdown = false,
                restart_when_active = true,
            };

            var created = await CreateOrUpdateBrowserSourceAsync(sourceName, sceneName, browserSettings, cts.Token)
                .ConfigureAwait(false);

            await TryRefreshBrowserSourceAsync(sourceName, cts.Token).ConfigureAwait(false);

            LogBrowserSourceProvisioned(logger,
                created ? "создан" : "обновлён",
                sceneName,
                sourceName,
                overlayUrl,
                null);

            return new(created, sceneName, sourceName, overlayUrl);
        }
        finally
        {
            _operationGate.Release();
        }
    }

    public void Dispose()
    {
        _operationGate.Dispose();
    }

    private static ObsConnectionOptions CreateOptions(ObsIntegrationSettings settings)
    {
        var host = string.IsNullOrWhiteSpace(settings.Host) ? "127.0.0.1" : settings.Host.Trim();
        var port = settings.Port is >= 1 and <= 65535 ? settings.Port : 4455;
        return new(host, port, settings.Password);
    }

    private static CancellationTokenSource CreateTimeoutToken(CancellationToken cancellationToken)
    {
        var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        cts.CancelAfter(OperationTimeout);
        return cts;
    }

    private static string NormalizeSourceName(string sourceName)
    {
        return string.IsNullOrWhiteSpace(sourceName) ? "PoproshaykaBot Chat" : sourceName.Trim();
    }

    private static int ClampDimension(int value)
    {
        return Math.Clamp(value, 1, 10000);
    }

    private static string ToSafeMessage(Exception exception)
    {
        return exception switch
        {
            OperationCanceledException => "превышено время ожидания",
            ObsRequestException requestException => requestException.Message,
            InvalidOperationException invalidOperation => invalidOperation.Message,
            _ => "OBS недоступен или отклонил подключение",
        };
    }

    private static string GetRequiredString(JsonElement element, string propertyName)
    {
        return GetOptionalString(element, propertyName) ?? string.Empty;
    }

    private static string? GetOptionalString(JsonElement element, string propertyName)
    {
        return element.TryGetProperty(propertyName, out var value) ? value.GetString() : null;
    }

    private async Task<bool> CreateOrUpdateBrowserSourceAsync(
        string sourceName,
        string sceneName,
        object browserSettings,
        CancellationToken cancellationToken)
    {
        var inputs = await GetInputListAsync(cancellationToken).ConfigureAwait(false);
        var existing = inputs.FirstOrDefault(input =>
            string.Equals(input.Name, sourceName, StringComparison.OrdinalIgnoreCase));

        if (existing is null)
        {
            await client.SendRequestAsync("CreateInput",
                    new
                    {
                        sceneName,
                        inputName = sourceName,
                        inputKind = "browser_source",
                        inputSettings = browserSettings,
                        sceneItemEnabled = true,
                    },
                    cancellationToken)
                .ConfigureAwait(false);

            return true;
        }

        if (!string.Equals(existing.Kind, "browser_source", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException($"Источник OBS \"{sourceName}\" уже существует, но это не Browser Source.");
        }

        await client.SendRequestAsync("SetInputSettings",
                new
                {
                    inputName = sourceName,
                    inputSettings = browserSettings,
                    overlay = true,
                },
                cancellationToken)
            .ConfigureAwait(false);

        return false;
    }

    private async Task EnsureConnectedAsync(ObsIntegrationSettings settings, CancellationToken cancellationToken)
    {
        if (client.IsConnected)
        {
            return;
        }

        var snapshot = await client.ConnectAsync(CreateOptions(settings), cancellationToken).ConfigureAwait(false);
        CurrentStatus = await RefreshVersionSnapshotAsync(snapshot, cancellationToken).ConfigureAwait(false);
    }

    private async Task<ObsConnectionSnapshot> RefreshVersionSnapshotAsync(
        ObsConnectionSnapshot fallback,
        CancellationToken cancellationToken)
    {
        try
        {
            var version = await client.SendRequestAsync("GetVersion", null, cancellationToken).ConfigureAwait(false);
            if (version is null)
            {
                return fallback;
            }

            var data = version.Value;
            return fallback with
            {
                ObsVersion = GetOptionalString(data, "obsVersion") ?? fallback.ObsVersion,
                ObsWebSocketVersion = GetOptionalString(data, "obsWebSocketVersion") ?? fallback.ObsWebSocketVersion,
            };
        }
        catch (Exception exception)
        {
            logger.LogDebug(exception, "Не удалось получить версию OBS через GetVersion");
            return fallback;
        }
    }

    private async Task<IReadOnlyList<ObsSceneInfo>> GetSceneListAsync(CancellationToken cancellationToken)
    {
        var response = await client.SendRequestAsync("GetSceneList", null, cancellationToken).ConfigureAwait(false);
        if (response is null || !response.Value.TryGetProperty("scenes", out var scenes))
        {
            return [];
        }

        var result = new List<ObsSceneInfo>();
        using var sceneEnumerator = scenes.EnumerateArray();
        while (sceneEnumerator.MoveNext())
        {
            var scene = sceneEnumerator.Current;
            result.Add(new(GetRequiredString(scene, "sceneName")));
        }

        return result;
    }

    private async Task<string> ResolveSceneNameAsync(string configuredSceneName, CancellationToken cancellationToken)
    {
        if (!string.IsNullOrWhiteSpace(configuredSceneName))
        {
            return configuredSceneName.Trim();
        }

        var response = await client.SendRequestAsync("GetSceneList", null, cancellationToken).ConfigureAwait(false);
        if (response is null)
        {
            throw new InvalidOperationException("OBS не вернул список сцен.");
        }

        var data = response.Value;
        var current = GetOptionalString(data, "currentProgramSceneName");
        if (!string.IsNullOrWhiteSpace(current))
        {
            return current;
        }

        if (data.TryGetProperty("scenes", out var scenes))
        {
            using var sceneEnumerator = scenes.EnumerateArray();
            while (sceneEnumerator.MoveNext())
            {
                var scene = sceneEnumerator.Current;
                if (scene.ValueKind == JsonValueKind.Object)
                {
                    return GetRequiredString(scene, "sceneName");
                }
            }
        }

        throw new InvalidOperationException("В OBS нет сцен для добавления Browser Source.");
    }

    private async Task<IReadOnlyList<ObsInputInfo>> GetInputListAsync(CancellationToken cancellationToken)
    {
        var response = await client.SendRequestAsync("GetInputList", null, cancellationToken).ConfigureAwait(false);
        if (response is null || !response.Value.TryGetProperty("inputs", out var inputs))
        {
            return [];
        }

        var result = new List<ObsInputInfo>();
        using var inputEnumerator = inputs.EnumerateArray();
        while (inputEnumerator.MoveNext())
        {
            var input = inputEnumerator.Current;
            result.Add(new(GetRequiredString(input, "inputName"),
                GetRequiredString(input, "inputKind")));
        }

        return result;
    }

    private async Task EnsureBrowserSourceKindAsync(CancellationToken cancellationToken)
    {
        var response = await client.SendRequestAsync("GetInputKindList", new { unversioned = true }, cancellationToken).ConfigureAwait(false);
        if (response is null || !response.Value.TryGetProperty("inputKinds", out var inputKinds))
        {
            throw new InvalidOperationException("OBS не вернул список типов источников.");
        }

        var hasBrowserSource = false;
        using var kindEnumerator = inputKinds.EnumerateArray();
        while (kindEnumerator.MoveNext())
        {
            var kind = kindEnumerator.Current;
            if (!string.Equals(kind.GetString(), "browser_source", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            hasBrowserSource = true;
            break;
        }

        if (!hasBrowserSource)
        {
            throw new InvalidOperationException("В OBS недоступен тип Browser Source. Проверьте установку OBS Studio.");
        }
    }

    private async Task TryRefreshBrowserSourceAsync(string sourceName, CancellationToken cancellationToken)
    {
        try
        {
            await client.SendRequestAsync("PressInputPropertiesButton",
                    new { inputName = sourceName, propertyName = "refreshnocache" },
                    cancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            LogBrowserSourceRefreshFailed(logger, sourceName, exception);
        }
    }

    private string BuildOverlayUrl()
    {
        var port = settingsManager.Current.Twitch.HttpServerPort;
        return string.Create(CultureInfo.InvariantCulture, $"http://localhost:{port}/chat");
    }

    private sealed record ObsSceneInfo(string Name);

    private sealed record ObsInputInfo(string Name, string Kind);
}
