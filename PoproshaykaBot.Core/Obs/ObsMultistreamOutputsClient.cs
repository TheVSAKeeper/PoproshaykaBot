using Microsoft.Extensions.Logging;
using System.Text.Json;

namespace PoproshaykaBot.Core.Obs;

public sealed class ObsMultistreamOutputsClient(
    IObsWebSocketClient client,
    ILogger logger)
{
    public const string StreamSuiteVendorName = "aitum-stream-suite";
    public const string MultistreamVendorName = "aitum-multistream";
    public const string StreamSuiteOutputPrefix = "Aitum Stream Suite Output ";
    public const string MultistreamOutputPrefix = "aitum_multi_output_";

    private const int VendorNotFoundErrorCode = 600;
    private const string RtmpOutputKindMarker = "rtmp";

    private static readonly HashSet<string> StandardOutputNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "adv_stream",
        "simple_stream",
        "default_stream",
        "adv_file_output",
        "simple_file_output",
        "adv_ffmpeg_output",
        "replay_buffer",
        "virtualcam_output",
    };

    private ObsMultistreamVendor? _detectedVendor;

    public void ResetVendorCache()
    {
        _detectedVendor = null;
    }

    public static string NormalizeOutputName(string rawName, ObsMultistreamVendor? vendor = null)
    {
        if (string.IsNullOrEmpty(rawName))
        {
            return string.Empty;
        }

        if (vendor is ObsMultistreamVendor.AitumStreamSuite or null
            && rawName.StartsWith(StreamSuiteOutputPrefix, StringComparison.Ordinal))
        {
            return rawName[StreamSuiteOutputPrefix.Length..];
        }

        if (vendor is ObsMultistreamVendor.AitumMultistream or null
            && rawName.StartsWith(MultistreamOutputPrefix, StringComparison.Ordinal))
        {
            return rawName[MultistreamOutputPrefix.Length..];
        }

        return rawName;
    }

    public static bool TryParseVendorEvent(ObsWebSocketEventArgs evt, out ObsSecondaryOutputEvent? parsed)
    {
        ArgumentNullException.ThrowIfNull(evt);
        parsed = null;

        if (!string.Equals(evt.EventType, "VendorEvent", StringComparison.Ordinal)
            || evt.EventData is not { ValueKind: JsonValueKind.Object } outer)
        {
            return false;
        }

        var vendorName = GetOptionalString(outer, "vendorName");
        var eventType = GetOptionalString(outer, "eventType");
        if (string.IsNullOrWhiteSpace(vendorName)
            || string.IsNullOrWhiteSpace(eventType)
            || !outer.TryGetProperty("eventData", out var eventData)
            || eventData.ValueKind != JsonValueKind.Object)
        {
            return false;
        }

        if (string.Equals(vendorName, MultistreamVendorName, StringComparison.Ordinal))
        {
            var kind = eventType switch
            {
                "output_started" => ObsSecondaryOutputEventKind.Started,
                "output_stopped" => ObsSecondaryOutputEventKind.Stopped,
                _ => ObsSecondaryOutputEventKind.None,
            };

            if (kind == ObsSecondaryOutputEventKind.None)
            {
                return false;
            }

            var rawName = GetOptionalString(eventData, "name");
            if (string.IsNullOrWhiteSpace(rawName))
            {
                return false;
            }

            parsed = new(
                ObsMultistreamVendor.AitumMultistream,
                kind,
                NormalizeOutputName(rawName, ObsMultistreamVendor.AitumMultistream),
                null,
                null);
            return true;
        }

        if (string.Equals(vendorName, StreamSuiteVendorName, StringComparison.Ordinal))
        {
            var kind = eventType switch
            {
                "start_output" => ObsSecondaryOutputEventKind.Started,
                "stop_output" => ObsSecondaryOutputEventKind.Stopped,
                _ => ObsSecondaryOutputEventKind.None,
            };

            if (kind == ObsSecondaryOutputEventKind.None)
            {
                return false;
            }

            var rawOutput = GetOptionalString(eventData, "output");
            if (string.IsNullOrWhiteSpace(rawOutput))
            {
                return false;
            }

            var errorCode = GetOptionalInt(eventData, "code");
            var lastError = GetOptionalString(eventData, "last_error");
            parsed = new(
                ObsMultistreamVendor.AitumStreamSuite,
                kind,
                NormalizeOutputName(rawOutput, ObsMultistreamVendor.AitumStreamSuite),
                errorCode,
                string.IsNullOrWhiteSpace(lastError) ? null : lastError);
            return true;
        }

        return false;
    }

    public async Task<ObsMultistreamVendor> DetectVendorAsync(CancellationToken cancellationToken)
    {
        if (_detectedVendor is { } cached)
        {
            return cached;
        }

        if (await ProbeVendorAsync(StreamSuiteVendorName, cancellationToken).ConfigureAwait(false))
        {
            _detectedVendor = ObsMultistreamVendor.AitumStreamSuite;
            return ObsMultistreamVendor.AitumStreamSuite;
        }

        if (await ProbeVendorAsync(MultistreamVendorName, cancellationToken).ConfigureAwait(false))
        {
            _detectedVendor = ObsMultistreamVendor.AitumMultistream;
            return ObsMultistreamVendor.AitumMultistream;
        }

        _detectedVendor = ObsMultistreamVendor.ObsOutput;
        return ObsMultistreamVendor.ObsOutput;
    }

    public async Task<IReadOnlyList<ObsSecondaryOutputSnapshot>> GetSecondaryOutputsAsync(CancellationToken cancellationToken)
    {
        var vendor = await DetectVendorAsync(cancellationToken).ConfigureAwait(false);
        var activeOutputs = await GetOutputListAsync(cancellationToken).ConfigureAwait(false);
        var activeById = activeOutputs.ToDictionary(item => item.Name, StringComparer.Ordinal);

        var result = new List<ObsSecondaryOutputSnapshot>();
        var coveredOutputIds = new HashSet<string>(StringComparer.Ordinal);

        if (vendor == ObsMultistreamVendor.AitumStreamSuite)
        {
            var suiteOutputs = await TryGetStreamSuiteOutputsAsync(activeById, cancellationToken).ConfigureAwait(false);
            if (suiteOutputs is null)
            {
                ResetVendorCache();
                vendor = await DetectVendorAsync(cancellationToken).ConfigureAwait(false);
            }
            else
            {
                foreach (var item in suiteOutputs)
                {
                    result.Add(item);
                    coveredOutputIds.Add(item.OutputId);
                }
            }
        }

        if (vendor == ObsMultistreamVendor.AitumMultistream)
        {
            var multistreamOutputs = await TryGetMultistreamOutputsAsync(activeById, cancellationToken).ConfigureAwait(false);
            if (multistreamOutputs is null)
            {
                ResetVendorCache();
            }
            else
            {
                foreach (var item in multistreamOutputs)
                {
                    result.Add(item);
                    coveredOutputIds.Add(item.OutputId);
                }
            }
        }

        foreach (var output in activeOutputs)
        {
            if (coveredOutputIds.Contains(output.Name)
                || !output.Active
                || !IsSecondaryRtmpCandidate(output))
            {
                continue;
            }

            var status = await TryGetOutputStatusAsync(output.Name, cancellationToken).ConfigureAwait(false);
            result.Add(new(
                NormalizeOutputName(output.Name),
                output.Name,
                output.Kind,
                ObsMultistreamVendor.ObsOutput,
                status?.Active ?? output.Active,
                status?.Reconnecting ?? false,
                status?.Timecode,
                status?.DurationMs,
                status?.Bytes,
                status?.Congestion,
                status?.SkippedFrames,
                status?.TotalFrames,
                CanControl: false,
                IsVertical: false));
        }

        return result;
    }

    public Task StartOutputAsync(string name, CancellationToken cancellationToken)
    {
        return InvokeOutputControlAsync(name, isStart: true, cancellationToken);
    }

    public Task StopOutputAsync(string name, CancellationToken cancellationToken)
    {
        return InvokeOutputControlAsync(name, isStart: false, cancellationToken);
    }

    private async Task InvokeOutputControlAsync(string name, bool isStart, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        var vendor = await DetectVendorAsync(cancellationToken).ConfigureAwait(false);
        if (vendor is not (ObsMultistreamVendor.AitumStreamSuite or ObsMultistreamVendor.AitumMultistream))
        {
            throw new InvalidOperationException("Управление дополнительными выходами недоступно без плагина Aitum");
        }

        var normalizedName = NormalizeOutputName(name, vendor);
        var vendorName = vendor == ObsMultistreamVendor.AitumStreamSuite
            ? StreamSuiteVendorName
            : MultistreamVendorName;
        var requestType = isStart ? "start_output" : "stop_output";
        object requestData = vendor == ObsMultistreamVendor.AitumStreamSuite
            ? new { output = normalizedName }
            : new { name = normalizedName };

        JsonElement? response;
        try
        {
            response = await CallVendorRequestAsync(vendorName, requestType, requestData, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (ObsRequestException exception) when (exception.Code == VendorNotFoundErrorCode)
        {
            ResetVendorCache();
            throw new InvalidOperationException("Плагин Aitum в OBS больше недоступен", exception);
        }

        var data = UnwrapVendorResponseData(response);
        if (data is { } payload && GetOptionalBool(payload, "success") == false)
        {
            var error = GetOptionalString(payload, "error");
            throw new InvalidOperationException(MapVendorError(error, normalizedName));
        }
    }

    private async Task<bool> ProbeVendorAsync(string vendorName, CancellationToken cancellationToken)
    {
        try
        {
            var response = await CallVendorRequestAsync(vendorName, "version", new { }, cancellationToken)
                .ConfigureAwait(false);
            var data = UnwrapVendorResponseData(response);
            if (data is not { ValueKind: JsonValueKind.Object } payload)
            {
                return false;
            }

            if (GetOptionalBool(payload, "success") == true)
            {
                return true;
            }

            return !string.IsNullOrWhiteSpace(GetOptionalString(payload, "version"));
        }
        catch (ObsRequestException exception)
        {
            logger.LogDebug(exception, "Вендор OBS {VendorName} недоступен (код {Code})", vendorName, exception.Code);
            return false;
        }
    }

    private async Task<List<ObsSecondaryOutputSnapshot>?> TryGetStreamSuiteOutputsAsync(
        IReadOnlyDictionary<string, OutputListEntry> activeById,
        CancellationToken cancellationToken)
    {
        JsonElement? response;
        try
        {
            response = await CallVendorRequestAsync(StreamSuiteVendorName, "get_outputs", new { }, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (ObsRequestException exception) when (exception.Code == VendorNotFoundErrorCode)
        {
            logger.LogDebug(exception, "Вендор {VendorName} не найден при опросе выходов", StreamSuiteVendorName);
            return null;
        }
        catch (ObsRequestException exception)
        {
            logger.LogDebug(exception, "Не удалось получить список выходов {VendorName}", StreamSuiteVendorName);
            return [];
        }

        var data = UnwrapVendorResponseData(response);
        if (data is not { ValueKind: JsonValueKind.Object } payload
            || !payload.TryGetProperty("outputs", out var outputs)
            || outputs.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        var snapshots = new List<ObsSecondaryOutputSnapshot>();
        using var enumerator = outputs.EnumerateArray();
        while (enumerator.MoveNext())
        {
            var item = enumerator.Current;
            var name = GetOptionalString(item, "name");
            if (string.IsNullOrWhiteSpace(name))
            {
                continue;
            }

            var outputId = StreamSuiteOutputPrefix + name;
            var type = GetOptionalString(item, "type");
            var vendorActive = GetOptionalBool(item, "active") ?? false;
            var vendorReconnecting = GetOptionalBool(item, "reconnecting") ?? false;
            var totalFrames = GetOptionalLong(item, "total_frames");
            var droppedFrames = GetOptionalLong(item, "dropped_frames");
            var totalBytes = GetOptionalLong(item, "total_bytes");
            var isVertical = GetOptionalBool(item, "vertical") ?? false;

            activeById.TryGetValue(outputId, out var listEntry);
            var isActive = listEntry?.Active ?? vendorActive;

            OutputStatusFields? status = null;
            if (isActive || listEntry is not null)
            {
                status = await TryGetOutputStatusAsync(outputId, cancellationToken).ConfigureAwait(false);
            }

            snapshots.Add(new(
                name,
                outputId,
                listEntry?.Kind ?? (string.IsNullOrWhiteSpace(type) ? "rtmp_output" : type),
                ObsMultistreamVendor.AitumStreamSuite,
                status?.Active ?? isActive,
                status?.Reconnecting ?? vendorReconnecting,
                status?.Timecode,
                status?.DurationMs,
                status?.Bytes ?? totalBytes,
                status?.Congestion,
                status?.SkippedFrames ?? droppedFrames,
                status?.TotalFrames ?? totalFrames,
                CanControl: true,
                IsVertical: isVertical));
        }

        return snapshots;
    }

    private async Task<List<ObsSecondaryOutputSnapshot>?> TryGetMultistreamOutputsAsync(
        IReadOnlyDictionary<string, OutputListEntry> activeById,
        CancellationToken cancellationToken)
    {
        JsonElement? response;
        try
        {
            response = await CallVendorRequestAsync(MultistreamVendorName, "get_outputs", new { }, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (ObsRequestException exception) when (exception.Code == VendorNotFoundErrorCode)
        {
            logger.LogDebug(exception, "Вендор {VendorName} не найден при опросе выходов", MultistreamVendorName);
            return null;
        }
        catch (ObsRequestException exception)
        {
            logger.LogDebug(exception, "Не удалось получить список выходов {VendorName}", MultistreamVendorName);
            return [];
        }

        var data = UnwrapVendorResponseData(response);
        if (data is not { ValueKind: JsonValueKind.Object } payload
            || !payload.TryGetProperty("outputs", out var outputs)
            || outputs.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        var snapshots = new List<ObsSecondaryOutputSnapshot>();
        using var enumerator = outputs.EnumerateArray();
        while (enumerator.MoveNext())
        {
            var item = enumerator.Current;
            var name = GetOptionalString(item, "name");
            if (string.IsNullOrWhiteSpace(name))
            {
                continue;
            }

            var isVertical = GetOptionalBool(item, "vertical") ?? false;
            var outputId = MultistreamOutputPrefix + name;

            activeById.TryGetValue(outputId, out var listEntry);
            OutputStatusFields? status = null;
            if (listEntry is not null)
            {
                status = await TryGetOutputStatusAsync(outputId, cancellationToken).ConfigureAwait(false);
            }

            var isActive = status?.Active ?? listEntry?.Active ?? false;
            snapshots.Add(new(
                name,
                outputId,
                listEntry?.Kind ?? "rtmp_output",
                ObsMultistreamVendor.AitumMultistream,
                isActive,
                status?.Reconnecting ?? false,
                status?.Timecode,
                status?.DurationMs,
                status?.Bytes,
                status?.Congestion,
                status?.SkippedFrames,
                status?.TotalFrames,
                CanControl: true,
                IsVertical: isVertical));
        }

        return snapshots;
    }

    private async Task<IReadOnlyList<OutputListEntry>> GetOutputListAsync(CancellationToken cancellationToken)
    {
        JsonElement? response;
        try
        {
            response = await client.SendRequestAsync("GetOutputList", null, cancellationToken).ConfigureAwait(false);
        }
        catch (ObsRequestException exception)
        {
            logger.LogDebug(exception, "OBS отклонил запрос GetOutputList");
            return [];
        }

        if (response is not { ValueKind: JsonValueKind.Object } payload
            || !payload.TryGetProperty("outputs", out var outputs)
            || outputs.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        var result = new List<OutputListEntry>();
        using var enumerator = outputs.EnumerateArray();
        while (enumerator.MoveNext())
        {
            var item = enumerator.Current;
            var name = GetOptionalString(item, "outputName");
            if (string.IsNullOrWhiteSpace(name))
            {
                continue;
            }

            result.Add(new(
                name,
                GetOptionalString(item, "outputKind") ?? string.Empty,
                GetOptionalBool(item, "outputActive") ?? false));
        }

        return result;
    }

    private async Task<OutputStatusFields?> TryGetOutputStatusAsync(string outputName, CancellationToken cancellationToken)
    {
        try
        {
            var response = await client.SendRequestAsync("GetOutputStatus", new { outputName }, cancellationToken)
                .ConfigureAwait(false);
            if (response is not { ValueKind: JsonValueKind.Object } data)
            {
                return null;
            }

            return new(
                GetOptionalBool(data, "outputActive") ?? false,
                GetOptionalBool(data, "outputReconnecting") ?? false,
                GetOptionalString(data, "outputTimecode"),
                GetOptionalLong(data, "outputDuration"),
                GetOptionalLong(data, "outputBytes"),
                GetOptionalDouble(data, "outputCongestion"),
                GetOptionalLong(data, "outputSkippedFrames"),
                GetOptionalLong(data, "outputTotalFrames"));
        }
        catch (ObsRequestException exception)
        {
            logger.LogDebug(exception, "Не удалось получить статус выхода OBS {OutputName}", outputName);
            return null;
        }
    }

    private Task<JsonElement?> CallVendorRequestAsync(
        string vendorName,
        string requestType,
        object requestData,
        CancellationToken cancellationToken)
    {
        return client.SendRequestAsync("CallVendorRequest",
            new { vendorName, requestType, requestData },
            cancellationToken);
    }

    private static bool IsSecondaryRtmpCandidate(OutputListEntry output)
    {
        if (StandardOutputNames.Contains(output.Name))
        {
            return false;
        }

        return output.Kind.Contains(RtmpOutputKindMarker, StringComparison.OrdinalIgnoreCase);
    }

    private static JsonElement? UnwrapVendorResponseData(JsonElement? response)
    {
        if (response is not { ValueKind: JsonValueKind.Object } element)
        {
            return response;
        }

        if (element.TryGetProperty("responseData", out var nested) && nested.ValueKind == JsonValueKind.Object)
        {
            return nested;
        }

        return element;
    }

    private static string MapVendorError(string? error, string outputName)
    {
        if (string.IsNullOrWhiteSpace(error))
        {
            return "OBS отклонил команду управления дополнительным выходом";
        }

        if (error.Contains("not found", StringComparison.OrdinalIgnoreCase))
        {
            return $"Выход «{outputName}» не найден в OBS";
        }

        if (error.Contains("not set", StringComparison.OrdinalIgnoreCase)
            || error.Contains("Missing name", StringComparison.OrdinalIgnoreCase))
        {
            return "Не указано имя дополнительного выхода";
        }

        return "OBS отклонил команду управления дополнительным выходом";
    }

    private static string? GetOptionalString(JsonElement element, string propertyName)
    {
        return element.TryGetProperty(propertyName, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;
    }

    private static bool? GetOptionalBool(JsonElement element, string propertyName)
    {
        return element.TryGetProperty(propertyName, out var value)
               && value.ValueKind is JsonValueKind.True or JsonValueKind.False
            ? value.GetBoolean()
            : null;
    }

    private static int? GetOptionalInt(JsonElement element, string propertyName)
    {
        return element.TryGetProperty(propertyName, out var value)
               && value.ValueKind == JsonValueKind.Number
               && value.TryGetInt32(out var parsed)
            ? parsed
            : null;
    }

    private static long? GetOptionalLong(JsonElement element, string propertyName)
    {
        return element.TryGetProperty(propertyName, out var value)
               && value.ValueKind == JsonValueKind.Number
               && value.TryGetInt64(out var parsed)
            ? parsed
            : null;
    }

    private static double? GetOptionalDouble(JsonElement element, string propertyName)
    {
        return element.TryGetProperty(propertyName, out var value)
               && value.ValueKind == JsonValueKind.Number
            ? value.GetDouble()
            : null;
    }

    private sealed record OutputListEntry(string Name, string Kind, bool Active);

    private sealed record OutputStatusFields(
        bool Active,
        bool Reconnecting,
        string? Timecode,
        long? DurationMs,
        long? Bytes,
        double? Congestion,
        long? SkippedFrames,
        long? TotalFrames);
}
