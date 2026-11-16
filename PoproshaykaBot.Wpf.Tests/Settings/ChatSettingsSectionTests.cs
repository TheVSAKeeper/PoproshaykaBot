using Microsoft.Extensions.Logging.Abstractions;
using PoproshaykaBot.Core.Settings;
using PoproshaykaBot.Core.Settings.Chat;
using PoproshaykaBot.Core.Settings.Stores;
using PoproshaykaBot.Wpf.ViewModels.Settings;
using PoproshaykaBot.Wpf.ViewModels.Settings.Contributors;
using System.IO;

namespace PoproshaykaBot.Wpf.Tests.Settings;

[TestFixture]
public sealed class ChatSettingsSectionTests
{
    private DirectoryInfo _directory = null!;

    [SetUp]
    public void SetUp()
    {
        _directory = Directory.CreateTempSubdirectory("chat-settings-section");
    }

    [TearDown]
    public void TearDown()
    {
        _directory.Delete(recursive: true);
    }

    [Test]
    public async Task LoadEditSave_RoundTripsChatSettings_AndHonorsWriteGate()
    {
        var settingsPath = Path.Combine(_directory.FullName, "settings.json");
        var chatPath = Path.Combine(_directory.FullName, "chat.json");

        var settingsManager = new SettingsManager(NullLogger<SettingsManager>.Instance, settingsPath);
        settingsManager.Mutate(s => s.Twitch.Infrastructure.ChatHistoryMaxItems = 300);

        var store = new ChatSettingsStore(settingsManager, null, chatPath);
        var vm = new ChatSettingsSectionViewModel();
        using var contributor = new ChatSettingsSectionContributor(vm, store);

        var session = new SettingsSessionState { Settings = settingsManager.Current };
        contributor.Load(session);

        var dirtyCount = 0;
        using var dirtySub = contributor.SubscribeDirty(session, () => dirtyCount++, () => { });

        using (Assert.EnterMultipleScope())
        {
            Assert.That(vm.ShowHistoryInApp, Is.False);
            Assert.That(vm.ShowHistoryInObs, Is.False);
            Assert.That(vm.HistoryMaxItems, Is.EqualTo(300));
        }

        vm.ShowHistoryInApp = true;
        vm.ShowHistoryInObs = true;
        vm.IncreaseHistoryMaxItemsCommand.Execute(null);

        contributor.BeginSave(settingsManager.Current);
        var outcome = await contributor.SaveAsync(settingsManager.Current, appSettingsWritten: true);
        var reloaded = new ChatSettingsStore(settingsManager, null, chatPath).Load();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(dirtyCount, Is.EqualTo(3));
            Assert.That(outcome.Written, Is.True);
            Assert.That(reloaded.ShowHistoryInApp, Is.True);
            Assert.That(reloaded.ShowHistoryInObs, Is.True);
            Assert.That(reloaded.HistoryMaxItems, Is.EqualTo(400));
        }

        var closedGate = new SettingsWriteGate();
        await closedGate.RunExternalWriteAsync(() => Task.FromResult(0), _ => ["chat.json"]);
        var blockedDir = Path.Combine(_directory.FullName, "blocked");
        Directory.CreateDirectory(blockedDir);
        var blockedStore = new ChatSettingsStore(settingsManager, null, Path.Combine(blockedDir, "chat.json"), closedGate);
        using var blockedContributor = new ChatSettingsSectionContributor(new(), blockedStore);
        blockedContributor.Load(session);
        blockedContributor.BeginSave(settingsManager.Current);
        var blockedOutcome = await blockedContributor.SaveAsync(settingsManager.Current, appSettingsWritten: true);

        Assert.That(blockedOutcome.Written, Is.False);
    }
}
