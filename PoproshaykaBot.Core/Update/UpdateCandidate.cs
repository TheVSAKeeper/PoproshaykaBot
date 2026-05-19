namespace PoproshaykaBot.Core.Update;

public sealed record UpdateCandidate(Version Version, string TagName, ReleaseAsset Asset, string NotesUrl);
