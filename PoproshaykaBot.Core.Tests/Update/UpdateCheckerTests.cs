using KeepShell.Updates;
using PoproshaykaBot.Core.Update;
using UpdateKind = PoproshaykaBot.Core.Update.UpdateKind;

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
        string assetName = "PoproshaykaBot-v3.1.0.7-x64-portable.exe",
        string arch = "x64",
        bool includeChecksums = true,
        bool includeRuntime = true,
        bool includeManifest = false)
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

        if (includeManifest)
        {
            assets.Add(Asset(ReleaseManifest.AssetName));
        }

        return new(tag, "https://example/notes", "body", prerelease, draft, assets);
    }

    private static IReleaseSource CreateLegacySource(
        ReleaseInfo release,
        string? checksums = null,
        string? runtime = "net10.0-windows")
    {
        var source = Substitute.For<IReleaseSource>();
        source.GetLatestReleaseAsync(Arg.Any<CancellationToken>()).Returns(release);

        var sumsText = checksums
            ?? $"{PortableHash}  PoproshaykaBot-v3.1.0.7-x64-portable.exe\n{FdHash}  PoproshaykaBot-v3.1.0.7-x64.exe\n";

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
        UpdateKind kind = UpdateKind.Portable,
        int runtimeMajor = 10)
    {
        var environment = Substitute.For<PoproshaykaBot.Core.Update.IUpdateEnvironment>();
        environment.Kind.Returns(kind);
        ((KeepShell.Updates.IUpdateEnvironment)environment).Kind.Returns(kind == UpdateKind.Unsupported
            ? KeepShell.Updates.UpdateKind.None
            : KeepShell.Updates.UpdateKind.SingleFile);
        environment.SelfContained.Returns(kind == UpdateKind.Portable);
        environment.CurrentVersion.Returns(Version.Parse(currentVersion));
        environment.Architecture.Returns(arch);
        environment.RuntimeMajor.Returns(runtimeMajor);

        return new(
            source,
            environment,
            PoproshaykaBot.Core.Update.UpdateServiceCollectionExtensions.CreateCheckOptions(),
            NullLogger<UpdateChecker>.Instance);
    }

    [Test]
    public async Task CheckAsync_ReturnsCandidate_WhenNewerVersionAvailable()
    {
        var source = CreateLegacySource(Release());

        var candidate = await CreateChecker(source).CheckAsync(null, CancellationToken.None);

        Assert.That(candidate, Is.Not.Null);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(candidate!.Version, Is.EqualTo(Version.Parse("3.1.0.7")));
            Assert.That(candidate.NotesUrl, Is.EqualTo("https://example/notes"));
            Assert.That(candidate.Variant.AssetName, Is.EqualTo("PoproshaykaBot-v3.1.0.7-x64-portable.exe"));
            Assert.That(candidate.Variant.Sha256, Is.EqualTo(PortableHash));
        }
    }

    [Test]
    public async Task CheckAsync_ReturnsNull_WhenAlreadyCurrent()
    {
        var source = CreateLegacySource(Release(tag: "v3.1.0.6"));

        var candidate = await CreateChecker(source).CheckAsync(null, CancellationToken.None);

        Assert.That(candidate, Is.Null);
    }

    [Test]
    public async Task CheckAsync_ReturnsNull_WhenVersionSkipped()
    {
        var source = CreateLegacySource(Release());

        var candidate = await CreateChecker(source).CheckAsync("3.1.0.7", CancellationToken.None);

        Assert.That(candidate, Is.Null);
    }

    [Test]
    public async Task CheckAsync_ReturnsNull_ForPrereleaseOrDraft()
    {
        var prereleaseSource = CreateLegacySource(Release(prerelease: true));
        var draftSource = CreateLegacySource(Release(draft: true));

        using (Assert.EnterMultipleScope())
        {
            Assert.That(await CreateChecker(prereleaseSource).CheckAsync(null, CancellationToken.None), Is.Null);
            Assert.That(await CreateChecker(draftSource).CheckAsync(null, CancellationToken.None), Is.Null);
        }
    }

    [Test]
    public async Task CheckAsync_FrameworkDependent_PicksPlainExe()
    {
        var source = Substitute.For<IReleaseSource>();
        source.GetLatestReleaseAsync(Arg.Any<CancellationToken>())
            .Returns(new ReleaseInfo("v3.1.0.7", "https://example/notes", "body", false, false,
            [
                new("PoproshaykaBot-v3.1.0.7-x64-portable.exe", "https://example/download/p", 1),
                new("PoproshaykaBot-v3.1.0.7-x64.exe", "https://example/download/f", 2),
                new("SHA256SUMS-x64.txt", "https://example/download/s", 3),
                new("RUNTIME-x64.txt", "https://example/download/r", 4),
            ]));
        source.DownloadTextAsync("https://example/download/s", Arg.Any<CancellationToken>())
            .Returns($"{PortableHash}  PoproshaykaBot-v3.1.0.7-x64-portable.exe\n{FdHash}  PoproshaykaBot-v3.1.0.7-x64.exe\n");
        source.DownloadTextAsync("https://example/download/r", Arg.Any<CancellationToken>())
            .Returns("net10.0-windows");

        var candidate = await CreateChecker(source, kind: UpdateKind.FrameworkDependent)
            .CheckAsync(null, CancellationToken.None);

        Assert.That(candidate, Is.Not.Null);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(candidate!.Variant.AssetName, Is.EqualTo("PoproshaykaBot-v3.1.0.7-x64.exe"));
            Assert.That(candidate.Variant.Sha256, Is.EqualTo(FdHash));
        }
    }

    [Test]
    public async Task CheckAsync_ReturnsNull_WhenUnsupportedBuild()
    {
        var source = CreateLegacySource(Release());

        var candidate = await CreateChecker(source, kind: UpdateKind.Unsupported)
            .CheckAsync(null, CancellationToken.None);

        Assert.That(candidate, Is.Null);
    }

    [Test]
    public async Task CheckAsync_ReturnsNull_WhenNoArchitectureAsset()
    {
        var source = CreateLegacySource(Release(assetName: "PoproshaykaBot-v3.1.0.7-x86-portable.exe"));

        var candidate = await CreateChecker(source, arch: "x64").CheckAsync(null, CancellationToken.None);

        Assert.That(candidate, Is.Null);
    }

    [Test]
    public async Task CheckAsync_ReturnsNull_WhenChecksumsAssetMissing()
    {
        var source = CreateLegacySource(Release(includeChecksums: false));

        var candidate = await CreateChecker(source).CheckAsync(null, CancellationToken.None);

        Assert.That(candidate, Is.Null);
    }

    [Test]
    public async Task CheckAsync_ReturnsNull_WhenRuntimeAssetMissing_ForFrameworkDependent()
    {
        var source = CreateLegacySource(Release(assetName: "PoproshaykaBot-v3.1.0.7-x64.exe", includeRuntime: false));

        var candidate = await CreateChecker(source, kind: UpdateKind.FrameworkDependent)
            .CheckAsync(null, CancellationToken.None);

        Assert.That(candidate, Is.Null);
    }

    [Test]
    public async Task CheckAsync_ReturnsNull_WhenRuntimeMajorDiffers_ForFrameworkDependent()
    {
        var source = CreateLegacySource(
            Release(assetName: "PoproshaykaBot-v3.1.0.7-x64.exe"),
            runtime: "net11.0-windows");

        var candidate = await CreateChecker(source, kind: UpdateKind.FrameworkDependent, runtimeMajor: 10)
            .CheckAsync(null, CancellationToken.None);

        Assert.That(candidate, Is.Null);
    }

    [Test]
    public async Task CheckAsync_Portable_DoesNotRequireRuntimeAsset()
    {
        var source = CreateLegacySource(Release(includeRuntime: false));

        var candidate = await CreateChecker(source).CheckAsync(null, CancellationToken.None);

        Assert.That(candidate, Is.Not.Null);
    }

    [TestCase(UpdateKind.Portable, "PoproshaykaBot-v3.1.0.7-x64-portable.exe", PortableHash)]
    [TestCase(UpdateKind.FrameworkDependent, "PoproshaykaBot-v3.1.0.7-x64.exe", FdHash)]
    public async Task CheckAsync_UsesReleaseManifestSchemaFromPublishScript(
        UpdateKind kind,
        string expectedAsset,
        string expectedHash)
    {
        var manifestJson = $$"""
            {
              "version": "3.1.0.7",
              "variants": [
                {
                  "kind": "SingleFile",
                  "arch": "x64",
                  "selfContained": false,
                  "runtimeMajor": 10,
                  "asset": "PoproshaykaBot-v3.1.0.7-x64.exe",
                  "sha256": "{{FdHash}}",
                  "size": 12345678
                },
                {
                  "kind": "SingleFile",
                  "arch": "x64",
                  "selfContained": true,
                  "runtimeMajor": null,
                  "asset": "PoproshaykaBot-v3.1.0.7-x64-portable.exe",
                  "sha256": "{{PortableHash}}",
                  "size": 87654321
                }
              ]
            }
            """;

        var source = Substitute.For<IReleaseSource>();
        source.GetLatestReleaseAsync(Arg.Any<CancellationToken>())
            .Returns(new ReleaseInfo("v3.1.0.7", "https://example/notes", "body", false, false,
            [
                Asset("PoproshaykaBot-v3.1.0.7-x64.exe"),
                Asset("PoproshaykaBot-v3.1.0.7-x64-portable.exe"),
                Asset(ReleaseManifest.AssetName),
            ]));
        source.DownloadTextAsync("https://example/download/" + ReleaseManifest.AssetName, Arg.Any<CancellationToken>())
            .Returns(manifestJson);

        var candidate = await CreateChecker(source, kind: kind).CheckAsync(null, CancellationToken.None);

        Assert.That(candidate, Is.Not.Null);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(candidate!.Variant.AssetName, Is.EqualTo(expectedAsset));
            Assert.That(candidate.Variant.Sha256, Is.EqualTo(expectedHash));
        }
    }

    [Test]
    public async Task CheckAsync_FallsBackToLegacyAssets_WhenManifestVersionMismatchesTag()
    {
        var mismatchedManifestJson = $$"""
            {
              "version": "3.1.0.8",
              "variants": [
                {
                  "kind": "SingleFile",
                  "arch": "x64",
                  "selfContained": true,
                  "runtimeMajor": null,
                  "asset": "PoproshaykaBot-v3.1.0.8-x64-portable.exe",
                  "sha256": "{{PortableHash}}",
                  "size": 100
                }
              ]
            }
            """;

        var source = CreateLegacySource(Release(includeManifest: true));
        source.DownloadTextAsync("https://example/download/" + ReleaseManifest.AssetName, Arg.Any<CancellationToken>())
            .Returns(mismatchedManifestJson);

        var candidate = await CreateChecker(source).CheckAsync(null, CancellationToken.None);

        Assert.That(candidate, Is.Not.Null);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(candidate!.Variant.AssetName, Is.EqualTo("PoproshaykaBot-v3.1.0.7-x64-portable.exe"));
            Assert.That(candidate.Variant.Sha256, Is.EqualTo(PortableHash));
        }
    }
}
