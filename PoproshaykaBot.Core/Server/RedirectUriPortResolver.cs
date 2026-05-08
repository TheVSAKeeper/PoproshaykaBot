namespace PoproshaykaBot.Core.Server;

public static class RedirectUriPortResolver
{
    public static bool TryResolve(string? redirectUri, out int port)
    {
        port = 0;

        if (string.IsNullOrWhiteSpace(redirectUri))
        {
            return false;
        }

        if (!Uri.TryCreate(redirectUri, UriKind.Absolute, out var uri))
        {
            return false;
        }

        port = uri.Port == -1
            ? uri.Scheme == "https" ? 443 : 80
            : uri.Port;

        return true;
    }
}
