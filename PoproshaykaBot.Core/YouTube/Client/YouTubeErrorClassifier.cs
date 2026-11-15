using Grpc.Core;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace PoproshaykaBot.Core.YouTube.Client;

internal static class YouTubeErrorClassifier
{
    public static YouTubeApiException FromHttpResponse(
        HttpStatusCode statusCode,
        string? responseBody,
        string operation)
    {
        var reason = TryExtractHttpReason(responseBody);
        var kind = ClassifyFromReason(reason);

        if (kind == YouTubeApiErrorKind.None)
        {
            kind = statusCode switch
            {
                HttpStatusCode.Unauthorized => YouTubeApiErrorKind.Unauthorized,
                HttpStatusCode.Forbidden => YouTubeApiErrorKind.Forbidden,
                HttpStatusCode.NotFound => YouTubeApiErrorKind.NotFound,
                HttpStatusCode.BadRequest => YouTubeApiErrorKind.BadRequest,
                HttpStatusCode.TooManyRequests => YouTubeApiErrorKind.RateLimitExceeded,
                >= HttpStatusCode.InternalServerError => YouTubeApiErrorKind.Transient,
                _ => YouTubeApiErrorKind.Unknown,
            };
        }

        if (kind == YouTubeApiErrorKind.Unknown && ContainsKeyword(responseBody, "quotaExceeded"))
        {
            kind = YouTubeApiErrorKind.QuotaExceeded;
        }

        var message = string.IsNullOrWhiteSpace(reason)
            ? $"YouTube Data API ({operation}) вернул {(int)statusCode}."
            : $"YouTube Data API ({operation}) вернул {(int)statusCode} ({reason}).";

        return new(kind, message, reason, statusCode);
    }

    public static YouTubeApiException FromRpcException(RpcException exception, string operation)
    {
        ArgumentNullException.ThrowIfNull(exception);

        var reason = TryExtractRpcReason(exception);
        var kind = ClassifyFromReason(reason);

        if (kind == YouTubeApiErrorKind.None)
        {
            kind = exception.StatusCode switch
            {
                StatusCode.Unauthenticated => YouTubeApiErrorKind.Unauthorized,
                StatusCode.Unimplemented => YouTubeApiErrorKind.TransportUnsupported,
                StatusCode.PermissionDenied => YouTubeApiErrorKind.Forbidden,
                StatusCode.NotFound => YouTubeApiErrorKind.NotFound,
                StatusCode.InvalidArgument or StatusCode.FailedPrecondition => YouTubeApiErrorKind.BadRequest,
                StatusCode.ResourceExhausted => YouTubeApiErrorKind.RateLimitExceeded,
                StatusCode.Unavailable or StatusCode.Internal or StatusCode.DeadlineExceeded or StatusCode.Aborted =>
                    YouTubeApiErrorKind.Transient,
                _ => YouTubeApiErrorKind.Unknown,
            };
        }

        if (HasHttpProtocolException(exception))
        {
            kind = YouTubeApiErrorKind.TransportUnsupported;
        }

        var message = string.IsNullOrWhiteSpace(reason)
            ? $"YouTube gRPC ({operation}) завершился со статусом {exception.StatusCode}: {exception.Status.Detail}"
            : $"YouTube gRPC ({operation}) завершился со статусом {exception.StatusCode} ({reason}).";

        return new(kind, message, reason, innerException: exception);
    }

    public static YouTubeApiException FromTransportException(Exception exception, string operation)
    {
        ArgumentNullException.ThrowIfNull(exception);

        if (HasHttpProtocolException(exception))
        {
            return new(
                YouTubeApiErrorKind.TransportUnsupported,
                $"YouTube ({operation}): HTTP/2 транспорт не поддерживается: {exception.Message}",
                innerException: exception);
        }

        return new(
            YouTubeApiErrorKind.Transient,
            $"YouTube ({operation}): сетевая ошибка: {exception.Message}",
            innerException: exception);
    }

