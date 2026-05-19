using PoproshaykaBot.Core.Update;

namespace PoproshaykaBot.Core.Tests.Update;

[TestFixture]
public sealed class UpdateCheckerTests
{
    private static ReleaseInfo Release(
        string tag = "v3.1.0.7",
        bool prerelease = false,
        bool draft = false,
        string assetName = "PoproshaykaBot-v3.1.0.7-x64-portable.exe")
    {
        return new(tag, "https://example/notes", "body", prerelease, draft,
        [
            new(assetName, "https://example/download/" + assetName, 100, "application/octet-stream"),
        ]);
    }

    private static UpdateChecker CreateChecker(
        IGitHubReleaseClient client,
        string currentVersion = "3.1.0.6",
        string arch = "x64",
        UpdateKind kind = UpdateKind.Portable)
    {
        var environment = Substitute.For<IUpdateEnvironment>();
        environment.CurrentVersion.Returns(Version.Parse(currentVersion));
        environment.ArchitectureMoniker.Returns(arch);
        environment.Kind.Returns(kind);

        return new(client, environment, NullLogger<UpdateChecker>.Instance);
    }

    [Test]
    public async Task CheckAsync_ReturnsCandidate_WhenNewerVersionAvailable()
    {
        var client = Substitute.For<IGitHubReleaseClient>();
        client.GetLatestReleaseAsync(Arg.Any<CancellationToken>()).Returns(Release());

        var candidate = await CreateChecker(client).CheckAsync(null, CancellationToken.None);

        Assert.That(candidate, Is.Not.Null);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(candidate!.Version, Is.EqualTo(Version.Parse("3.1.0.7")));
            Assert.That(candidate.NotesUrl, Is.EqualTo("https://example/notes"));
            Assert.That(candidate.Asset.Name, Is.EqualTo("PoproshaykaBot-v3.1.0.7-x64-portable.exe"));
        }
    }

    [Test]
    public async Task CheckAsync_ReturnsNull_WhenAlreadyCurrent()
    {
        var client = Substitute.For<IGitHubReleaseClient>();
        client.GetLatestReleaseAsync(Arg.Any<CancellationToken>()).Returns(Release(tag: "v3.1.0.6"));

        var candidate = await CreateChecker(client).CheckAsync(null, CancellationToken.None);

        Assert.That(candidate, Is.Null);
    }

    [Test]
    public async Task CheckAsync_ReturnsNull_WhenVersionSkipped()
    {
        var client = Substitute.For<IGitHubReleaseClient>();
        client.GetLatestReleaseAsync(Arg.Any<CancellationToken>()).Returns(Release());

        var candidate = await CreateChecker(client).CheckAsync("3.1.0.7", CancellationToken.None);

        Assert.That(candidate, Is.Null);
    }

    [Test]
    public async Task CheckAsync_ReturnsNull_ForPrereleaseOrDraft()
    {
        var prereleaseClient = Substitute.For<IGitHubReleaseClient>();
        prereleaseClient.GetLatestReleaseAsync(Arg.Any<CancellationToken>()).Returns(Release(prerelease: true));

        var draftClient = Substitute.For<IGitHubReleaseClient>();
        draftClient.GetLatestReleaseAsync(Arg.Any<CancellationToken>()).Returns(Release(draft: true));

        using (Assert.EnterMultipleScope())
        {
            Assert.That(await CreateChecker(prereleaseClient).CheckAsync(null, CancellationToken.None), Is.Null);
            Assert.That(await CreateChecker(draftClient).CheckAsync(null, CancellationToken.None), Is.Null);
        }
    }

    [Test]
    public async Task CheckAsync_FrameworkDependent_PicksPlainExe()
    {
        var client = Substitute.For<IGitHubReleaseClient>();
        client.GetLatestReleaseAsync(Arg.Any<CancellationToken>())
            .Returns(new ReleaseInfo("v3.1.0.7", "https://example/notes", "body", false, false,
            [
                new("PoproshaykaBot-v3.1.0.7-x64-portable.exe", "p", 1, "application/octet-stream"),
                new("PoproshaykaBot-v3.1.0.7-x64.exe", "f", 2, "application/octet-stream"),
            ]));

        var candidate = await CreateChecker(client, kind: UpdateKind.FrameworkDependent)
            .CheckAsync(null, CancellationToken.None);

        Assert.That(candidate, Is.Not.Null);
        Assert.That(candidate!.Asset.Name, Is.EqualTo("PoproshaykaBot-v3.1.0.7-x64.exe"));
    }

    [Test]
    public async Task CheckAsync_ReturnsNull_WhenUnsupportedBuild()
    {
        var client = Substitute.For<IGitHubReleaseClient>();
        client.GetLatestReleaseAsync(Arg.Any<CancellationToken>()).Returns(Release());

        var candidate = await CreateChecker(client, kind: UpdateKind.Unsupported)
            .CheckAsync(null, CancellationToken.None);

        Assert.That(candidate, Is.Null);
    }

    [Test]
    public async Task CheckAsync_ReturnsNull_WhenNoArchitectureAsset()
    {
        var client = Substitute.For<IGitHubReleaseClient>();
        client
            .GetLatestReleaseAsync(Arg.Any<CancellationToken>())
            .Returns(Release(assetName: "PoproshaykaBot-v3.1.0.7-x86-portable.exe"));

        var candidate = await CreateChecker(client, arch: "x64").CheckAsync(null, CancellationToken.None);

        Assert.That(candidate, Is.Null);
    }
}
