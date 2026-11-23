namespace PoproshaykaBot.Core.Platforms.Auth;

public static class PlatformAuthRequestOptions
{
    public static readonly HttpRequestOptionsKey<string> ContextIdKey = new("poproshayka.auth.context-id");

    private static readonly AsyncLocal<(string? ContextId, string? AccountId)> AmbientContext = new();

    public static string? CurrentContextId => AmbientContext.Value.ContextId;

    public static string? ExpectedAccountId => AmbientContext.Value.AccountId;

    public static IDisposable UseContext(string? contextId, string? expectedAccountId = null)
    {
        var previous = AmbientContext.Value;
        AmbientContext.Value = (AuthContextIds.Normalize(contextId), expectedAccountId);
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

        return AuthContextIds.Normalize(CurrentContextId);
    }

    private sealed class Scope((string? ContextId, string? AccountId) previous) : IDisposable
    {
        private bool _disposed;

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            AmbientContext.Value = previous;
        }
    }
}
