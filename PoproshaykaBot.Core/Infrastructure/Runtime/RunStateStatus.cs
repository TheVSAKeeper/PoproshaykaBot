namespace PoproshaykaBot.Core.Infrastructure.Runtime;

public enum RunStateStatus
{
    None = 0,
    Running = 1,
    FatalShutdown = 2,
    StartupFailed = 3,
    MemoryWatchdog = 4,
    ShutdownWatchdog = 5,
    StopFailed = 6,
}
