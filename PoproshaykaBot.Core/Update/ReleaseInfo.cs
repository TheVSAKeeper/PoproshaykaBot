namespace PoproshaykaBot.Core.Update;

public sealed record ReleaseInfo(
    string TagName,
    string HtmlUrl,
    string Body,
    bool Prerelease,
    bool Draft,
    IReadOnlyList<ReleaseAsset> Assets);
