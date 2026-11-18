namespace PoproshaykaBot.Wpf.Views.Tiles;

internal sealed class ChatBrowserNavigation
{
    private ulong? _navigationId;
    private bool _waitingForTarget;
    private bool _needsRetry;
    private bool _recoveryAttempted;

    public Uri? Target { get; private set; }

    public bool CanReuse(Uri target) => Target == target && !_needsRetry;

    public void Begin(Uri? target)
    {
        if (Target != target)
        {
            _recoveryAttempted = false;
        }

        Target = target;
        _navigationId = null;
        _waitingForTarget = target is not null;
        _needsRetry = false;
    }

    public void Started(string address, ulong navigationId, bool isRedirected)
    {
        if (Target is null || string.Equals(address, "about:blank", StringComparison.OrdinalIgnoreCase)
            || (isRedirected && _navigationId != navigationId)
            || (_waitingForTarget && (!Uri.TryCreate(address, UriKind.Absolute, out var uri) || uri != Target)))
        {
            return;
        }

        _navigationId = navigationId;
        _waitingForTarget = false;
    }

    public bool TryComplete(ulong navigationId, bool success)
    {
        if (_navigationId != navigationId)
        {
            return false;
        }

        _navigationId = null;
        _needsRetry = !success;

        if (success)
        {
            _recoveryAttempted = false;
        }

        return true;
    }

    public void RequireRetry()
    {
        _navigationId = null;
        _needsRetry = true;
    }

    public bool TryRecover()
    {
        RequireRetry();

        if (_recoveryAttempted)
        {
            return false;
        }

        _recoveryAttempted = true;
        return true;
    }
}
