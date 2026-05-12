using PoproshaykaBot.Core.Infrastructure.Events;
using PoproshaykaBot.Core.Settings.Obs;
using PoproshaykaBot.Core.Settings.Stores;
using System.Text.Json;

namespace PoproshaykaBot.Core.Tests.Settings;

[TestFixture]
public sealed class ColorJsonConverterTests
{
    private static readonly JsonSerializerOptions Options = JsonStoreOptions.Default;

    [Test]
    public void Serialize_TranslucentBlack_WritesObjectFormWithAlpha()
    {
        var color = Color.FromArgb(179, 0, 0, 0);

        var json = JsonSerializer.Serialize(color, Options);

        Assert.That(json, Does.Contain("\"a\": 179"));
        Assert.That(json, Does.Contain("\"r\": 0"));
        Assert.That(json, Does.Contain("\"g\": 0"));
        Assert.That(json, Does.Contain("\"b\": 0"));
    }

    [Test]
    public void Roundtrip_TranslucentColor_PreservesAllChannels()
    {
        var original = Color.FromArgb(179, 12, 34, 56);

        var json = JsonSerializer.Serialize(original, Options);
        var deserialized = JsonSerializer.Deserialize<Color>(json, Options);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(deserialized.A, Is.EqualTo(179));
            Assert.That(deserialized.R, Is.EqualTo(12));
            Assert.That(deserialized.G, Is.EqualTo(34));
            Assert.That(deserialized.B, Is.EqualTo(56));
            Assert.That(deserialized.ToArgb(), Is.EqualTo(original.ToArgb()));
        }
    }

    [Test]
    public void Roundtrip_OpaqueColor_PreservesAllChannels()
    {
        var original = Color.FromArgb(255, 145, 70, 255);

        var json = JsonSerializer.Serialize(original, Options);
        var deserialized = JsonSerializer.Deserialize<Color>(json, Options);

        Assert.That(deserialized.ToArgb(), Is.EqualTo(original.ToArgb()));
    }

    [Test]
    public void Deserialize_LegacyUppercaseObjectForm_ReadsCorrectly()
    {
        const string Legacy = """{"A":200,"R":10,"G":20,"B":30}""";

        var color = JsonSerializer.Deserialize<Color>(Legacy, Options);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(color.A, Is.EqualTo(200));
            Assert.That(color.R, Is.EqualTo(10));
            Assert.That(color.G, Is.EqualTo(20));
            Assert.That(color.B, Is.EqualTo(30));
        }
    }

    [Test]
    public void Deserialize_HexRgb_AssignsOpaqueAlpha()
    {
        const string Hex = "\"#1a2b3c\"";

        var color = JsonSerializer.Deserialize<Color>(Hex, Options);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(color.A, Is.EqualTo(255));
            Assert.That(color.R, Is.EqualTo(0x1A));
            Assert.That(color.G, Is.EqualTo(0x2B));
            Assert.That(color.B, Is.EqualTo(0x3C));
        }
    }

    [Test]
    public void Deserialize_PartialObject_KeepsDefaultsForMissingChannels()
    {
        const string Partial = """{"r":10,"g":20,"b":30}""";

        var color = JsonSerializer.Deserialize<Color>(Partial, Options);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(color.A, Is.EqualTo(255));
            Assert.That(color.R, Is.EqualTo(10));
            Assert.That(color.G, Is.EqualTo(20));
            Assert.That(color.B, Is.EqualTo(30));
        }
    }

    [Test]
    public void ObsChatStore_RoundtripsTranslucentBackgroundColor()
    {
        var tempPath = Path.Combine(Path.GetTempPath(), $"obs-chat-{Guid.NewGuid():N}.json");

        try
        {
            var eventBus = new InMemoryEventBus(NullLogger<InMemoryEventBus>.Instance);
            var store = new ObsChatStore(eventBus, null, tempPath);

            var settings = store.Load();
            settings.BackgroundColor = Color.FromArgb(64, 12, 200, 8);
            settings.UsernameColor = Color.FromArgb(255, 145, 70, 255);
            store.Save(settings);

            var reopened = new ObsChatStore(eventBus, null, tempPath);
            var reloaded = reopened.Load();

            using (Assert.EnterMultipleScope())
            {
                Assert.That(reloaded.BackgroundColor.ToArgb(), Is.EqualTo(settings.BackgroundColor.ToArgb()));
                Assert.That(reloaded.UsernameColor.ToArgb(), Is.EqualTo(settings.UsernameColor.ToArgb()));
            }
        }
        finally
        {
            if (File.Exists(tempPath))
            {
                File.Delete(tempPath);
            }
        }
    }

    [Test]
    public void ObsChatSettings_DefaultBackgroundColor_RoundtripsViaJsonStoreClone()
    {
        var defaults = new ObsChatSettings();
        var cloned = JsonStoreClone.DeepClone(defaults);

        Assert.That(cloned.BackgroundColor.ToArgb(), Is.EqualTo(defaults.BackgroundColor.ToArgb()),
            "Дефолтный полупрозрачный фон не должен сбрасываться в прозрачный после JSON-клонирования.");
    }

    [Test]
    public void Roundtrip_WithoutColorConverter_LosesAllChannels()
    {
        var original = Color.FromArgb(179, 12, 34, 56);

        var json = JsonSerializer.Serialize(original);
        var deserialized = JsonSerializer.Deserialize<Color>(json);

        Assert.That(deserialized.IsEmpty, Is.True);
    }

    [Test]
    public void ObsChatSettings_DeepClone_PreservesColorsWhenUsingJsonStoreOptions()
    {
        var settings = new ObsChatSettings
        {
            BackgroundColor = Color.FromArgb(179, 12, 34, 56),
            TextColor = Color.FromArgb(255, 100, 150, 200),
        };

        var cloned = JsonStoreClone.DeepClone(settings);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(cloned.BackgroundColor.ToArgb(), Is.EqualTo(settings.BackgroundColor.ToArgb()));
            Assert.That(cloned.TextColor.ToArgb(), Is.EqualTo(settings.TextColor.ToArgb()));
        }
    }
}