    public static string? TryExtractHttpReason(string? responseBody)
    {
        if (string.IsNullOrWhiteSpace(responseBody))
        {
            return null;
        }

        try
        {
            var dto = JsonSerializer.Deserialize<GoogleApiErrorEnvelopeDto>(responseBody);

            var reason = dto?.Error?.Errors?
                .FirstOrDefault(static item => !string.IsNullOrWhiteSpace(item.Reason))
                ?.Reason;

            return string.IsNullOrWhiteSpace(reason) ? dto?.Error?.Status : reason;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static YouTubeApiErrorKind ClassifyFromReason(string? reason)
    {
        if (string.IsNullOrWhiteSpace(reason))
        {
            return YouTubeApiErrorKind.None;
        }

        if (ContainsAny(reason, "quotaExceeded", "dailyLimitExceeded", "dailyLimitExceededUnreg"))
        {
            return YouTubeApiErrorKind.QuotaExceeded;
        }

        if (ContainsAny(reason, "rateLimitExceeded", "userRateLimitExceeded"))
        {
            return YouTubeApiErrorKind.RateLimitExceeded;
        }

        if (ContainsAny(reason, "pageTokenInvalid", "invalidPageToken"))
        {
            return YouTubeApiErrorKind.InvalidPageToken;
        }

        if (ContainsAny(reason, "liveChatEnded", "liveChatDisabled", "liveChatNotFound"))
        {
            return YouTubeApiErrorKind.LiveChatEnded;
        }

        return YouTubeApiErrorKind.None;
    }

    private static string? TryExtractRpcReason(RpcException exception)
    {
        var fromDetail = FindKnownReason(exception.Status.Detail);

        if (fromDetail is not null)
        {
            return fromDetail;
        }

        foreach (var entry in exception.Trailers)
        {
            if (entry.IsBinary)
            {
                var decoded = Encoding.UTF8.GetString(entry.ValueBytes);
                var fromBytes = FindKnownReason(decoded);

                if (fromBytes is not null)
                {
                    return fromBytes;
                }
            }
            else if (string.Equals(entry.Key, "reason", StringComparison.OrdinalIgnoreCase))
            {
                return entry.Value;
            }
        }

        return null;
    }

    private static string? FindKnownReason(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        string[] knownReasons =
        [
            "quotaExceeded",
            "dailyLimitExceededUnreg",
            "dailyLimitExceeded",
            "userRateLimitExceeded",
            "rateLimitExceeded",
            "pageTokenInvalid",
            "invalidPageToken",
            "liveChatEnded",
            "liveChatDisabled",
            "liveChatNotFound",
        ];

        foreach (var candidate in knownReasons)
        {
            if (text.Contains(candidate, StringComparison.OrdinalIgnoreCase))
            {
                return candidate;
            }
        }

        return null;
    }

    private static bool HasHttpProtocolException(Exception exception)
    {
        for (var current = exception; current is not null; current = current.InnerException)
        {
            if (current is HttpProtocolException)
            {
                return true;
            }
        }

        return false;
    }

    private static bool ContainsAny(string value, params string[] candidates)
    {
        foreach (var candidate in candidates)
        {
            if (value.Contains(candidate, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private static bool ContainsKeyword(string? text, string keyword)
    {
        return !string.IsNullOrWhiteSpace(text) && text.Contains(keyword, StringComparison.OrdinalIgnoreCase);
    }

    private sealed class GoogleApiErrorEnvelopeDto
    {
        [JsonPropertyName("error")]
        public GoogleApiErrorBodyDto? Error { get; set; }
    }

    private sealed class GoogleApiErrorBodyDto
    {
        [JsonPropertyName("status")]
        public string? Status { get; set; }

        [JsonPropertyName("errors")]
        public List<GoogleApiErrorItemDto>? Errors { get; set; }
    }

    private sealed class GoogleApiErrorItemDto
    {
        [JsonPropertyName("reason")]
        public string? Reason { get; set; }
    }
}
