using KeepShell.Updates;
using PoproshaykaBot.Core.Update;

namespace PoproshaykaBot.Core.Tests.Update;

[TestFixture]
public sealed class UpdateCheckerTests
{
    private const string PortableHash = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";
    private const string FdHash = "bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb";

    private static ReleaseAsset Asset(string name)
    {
        return new(name, "https://example/download/" + name, 100);
    }

    private static ReleaseInfo Release(
        string tag = "v3.1.0.7",
        bool prerelease = false,
        bool draft = false,
        string assetName = "PoproshaykaBot-v3.1.0.7-x64-portable.zip",
        string arch = "x64",
        bool includeChecksums = true,
        bool includeRuntime = true)
    {
        var assets = new List<ReleaseAsset> { Asset(assetName) };

        if (includeChecksums)
        {
            assets.Add(Asset("SHA256SUMS-" + arch + ".txt"));
        }

        if (includeRuntime)
        {
            assets.Add(Asset("RUNTIME-" + arch + ".txt"));
        }

        return new(tag, "https://example/notes", "body", prerelease, draft, assets);
    }

    private static IReleaseSource CreateSource(
        ReleaseInfo release,
        string? checksums = null,
        string? runtime = "net10.0-windows")
    {
        var source = Substitute.For<IReleaseSource>();
        source.GetLatestReleaseAsync(Arg.Any<CancellationToken>()).Returns(release);

        var sumsText = checksums
            ?? $"{PortableHash}  PoproshaykaBot-v3.1.0.7-x64-portable.zip\n"
            + $"{PortableHash}  PoproshaykaBot-v3.1.0.7-x64-portable.exe\n"
            + $"{FdHash}  PoproshaykaBot-v3.1.0.7-x64.zip\n"
            + $"{FdHash}  PoproshaykaBot-v3.1.0.7-x64.exe\n";

        source.DownloadTextAsync("https://example/download/SHA256SUMS-x64.txt", Arg.Any<CancellationToken>())
            .Returns(sumsText);
        source.DownloadTextAsync("https://example/download/RUNTIME-x64.txt", Arg.Any<CancellationToken>())
            .Returns(runtime);

        return source;
    }

    private static UpdateChecker CreateChecker(
        IReleaseSource source,
        string currentVersion = "3.1.0.6",
        string arch = "x64",
        UpdateKind kind = UpdateKind.SingleFile,
        bool selfContained = true,
        int runtimeMajor = 10)
    {
        var environment = new UpdateEnvironment(
            kind,
            selfContained,
            Version.Parse(currentVersion),
            runtimeMajor,
            executablePath: "Bot.exe",
            architecture: arch);

        return new(
            source,
            environment,
            PoproshaykaBot.Core.Update.UpdateServiceCollectionExtensions.CreateCheckOptions(),
            NullLogger<UpdateChecker>.Instance);
    }

