using Microsoft.AspNetCore.WebUtilities;
using PoproshaykaBot.Core.Server;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;

namespace PoproshaykaBot.Core.Platforms.Auth;

public sealed record LoopbackOAuthCallback(
    string? Code,
    string? State,
    string? Error,
    string? ErrorDescription);

public sealed class LoopbackOAuthCallbackListener : IDisposable
{
    private const int MaxHeaderLines = 128;

    private static readonly Lazy<string> SuccessHtml = new(static () =>
        ResourceLoader.LoadResourceText("PoproshaykaBot.Core.Assets.oauth-success.html"));

    private static readonly Lazy<string> ErrorTemplateHtml = new(static () =>
        ResourceLoader.LoadResourceText("PoproshaykaBot.Core.Assets.oauth-error.html"));

    private readonly TcpListener _listener;
    private readonly int _port;
    private readonly TimeSpan _connectionTimeout;
    private bool _disposed;

    public LoopbackOAuthCallbackListener(TimeSpan? connectionTimeout = null)
    {
        _connectionTimeout = connectionTimeout ?? TimeSpan.FromSeconds(10);
        _listener = new(IPAddress.Loopback, 0);
        _listener.Start();
        _port = ((IPEndPoint)_listener.LocalEndpoint).Port;
    }

    public string RedirectUri => $"http://127.0.0.1:{_port}/";

    public async Task<LoopbackOAuthCallback> WaitForCallbackAsync(string expectedState, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrEmpty(expectedState);

        while (!ct.IsCancellationRequested)
        {
            using var client = await _listener.AcceptTcpClientAsync(ct);

            try
            {
                var stream = client.GetStream();

                var requestTarget = await ReadRequestTargetAsync(stream, _connectionTimeout, ct);
                if (requestTarget is null)
                {
                    continue;
                }

                var (path, query) = SplitPathAndQuery(requestTarget);
                if (!string.Equals(path, "/", StringComparison.Ordinal))
                {
                    await WriteResponseAsync(stream, 404, "Not Found", string.Empty, ct);
                    continue;
                }

                var parameters = QueryHelpers.ParseQuery(query);
                var code = parameters.TryGetValue("code", out var c) ? c.ToString() : null;
                var state = parameters.TryGetValue("state", out var s) ? s.ToString() : null;
                var error = parameters.TryGetValue("error", out var err) ? err.ToString() : null;
                var errorDescription = parameters.TryGetValue("error_description", out var desc) ? desc.ToString() : null;

                if (!string.IsNullOrEmpty(error))
                {
                    var message = string.IsNullOrWhiteSpace(errorDescription) ? error : $"{error}: {errorDescription}";
                    await WriteResponseAsync(stream, 400, "Bad Request", BuildErrorHtml(message), ct);
                    return new(null, state, error, errorDescription);
                }

                if (!string.Equals(state, expectedState, StringComparison.Ordinal))
                {
                    const string StateError = "Несовпадение параметра state";
                    await WriteResponseAsync(stream, 400, "Bad Request", BuildErrorHtml(StateError), ct);
                    return new(null, state, "state_mismatch", StateError);
                }

                if (string.IsNullOrWhiteSpace(code))
                {
                    const string MissingCode = "Код авторизации отсутствует";
                    await WriteResponseAsync(stream, 400, "Bad Request", BuildErrorHtml(MissingCode), ct);
                    return new(null, state, "missing_code", MissingCode);
                }

                await WriteResponseAsync(stream, 200, "OK", SuccessHtml.Value, ct);
                return new(code, state, null, null);
            }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested)
            {
            }
            catch (IOException)
            {
            }
            catch (SocketException)
            {
            }
        }

        throw new OperationCanceledException(ct);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _listener.Stop();
    }

    private static async Task<string?> ReadRequestTargetAsync(NetworkStream stream, TimeSpan timeout, CancellationToken ct)
    {
        using var readTimeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        readTimeout.CancelAfter(timeout);

        using var reader = new StreamReader(stream, Encoding.ASCII, detectEncodingFromByteOrderMarks: false, bufferSize: 4096, leaveOpen: true);

        var requestLine = await reader.ReadLineAsync(readTimeout.Token);
        if (string.IsNullOrWhiteSpace(requestLine))
        {
            return null;
        }

        for (var i = 0; i < MaxHeaderLines; i++)
        {
            var headerLine = await reader.ReadLineAsync(readTimeout.Token);
            if (string.IsNullOrEmpty(headerLine))
            {
                break;
            }
        }

        var parts = requestLine.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return parts.Length >= 2 ? parts[1] : null;
    }

    private static (string Path, string Query) SplitPathAndQuery(string requestTarget)
    {
        var queryIndex = requestTarget.IndexOf('?', StringComparison.Ordinal);
        return queryIndex < 0
            ? (requestTarget, string.Empty)
            : (requestTarget[..queryIndex], requestTarget[queryIndex..]);
    }

    private static string BuildErrorHtml(string error)
    {
        return ErrorTemplateHtml.Value.Replace("{0}", WebUtility.HtmlEncode(error), StringComparison.Ordinal);
    }

    private static async Task WriteResponseAsync(NetworkStream stream, int statusCode, string reasonPhrase, string htmlBody, CancellationToken ct)
    {
        var bodyBytes = Encoding.UTF8.GetBytes(htmlBody);
        var headers =
            $"HTTP/1.1 {statusCode} {reasonPhrase}\r\n" +
            $"Content-Type: text/html; charset=utf-8\r\n" +
            $"Content-Length: {bodyBytes.Length}\r\n" +
            "Connection: close\r\n" +
            "Cache-Control: no-store\r\n\r\n";

        var headerBytes = Encoding.ASCII.GetBytes(headers);
        await stream.WriteAsync(headerBytes, ct);
        if (bodyBytes.Length > 0)
        {
            await stream.WriteAsync(bodyBytes, ct);
        }

        await stream.FlushAsync(ct);
    }
}
