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

    private static BroadcastProfilesManager CreateManager()
    {
        var logger = Substitute.For<ILogger<BroadcastProfilesManager>>();
        var settingsLogger = Substitute.For<ILogger<SettingsManager>>();
        var busLogger = Substitute.For<ILogger<InMemoryEventBus>>();
        var bus = new InMemoryEventBus(busLogger);
        var settingsManager = Substitute.For<SettingsManager>(settingsLogger, bus);
        settingsManager.Current.Returns(new AppSettings());
        var applier = Substitute.For<IChannelInformationApplier>();
        return new(settingsManager, applier, bus, logger);
    }
}