    [Test]
    public async Task CheckAsync_ReturnsZipCandidate_WhenNewerVersionAvailable()
    {
        var source = CreateSource(Release());

        var candidate = await CreateChecker(source).CheckAsync(null, CancellationToken.None);

        Assert.That(candidate, Is.Not.Null);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(candidate!.Version, Is.EqualTo(Version.Parse("3.1.0.7")));
            Assert.That(candidate.NotesUrl, Is.EqualTo("https://example/notes"));
            Assert.That(candidate.Variant.AssetName, Is.EqualTo("PoproshaykaBot-v3.1.0.7-x64-portable.zip"));
            Assert.That(candidate.Variant.Sha256, Is.EqualTo(PortableHash));
        }
    }

    [Test]
    public async Task CheckAsync_PrefersZipAndFallsBackToExe_OnOlderReleaseWithoutZipHash()
    {
        var release = new ReleaseInfo("v3.1.0.7", "https://example/notes", "body", false, false,
        [
            Asset("PoproshaykaBot-v3.1.0.7-x64-portable.exe"),
            Asset("PoproshaykaBot-v3.1.0.7-x64-portable.zip"),
            Asset("SHA256SUMS-x64.txt"),
        ]);

        var sourceWithZip = CreateSource(release);
        var zipCandidate = await CreateChecker(sourceWithZip).CheckAsync(null, CancellationToken.None);
        Assert.That(zipCandidate!.Variant.AssetName, Is.EqualTo("PoproshaykaBot-v3.1.0.7-x64-portable.zip"));

        var sourceOnlyExeHash = CreateSource(release, checksums: $"{PortableHash}  PoproshaykaBot-v3.1.0.7-x64-portable.exe\n");
        var exeCandidate = await CreateChecker(sourceOnlyExeHash).CheckAsync(null, CancellationToken.None);
        Assert.That(exeCandidate!.Variant.AssetName, Is.EqualTo("PoproshaykaBot-v3.1.0.7-x64-portable.exe"));
    }

    [Test]
    public async Task CheckAsync_ReturnsNull_WhenAlreadyCurrent()
    {
        var source = CreateSource(Release(tag: "v3.1.0.6"));

        var candidate = await CreateChecker(source).CheckAsync(null, CancellationToken.None);

        Assert.That(candidate, Is.Null);
    }

    [Test]
    public async Task CheckAsync_ReturnsNull_WhenVersionSkipped()
    {
        var source = CreateSource(Release());

        var candidate = await CreateChecker(source).CheckAsync("3.1.0.7", CancellationToken.None);

        Assert.That(candidate, Is.Null);
    }

    [Test]
    public async Task CheckAsync_ReturnsNull_ForPrereleaseOrDraft()
    {
        var prereleaseSource = CreateSource(Release(prerelease: true));
        var draftSource = CreateSource(Release(draft: true));

        using (Assert.EnterMultipleScope())
        {
            Assert.That(await CreateChecker(prereleaseSource).CheckAsync(null, CancellationToken.None), Is.Null);
            Assert.That(await CreateChecker(draftSource).CheckAsync(null, CancellationToken.None), Is.Null);
        }
    }

    [Test]
    public async Task CheckAsync_FrameworkDependent_PicksNonPortableZip()
    {
        var source = Substitute.For<IReleaseSource>();
        source.GetLatestReleaseAsync(Arg.Any<CancellationToken>())
            .Returns(new ReleaseInfo("v3.1.0.7", "https://example/notes", "body", false, false,
            [
                new("PoproshaykaBot-v3.1.0.7-x64-portable.zip", "https://example/download/p", 1),
                new("PoproshaykaBot-v3.1.0.7-x64.zip", "https://example/download/f", 2),
                new("SHA256SUMS-x64.txt", "https://example/download/s", 3),
                new("RUNTIME-x64.txt", "https://example/download/r", 4),
            ]));
        source.DownloadTextAsync("https://example/download/s", Arg.Any<CancellationToken>())
            .Returns($"{PortableHash}  PoproshaykaBot-v3.1.0.7-x64-portable.zip\n{FdHash}  PoproshaykaBot-v3.1.0.7-x64.zip\n");
        source.DownloadTextAsync("https://example/download/r", Arg.Any<CancellationToken>())
            .Returns("net10.0-windows");

        var candidate = await CreateChecker(source, selfContained: false)
            .CheckAsync(null, CancellationToken.None);

        Assert.That(candidate, Is.Not.Null);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(candidate!.Variant.AssetName, Is.EqualTo("PoproshaykaBot-v3.1.0.7-x64.zip"));
            Assert.That(candidate.Variant.Sha256, Is.EqualTo(FdHash));
        }
    }

    [Test]
    public async Task CheckAsync_ReturnsNull_WhenUnsupportedBuild()
    {
        var source = CreateSource(Release());

        var candidate = await CreateChecker(source, kind: UpdateKind.None)
            .CheckAsync(null, CancellationToken.None);

        Assert.That(candidate, Is.Null);
    }

    [Test]
    public async Task CheckAsync_ReturnsNull_WhenNoArchitectureAsset()
    {
        var source = CreateSource(Release(assetName: "PoproshaykaBot-v3.1.0.7-x86-portable.zip"));

        var candidate = await CreateChecker(source, arch: "x64").CheckAsync(null, CancellationToken.None);

        Assert.That(candidate, Is.Null);
    }

    [Test]
    public async Task CheckAsync_ReturnsNull_WhenChecksumsAssetMissing()
    {
        var source = CreateSource(Release(includeChecksums: false));

        var candidate = await CreateChecker(source).CheckAsync(null, CancellationToken.None);

        Assert.That(candidate, Is.Null);
    }

    [Test]
    public async Task CheckAsync_ReturnsNull_WhenRuntimeAssetMissing_ForFrameworkDependent()
    {
        var source = CreateSource(Release(assetName: "PoproshaykaBot-v3.1.0.7-x64.zip", includeRuntime: false));

        var candidate = await CreateChecker(source, selfContained: false)
            .CheckAsync(null, CancellationToken.None);

        Assert.That(candidate, Is.Null);
    }

    [Test]
    public async Task CheckAsync_ReturnsNull_WhenRuntimeMajorDiffers_ForFrameworkDependent()
    {
        var source = CreateSource(
            Release(assetName: "PoproshaykaBot-v3.1.0.7-x64.zip"),
            runtime: "net11.0-windows");

        var candidate = await CreateChecker(source, selfContained: false, runtimeMajor: 10)
            .CheckAsync(null, CancellationToken.None);

        Assert.That(candidate, Is.Null);
    }

    [Test]
    public async Task CheckAsync_Portable_DoesNotRequireRuntimeAsset()
    {
        var source = CreateSource(Release(includeRuntime: false));

        var candidate = await CreateChecker(source).CheckAsync(null, CancellationToken.None);

        Assert.That(candidate, Is.Not.Null);
    }
}
