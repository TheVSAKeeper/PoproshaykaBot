using Microsoft.Extensions.Logging;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace PoproshaykaBot.Core.Update;

public sealed class GitHubReleaseClient(
    IHttpClientFactory httpClientFactory,
    IUpdateRepositoryProvider repository,
    ILogger<GitHubReleaseClient> logger)
    : IGitHubReleaseClient
{
    public const string HttpClientName = "GitHubReleases";

    private const int DownloadBufferSize = 81920;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    public async Task<ReleaseInfo?> GetLatestReleaseAsync(CancellationToken cancellationToken)
    {
        using var client = httpClientFactory.CreateClient(HttpClientName);

        var latestReleasePath = $"repos/{repository.Slug}/releases/latest";

        using var response = await client
            .GetAsync(latestReleasePath, cancellationToken)
            .ConfigureAwait(false);

        if (!response.IsSuccessStatusCode)
        {
            logger.LogWarning("GitHub вернул статус {Status} при запросе последнего релиза", (int)response.StatusCode);
            return null;
        }

        await using var stream = await response.Content
            .ReadAsStreamAsync(cancellationToken)
            .ConfigureAwait(false);

        var dto = await JsonSerializer
            .DeserializeAsync<ReleaseDto>(stream, JsonOptions, cancellationToken)
            .ConfigureAwait(false);

        if (dto?.TagName is null)
        {
            return null;
        }

        var assets = (dto.Assets ?? [])
            .Where(asset => asset is { Name: not null, BrowserDownloadUrl: not null })
            .Select(asset => new ReleaseAsset(asset.Name!,
                asset.BrowserDownloadUrl!,
                asset.Size,
                asset.ContentType ?? string.Empty))
            .ToArray();

        return new(dto.TagName,
            dto.HtmlUrl ?? string.Empty,
            dto.Body ?? string.Empty,
            dto.Prerelease,
            dto.Draft,
            assets);
    }

    public async Task<string> DownloadTextAsync(string url, CancellationToken cancellationToken)
    {
        using var client = httpClientFactory.CreateClient(HttpClientName);

        using var response = await client
            .GetAsync(url, cancellationToken)
            .ConfigureAwait(false);

        response.EnsureSuccessStatusCode();

        return await response.Content
            .ReadAsStringAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task DownloadFileAsync(
        string url,
        string destinationPath,
        IProgress<int>? progress,
        CancellationToken cancellationToken)
    {
        using var client = httpClientFactory.CreateClient(HttpClientName);

        using var response = await client
            .GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
            .ConfigureAwait(false);

        response.EnsureSuccessStatusCode();

        var totalBytes = response.Content.Headers.ContentLength ?? -1L;

        await using var source = await response.Content
            .ReadAsStreamAsync(cancellationToken)
            .ConfigureAwait(false);

        await using var destination = new FileStream(destinationPath,
            FileMode.Create,
            FileAccess.Write,
            FileShare.None,
            DownloadBufferSize,
            true);

        var buffer = new byte[DownloadBufferSize];
        long received = 0;
        var lastReportedPercent = -1;
        int read;

        while ((read = await source.ReadAsync(buffer, cancellationToken).ConfigureAwait(false)) > 0)
        {
            await destination
                .WriteAsync(buffer.AsMemory(0, read), cancellationToken)
                .ConfigureAwait(false);

            received += read;

            if (progress is null || totalBytes <= 0)
            {
                continue;
            }

            var percent = (int)(received * 100 / totalBytes);
            if (percent == lastReportedPercent)
            {
                continue;
            }

            lastReportedPercent = percent;
            progress.Report(percent);
        }
    }

    private sealed record ReleaseDto
    {
        [JsonPropertyName("tag_name")]
        public string? TagName { get; init; }

        [JsonPropertyName("html_url")]
        public string? HtmlUrl { get; init; }

        [JsonPropertyName("body")]
        public string? Body { get; init; }

        [JsonPropertyName("prerelease")]
        public bool Prerelease { get; init; }

        [JsonPropertyName("draft")]
        public bool Draft { get; init; }

        [JsonPropertyName("assets")]
        public AssetDto[]? Assets { get; init; }
    }

    private sealed record AssetDto
    {
        [JsonPropertyName("name")]
        public string? Name { get; init; }

        [JsonPropertyName("browser_download_url")]
        public string? BrowserDownloadUrl { get; init; }

        [JsonPropertyName("size")]
        public long Size { get; init; }

        [JsonPropertyName("content_type")]
        public string? ContentType { get; init; }
    }
}
