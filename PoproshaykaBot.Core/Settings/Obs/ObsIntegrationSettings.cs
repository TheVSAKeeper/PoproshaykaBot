namespace PoproshaykaBot.Core.Settings.Obs;

public sealed class ObsIntegrationSettings
{
    public bool Enabled { get; set; }

    public bool AutoConnect { get; set; } = true;

    public bool AutoProvisionBrowserSource { get; set; }

    public bool ApplySceneOnProfile { get; set; }

    public bool ApplyProfileOnScene { get; set; }

    public string Host { get; set; } = "127.0.0.1";

    public int Port { get; set; } = 4455;

    public string Password { get; set; } = string.Empty;

    public string SceneName { get; set; } = string.Empty;

    public string DashboardMicrophoneName { get; set; } = string.Empty;

    public List<string> DashboardSourceNames { get; set; } = [];

    public int DashboardVolumeMeterDelayMs { get; set; } = 120;

    public string SourceName { get; set; } = "PoproshaykaBot Chat";

    public int Width { get; set; } = 1920;

    public int Height { get; set; } = 1080;

    public IReadOnlyList<string> GetDashboardSourceNames()
    {
        if (DashboardSourceNames.Count > 0)
        {
            return DashboardSourceNames
                .Select(name => name?.Trim() ?? string.Empty)
                .Where(name => name.Length > 0)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }

        return string.IsNullOrWhiteSpace(DashboardMicrophoneName)
            ? []
            : [DashboardMicrophoneName.Trim()];
    }
}
