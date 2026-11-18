namespace PoproshaykaBot.Core.Platforms.Auth;

public static class PlatformAuthRequestOptions
{
    public static readonly HttpRequestOptionsKey<string> ContextIdKey = new("poproshayka.auth.context-id");

    private static readonly AsyncLocal<string?> AmbientContextId = new();

    public static string? CurrentContextId => AmbientContextId.Value;

    public static IDisposable UseContext(string? contextId)
    {
        var previous = AmbientContextId.Value;
        AmbientContextId.Value = AuthContextIds.Normalize(contextId);
        return new Scope(previous);
    }

    public static string ResolveContextId(HttpRequestMessage? request)
    {
        if (request is not null
            && request.Options.TryGetValue(ContextIdKey, out var explicitContextId)
            && !string.IsNullOrWhiteSpace(explicitContextId))
        {
            return AuthContextIds.Normalize(explicitContextId);
        }

        return AuthContextIds.Normalize(AmbientContextId.Value);
    }

    private sealed class Scope(string? previous) : IDisposable
    {
        private bool _disposed;

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            AmbientContextId.Value = previous;
        }
    }
}
