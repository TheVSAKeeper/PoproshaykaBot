namespace PoproshaykaBot.Core.Server;

public sealed record HttpServerRuntimeSnapshot(bool IsRunning, int? Port);

public interface IHttpServerStatus
{
    bool IsRunning { get; }

    int? Port { get; }

    HttpServerRuntimeSnapshot CaptureSnapshot() => new(IsRunning, Port);
}
