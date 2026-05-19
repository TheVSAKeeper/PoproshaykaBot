namespace PoproshaykaBot.Core.Update;

public sealed record PendingUpdate(
    string Version,
    string StagedExecutablePath,
    string TargetExecutablePath,
    string Sha256);
