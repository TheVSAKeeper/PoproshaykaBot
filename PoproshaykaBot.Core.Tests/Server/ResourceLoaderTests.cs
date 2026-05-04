using PoproshaykaBot.Core.Server;

namespace PoproshaykaBot.Core.Tests.Server;

[TestFixture]
public sealed class ResourceLoaderTests
{
    [TestCase("PoproshaykaBot.Core.Assets.ObsOverlay.html")]
    [TestCase("PoproshaykaBot.Core.Assets.obs.css")]
    [TestCase("PoproshaykaBot.Core.Assets.obs.js")]
    [TestCase("PoproshaykaBot.Core.Assets.icon.ico")]
    public void ObsOverlayAssetsAreEmbedded(string resourceName)
    {
        var bytes = ResourceLoader.LoadResourceBytes(resourceName);

        Assert.That(bytes, Is.Not.Null);
        Assert.That(bytes, Is.Not.Empty,
            $"Ресурс {resourceName} объявлен, но пуст. Проверьте <EmbeddedResource> в .csproj.");
    }

    [Test]
    public void LoadResourceText_MissingResource_Throws()
    {
        Assert.That(() => ResourceLoader.LoadResourceText("PoproshaykaBot.Core.Assets.does-not-exist.html"),
            Throws.InvalidOperationException.With.Message.Contains("Ресурс не найден"));
    }

    [Test]
    public void LoadResourceText_OverlayHtml_StartsWithDoctype()
    {
        var html = ResourceLoader.LoadResourceText("PoproshaykaBot.Core.Assets.ObsOverlay.html");

        Assert.That(html.TrimStart(), Does.StartWith("<!DOCTYPE")
            .IgnoreCase
            .Or.StartWith("<html")
            .IgnoreCase);
    }
}
