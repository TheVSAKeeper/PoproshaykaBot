using PoproshaykaBot.WinForms.Settings;

namespace PoproshaykaBot.WinForms.Tests.Settings;

[TestFixture]
public class UiSettingsDefaultsTests
{
    [Test]
    public void Defaults_LeftSlotContent_ShouldBeLogs()
    {
        var settings = new UiSettings();

        Assert.That(settings.LeftSlotContent, Is.EqualTo(PanelContent.Logs));
    }

    [Test]
    public void Defaults_RightSlotContent_ShouldBeBroadcastProfiles()
    {
        var settings = new UiSettings();

        Assert.That(settings.RightSlotContent, Is.EqualTo(PanelContent.BroadcastProfiles));
    }
}
