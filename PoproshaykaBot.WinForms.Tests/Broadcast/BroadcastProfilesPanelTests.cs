using Microsoft.Extensions.Logging;
using PoproshaykaBot.WinForms.Broadcast;
using PoproshaykaBot.WinForms.Broadcast.Profiles;
using PoproshaykaBot.WinForms.Infrastructure.Di;
using PoproshaykaBot.WinForms.Infrastructure.Events;
using PoproshaykaBot.WinForms.Settings;
using PoproshaykaBot.WinForms.Streaming;

namespace PoproshaykaBot.WinForms.Tests.Broadcast;

[TestFixture]
[Apartment(ApartmentState.STA)]
public class BroadcastProfilesPanelTests
{
    [Test]
    public void Upsert_AfterPanelSetup_ShouldPersistThroughManager()
    {
        var manager = CreateManager();
        var forms = Substitute.For<IFormFactory>();
        var bus = Substitute.For<IEventBus>();
        var streamStatus = Substitute.For<IStreamStatus>();
        var settingsLogger = Substitute.For<ILogger<SettingsManager>>();
        var settingsManager = Substitute.For<SettingsManager>(settingsLogger, bus);
        settingsManager.Current.Returns(new AppSettings());

        using var panel = new BroadcastProfilesPanel
        {
            Manager = manager,
            Forms = forms,
            Bus = bus,
            Settings = settingsManager,
            Stream = streamStatus,
        };

        manager.Upsert(new() { Name = "Профиль 1" });

        Assert.That(manager.GetAll().Select(p => p.Name), Is.EquivalentTo(["Профиль 1"]));
    }

    [Test]
    public void TitleMatches_NoPlaceholder_LiteralEqualityWins()
    {
        Assert.That(BroadcastProfilesPanel.TitleMatches("Просто стрим", "Просто стрим"), Is.True);
        Assert.That(BroadcastProfilesPanel.TitleMatches("Просто стрим", "Другое"), Is.False);
    }

    [Test]
    public void TitleMatches_WithPlaceholder_AnyDigitsAccepted()
    {
        Assert.That(BroadcastProfilesPanel.TitleMatches("Серия #{n}", "Серия #14"), Is.True);
        Assert.That(BroadcastProfilesPanel.TitleMatches("Серия #{n}", "Серия #1"), Is.True);
        Assert.That(BroadcastProfilesPanel.TitleMatches("Серия #{n}", "Серия #999"), Is.True);
    }

    [Test]
    public void TitleMatches_WithPlaceholder_NonDigitsRejected()
    {
        Assert.That(BroadcastProfilesPanel.TitleMatches("Серия #{n}", "Серия #abc"), Is.False);
        Assert.That(BroadcastProfilesPanel.TitleMatches("Серия #{n}", "Другая #14"), Is.False);
    }

    [Test]
    public void TitleMatches_RegexSpecialCharsInProfileTitle_TreatedAsLiterals()
    {
        Assert.That(BroadcastProfilesPanel.TitleMatches("a.b+c?d", "a.b+c?d"), Is.True);
        Assert.That(BroadcastProfilesPanel.TitleMatches("a.b+c?d", "axbXcYd"), Is.False);
    }

    private static BroadcastProfilesManager CreateManager()
    {
        var logger = Substitute.For<ILogger<BroadcastProfilesManager>>();
        var settingsLogger = Substitute.For<ILogger<SettingsManager>>();
        var busLogger = Substitute.For<ILogger<InMemoryEventBus>>();
        var bus = new InMemoryEventBus(busLogger);
        var settingsManager = Substitute.For<SettingsManager>(settingsLogger, bus);
        settingsManager.Current.Returns(new AppSettings());
        var applier = Substitute.For<IChannelInformationApplier>();
        return new(settingsManager, applier, bus, TimeProvider.System, logger);
    }
}
