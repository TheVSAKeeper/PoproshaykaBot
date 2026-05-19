namespace PoproshaykaBot.Core.Settings.Update;

public sealed class UpdateSettings
{
    public bool AutoCheckEnabled { get; set; } = true;

    public bool AllowFrameworkDependentUpdate { get; set; }

    public string? RepositoryOverride { get; set; }

    public int CheckIntervalHours { get; set; } = 6;

    public string? SkippedVersion { get; set; }

    public DateTimeOffset? LastCheckUtc { get; set; }

    public UpdateApplyMode ApplyMode { get; set; } = UpdateApplyMode.NotifyAndConfirm;
}
