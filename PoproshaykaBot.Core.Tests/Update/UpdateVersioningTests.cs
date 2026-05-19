using PoproshaykaBot.Core.Update;

namespace PoproshaykaBot.Core.Tests.Update;

[TestFixture]
public sealed class UpdateVersioningTests
{
    [TestCase("v3.1.0.7", "3.1.0.7")]
    [TestCase("3.1.0.6", "3.1.0.6")]
    [TestCase("V3.1.0.6+abcdef", "3.1.0.6")]
    [TestCase(" v3.1.0 ", "3.1.0.0")]
    [TestCase("3.2.0-rc1", "3.2.0.0")]
    public void TryParseTag_ParsesSupportedFormats(string raw, string expected)
    {
        var parsed = UpdateVersioning.TryParseTag(raw, out var version);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(parsed, Is.True);
            Assert.That(version, Is.EqualTo(Version.Parse(expected)));
        }
    }

    [TestCase("")]
    [TestCase("   ")]
    [TestCase("not-a-version")]
    [TestCase(null)]
    public void TryParseTag_RejectsInvalidInput(string? raw)
    {
        Assert.That(UpdateVersioning.TryParseTag(raw, out _), Is.False);
    }

    [Test]
    public void SelectAsset_Portable_PicksPortableForArchitecture()
    {
        var release = new ReleaseInfo("v3.1.0.7", "url", "body", false, false,
        [
            new("PoproshaykaBot-v3.1.0.7-x86-portable.exe", "u1", 1, "application/octet-stream"),
            new("PoproshaykaBot-v3.1.0.7-x64-portable.exe", "u2", 2, "application/octet-stream"),
            new("PoproshaykaBot-v3.1.0.7-x64.exe", "u3", 3, "application/octet-stream"),
        ]);

        var asset = UpdateVersioning.SelectAsset(release, "x64", UpdateKind.Portable);

        Assert.That(asset, Is.Not.Null);
        Assert.That(asset!.Name, Is.EqualTo("PoproshaykaBot-v3.1.0.7-x64-portable.exe"));
    }

    [Test]
    public void SelectAsset_FrameworkDependent_PicksPlainExeNotPortable()
    {
        var release = new ReleaseInfo("v3.1.0.7", "url", "body", false, false,
        [
            new("PoproshaykaBot-v3.1.0.7-x64-portable.exe", "u1", 1, "application/octet-stream"),
            new("PoproshaykaBot-v3.1.0.7-x64.exe", "u2", 2, "application/octet-stream"),
        ]);

        var asset = UpdateVersioning.SelectAsset(release, "x64", UpdateKind.FrameworkDependent);

        Assert.That(asset, Is.Not.Null);
        Assert.That(asset!.Name, Is.EqualTo("PoproshaykaBot-v3.1.0.7-x64.exe"));
    }

    [Test]
    public void SelectAsset_ReturnsNull_ForUnsupportedKindOrNoMatch()
    {
        var release = new ReleaseInfo("v3.1.0.7", "url", "body", false, false,
        [
            new("PoproshaykaBot-v3.1.0.7-x86-portable.exe", "u1", 1, "application/octet-stream"),
        ]);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(UpdateVersioning.SelectAsset(release, "x64", UpdateKind.Portable), Is.Null);
            Assert.That(UpdateVersioning.SelectAsset(release, "x86", UpdateKind.Unsupported), Is.Null);
        }
    }

    [Test]
    public void IsNewer_ComparesVersions()
    {
        using (Assert.EnterMultipleScope())
        {
            Assert.That(UpdateVersioning.IsNewer(Version.Parse("3.1.0.7"), Version.Parse("3.1.0.6")), Is.True);
            Assert.That(UpdateVersioning.IsNewer(Version.Parse("3.1.0.6"), Version.Parse("3.1.0.6")), Is.False);
            Assert.That(UpdateVersioning.IsNewer(Version.Parse("3.1.0.5"), Version.Parse("3.1.0.6")), Is.False);
        }
    }
}
