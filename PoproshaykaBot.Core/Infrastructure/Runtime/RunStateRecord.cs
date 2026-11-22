namespace PoproshaykaBot.Core.Infrastructure.Runtime;

public sealed record RunStateRecord(
    int ProcessId,
    DateTimeOffset StartedAtUtc,
    string Version,
    string? Commit,
    RunStateStatus Status,
    string? ExceptionType,
    DateTimeOffset UpdatedAtUtc);

public sealed record PreviousRunState(bool WasUnclean, RunStateRecord? Record)
{
    public static PreviousRunState Clean { get; } = new(false, null);
}
