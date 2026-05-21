using PoproshaykaBot.Core.Settings.Obs;
using PoproshaykaBot.WinForms.Forms.Settings;
using System.Reflection;

namespace PoproshaykaBot.Core.Tests.Settings;

[TestFixture]
[Apartment(ApartmentState.STA)]
public sealed class ObsChatSettingsControlColorTests
{
    [Test]
    public void LoadSettings_TranslucentBackgroundColor_DoesNotThrow()
    {
        using var control = new ObsChatSettingsControl();
        var settings = new ObsChatSettings
        {
            BackgroundColor = Color.FromArgb(179, 0, 0, 0),
            TextColor = Color.FromArgb(255, 255, 255, 255),
        };

        Assert.DoesNotThrow(() => control.LoadSettings(settings, 8080));
    }

    [Test]
    public void LoadSettings_ThenSaveSettings_PreservesTranslucentBackgroundAlpha()
    {
        using var control = new ObsChatSettingsControl();
        var saved = new ObsChatSettings
        {
            BackgroundColor = Color.FromArgb(179, 12, 34, 56),
            TextColor = Color.FromArgb(255, 200, 100, 50),
            UsernameColor = Color.FromArgb(128, 145, 70, 255),
            SystemMessageColor = Color.FromArgb(255, 255, 204, 0),
            TimestampColor = Color.FromArgb(255, 153, 153, 153),
        };

        control.LoadSettings(saved, 8080);
        var written = new ObsChatSettings();
        control.SaveSettings(written);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(written.BackgroundColor.ToArgb(), Is.EqualTo(saved.BackgroundColor.ToArgb()),
                "Альфа фона не должна теряться после Load → Save в настройках.");

            Assert.That(written.UsernameColor.ToArgb(), Is.EqualTo(saved.UsernameColor.ToArgb()),
                "Альфа цвета имени не должна теряться.");
        }
    }

    [Test]
    public void LoadSettings_DefaultObsChatSettings_DoesNotThrowAndRoundtripsBackgroundAlpha()
    {
        using var control = new ObsChatSettingsControl();
        var defaults = new ObsChatSettings();

        Assert.DoesNotThrow(() => control.LoadSettings(defaults, 8080));

        var saved = new ObsChatSettings();
        control.SaveSettings(saved);

        Assert.That(saved.BackgroundColor.ToArgb(), Is.EqualTo(defaults.BackgroundColor.ToArgb()));
    }

    [Test]
    public void ColorPreviewButton_ShowsHexWithAlphaWhenColorHasTransparency()
    {
        using var control = new ObsChatSettingsControl();
        var settings = new ObsChatSettings
        {
            BackgroundColor = Color.FromArgb(179, 0, 0, 0),
            TextColor = Color.FromArgb(255, 255, 255, 255),
        };

        control.LoadSettings(settings, 8080);

        var button = (Button)typeof(ObsChatSettingsControl)
            .GetField("_backgroundColorButton", BindingFlags.NonPublic | BindingFlags.Instance)!
            .GetValue(control)!;

        Assert.That(button.Text, Does.Contain("B3").IgnoreCase,
            $"Кнопка фона должна показывать альфу (B3 = 179 в hex), реально: '{button.Text}'.");
    }

    [Test]
    public void ColorPreviewButton_HidesAlphaSegmentWhenColorIsOpaque()
    {
        using var control = new ObsChatSettingsControl();
        var settings = new ObsChatSettings
        {
            BackgroundColor = Color.FromArgb(255, 18, 52, 86),
        };

        control.LoadSettings(settings, 8080);

        var button = (Button)typeof(ObsChatSettingsControl)
            .GetField("_backgroundColorButton", BindingFlags.NonPublic | BindingFlags.Instance)!
            .GetValue(control)!;

        Assert.That(button.Text, Is.EqualTo("#123456"),
            "Непрозрачный цвет должен показываться без хвоста-альфы.");
    }
}
