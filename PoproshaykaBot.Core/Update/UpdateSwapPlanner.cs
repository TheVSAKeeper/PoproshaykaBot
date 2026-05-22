namespace PoproshaykaBot.Core.Update;

public static class UpdateSwapPlanner
{
    public const string BackupSuffix = ".old";

    public static UpdateSwapPlan Plan(string currentExecutable, string stagedExecutable)
    {
        ArgumentException.ThrowIfNullOrEmpty(currentExecutable);
        ArgumentException.ThrowIfNullOrEmpty(stagedExecutable);

        return new(currentExecutable,
            currentExecutable + BackupSuffix,
            stagedExecutable);
    }
}
