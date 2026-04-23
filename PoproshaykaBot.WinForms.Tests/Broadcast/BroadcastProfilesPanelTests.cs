using Microsoft.Extensions.Logging;
using PoproshaykaBot.WinForms.Broadcast;
using PoproshaykaBot.WinForms.Broadcast.Profiles;
using PoproshaykaBot.WinForms.Infrastructure.Events;
using PoproshaykaBot.WinForms.Settings;

namespace PoproshaykaBot.WinForms.Tests.Broadcast;

[TestFixture]
[Apartment(ApartmentState.STA)]
public class BroadcastProfilesPanelTests
{
    [Test]
    public void Upsert_AfterPanelSetup_ShouldPersistThroughManager()
    {
        var manager = CreateManager();
        var resolver = Substitute.For<IGameCategoryResolver>();
        var bus = Substitute.For<IEventBus>();

        using var panel = new BroadcastProfilesPanel();
        panel.Setup(manager, resolver, bus);

        manager.Upsert(new BroadcastProfile { Name = "Профиль 1" });

        Assert.That(manager.GetAll().Select(p => p.Name), Is.EquivalentTo(new[] { "Профиль 1" }));
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
