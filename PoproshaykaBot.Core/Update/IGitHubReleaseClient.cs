namespace PoproshaykaBot.Core.Update;

public interface IGitHubReleaseClient
{
    Task<ReleaseInfo?> GetLatestReleaseAsync(CancellationToken cancellationToken);

    Task<string> DownloadTextAsync(string url, CancellationToken cancellationToken);

    Task DownloadFileAsync(string url, string destinationPath, IProgress<int>? progress, CancellationToken cancellationToken);
}
