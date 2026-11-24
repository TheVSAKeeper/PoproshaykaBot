using Microsoft.Extensions.Logging.Abstractions;
using PoproshaykaBot.Wpf.Infrastructure;
using System.IO;

namespace PoproshaykaBot.Wpf.Tests.Tiles;

[TestFixture]
public sealed class WebView2EnvironmentFactoryTests
{
    private DirectoryInfo _directory = null!;

    [SetUp]
    public void SetUp()
    {
        _directory = Directory.CreateTempSubdirectory("wv2-env-factory");
    }

    [TearDown]
    public void TearDown()
    {
        _directory.Delete(recursive: true);
    }

    [Test]
    public void CreateEnvironmentOptions_ConfiguresDiskAndMediaCacheLimits()
    {
        var options = WebView2EnvironmentFactory.CreateEnvironmentOptions();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(options.AdditionalBrowserArguments, Does.Contain("--disk-cache-size=52428800"));
            Assert.That(options.AdditionalBrowserArguments, Does.Contain("--media-cache-size=26214400"));
        }
    }

    [Test]
    public void TryDeleteProfileDirectory_RemovesTemporaryGuidProfileWithoutTouchingSiblings()
    {
        var ebWebView = Path.Combine(_directory.FullName, "EBWebView");
        var tempProfile = Path.Combine(ebWebView, "temp-guid-profile");
        var siblingProfile = Path.Combine(ebWebView, "saved-profile");
        Directory.CreateDirectory(tempProfile);
        Directory.CreateDirectory(siblingProfile);
        File.WriteAllText(Path.Combine(tempProfile, "Cookies"), "test");
        File.WriteAllText(Path.Combine(siblingProfile, "Cookies"), "keep");

        WebView2EnvironmentFactory.TryDeleteProfileDirectory(_directory.FullName, "temp-guid-profile", NullLogger.Instance);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(Directory.Exists(tempProfile), Is.False);
            Assert.That(Directory.Exists(siblingProfile), Is.True);
        }
    }

    [TestCase(null, "profile")]
    [TestCase("", "profile")]
    [TestCase("folder", null)]
    [TestCase("folder", "")]
    public void TryDeleteProfileDirectory_IgnoresMissingOrEmptyArguments(string? folder, string? profile)
    {
        Assert.DoesNotThrow(() => WebView2EnvironmentFactory.TryDeleteProfileDirectory(folder, profile, NullLogger.Instance));
    }
}
