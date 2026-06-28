using PoproshaykaBot.Wpf.Bootstrap;

namespace PoproshaykaBot.Wpf.Tests;

[TestFixture]
public class ThemeMappingTests
{
    [TestCase(AppTheme.Light, "light")]
    [TestCase(AppTheme.Dark, "dark")]
    public void ToKey_FromKey_round_trips(AppTheme theme, string key)
    {
        Assert.That(AppThemes.ToKey(theme), Is.EqualTo(key));
        Assert.That(AppThemes.FromKey(key), Is.EqualTo(theme));
    }

    [TestCase(null)]
    [TestCase("")]
    [TestCase("unknown")]
    public void FromKey_unknown_falls_back_to_light(string? key)
    {
        Assert.That(AppThemes.FromKey(key), Is.EqualTo(AppTheme.Light));
    }
}
