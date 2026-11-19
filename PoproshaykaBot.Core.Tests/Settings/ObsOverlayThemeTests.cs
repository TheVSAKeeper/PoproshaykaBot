using PoproshaykaBot.Core.Settings.Obs;

namespace PoproshaykaBot.Core.Tests.Settings;

[TestFixture]
public sealed class ObsOverlayThemeTests
{
    [TestCase(null, ObsOverlayTheme.Classic)]
    [TestCase("", ObsOverlayTheme.Classic)]
    [TestCase("   ", ObsOverlayTheme.Classic)]
    [TestCase("unknown", ObsOverlayTheme.Classic)]
    [TestCase("classic", ObsOverlayTheme.Classic)]
    [TestCase("CLASSIC", ObsOverlayTheme.Classic)]
    [TestCase("flow", ObsOverlayTheme.Flow)]
    [TestCase("  FLOW  ", ObsOverlayTheme.Flow)]
    public void Normalize_ReturnsCanonicalTheme(string? input, string expected)
    {
        Assert.That(ObsOverlayTheme.Normalize(input), Is.EqualTo(expected));
    }

    [Test]
    public void Clamp_NormalizesOverlayTheme()
    {
        var flow = ObsChatSettingsValidator.Clamp(new() { OverlayTheme = " FLOW " });
        var fallback = ObsChatSettingsValidator.Clamp(new() { OverlayTheme = "invalid" });

        using (Assert.EnterMultipleScope())
        {
            Assert.That(flow.OverlayTheme, Is.EqualTo(ObsOverlayTheme.Flow));
            Assert.That(fallback.OverlayTheme, Is.EqualTo(ObsOverlayTheme.Classic));
            Assert.That(ObsOverlayTheme.Values, Is.EquivalentTo(new[] { ObsOverlayTheme.Classic, ObsOverlayTheme.Flow }));
        }
    }
}
