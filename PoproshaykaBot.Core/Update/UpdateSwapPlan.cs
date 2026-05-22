namespace PoproshaykaBot.Core.Update;

public sealed record UpdateSwapPlan(string CurrentExecutable, string BackupExecutable, string StagedExecutable);
