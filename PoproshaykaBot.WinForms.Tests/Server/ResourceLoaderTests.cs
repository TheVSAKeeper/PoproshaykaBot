using PoproshaykaBot.WinForms.Server;

namespace PoproshaykaBot.WinForms.Tests.Server;

[TestFixture]
public sealed class ResourceLoaderTests
{
    [TestCase("PoproshaykaBot.WinForms.Assets.ObsOverlay.html")]
    [TestCase("PoproshaykaBot.WinForms.Assets.obs.css")]
    [TestCase("PoproshaykaBot.WinForms.Assets.obs.js")]
    [TestCase("PoproshaykaBot.WinForms.icon.ico")]
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
        Assert.That(() => ResourceLoader.LoadResourceText("PoproshaykaBot.WinForms.Assets.does-not-exist.html"),
            Throws.InvalidOperationException.With.Message.Contains("Ресурс не найден"));
    }

    [Test]
    public void LoadResourceText_OverlayHtml_StartsWithDoctype()
    {
        var html = ResourceLoader.LoadResourceText("PoproshaykaBot.WinForms.Assets.ObsOverlay.html");

        Assert.That(html.TrimStart(), Does.StartWith("<!DOCTYPE")
            .IgnoreCase
            .Or.StartWith("<html")
            .IgnoreCase);
    }
}
