namespace PoproshaykaBot.Core.Update;

public sealed record ReleaseInfo(
    string TagName,
    string HtmlUrl,
    string Body,
    bool Prerelease,
    bool Draft,
    IReadOnlyList<ReleaseAsset> Assets)
{
    public bool HasAsset(string name)
    {
        return Assets.Any(asset => asset.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
    }
}
