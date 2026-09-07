namespace PoproshaykaBot.Core.Infrastructure.Runtime;

public enum AppExitReason
{
    None = 0,
    Normal = 1,
    AlreadyRunning = 2,
    Failed = 3,
    UpdatePending = 4,
}
