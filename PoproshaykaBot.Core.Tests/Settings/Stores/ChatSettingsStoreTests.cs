using Microsoft.Extensions.Logging.Abstractions;
using PoproshaykaBot.Core.Settings;
using PoproshaykaBot.Core.Settings.Chat;
using PoproshaykaBot.Core.Settings.Stores;

namespace PoproshaykaBot.Core.Tests.Settings.Stores;

[TestFixture]
public sealed class ChatSettingsStoreTests
{
    private DirectoryInfo _directory = null!;

    [SetUp]
    public void SetUp()
    {
        _directory = Directory.CreateTempSubdirectory("chat-settings-store");
    }

    [TearDown]
    public void TearDown()
    {
        _directory.Delete(recursive: true);
    }

    [Test]
    public void Defaults_HaveBothHistoryTogglesDisabled_AndIndependent()
    {
        var path = Path.Combine(_directory.FullName, "chat.json");
        var store = new ChatSettingsStore(null, null, path);

        var loaded = store.Load();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(loaded.ShowHistoryInApp, Is.False);
            Assert.That(loaded.ShowHistoryInObs, Is.False);
            Assert.That(loaded.HistoryMaxItems, Is.EqualTo(ChatSettings.DefaultHistoryMaxItems));
        }

        store.Save(new() { ShowHistoryInApp = true, ShowHistoryInObs = false, HistoryMaxItems = 350 });
        var reloaded = new ChatSettingsStore(null, null, path).Load();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(reloaded.ShowHistoryInApp, Is.True);
            Assert.That(reloaded.ShowHistoryInObs, Is.False);
            Assert.That(reloaded.HistoryMaxItems, Is.EqualTo(350));
        }
    }

    [Test]
    public void Fallback_UsesLegacyTwitchHistoryLimit_UntilChatJsonIsSaved()
    {
        var settingsPath = Path.Combine(_directory.FullName, "settings.json");
        var chatPath = Path.Combine(_directory.FullName, "chat.json");

        var settingsManager = new SettingsManager(NullLogger<SettingsManager>.Instance, settingsPath);
        settingsManager.Mutate(settings => settings.Twitch.Infrastructure.ChatHistoryMaxItems = 250);

        var store = new ChatSettingsStore(settingsManager, null, chatPath);

        Assert.That(store.Load().HistoryMaxItems, Is.EqualTo(250));
        Assert.That(File.Exists(chatPath), Is.False);

        store.Save(new() { ShowHistoryInObs = true, HistoryMaxItems = 750 });

        var reloaded = new ChatSettingsStore(settingsManager, null, chatPath);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(reloaded.Load().HistoryMaxItems, Is.EqualTo(750));
            Assert.That(settingsManager.Current.Twitch.Infrastructure.ChatHistoryMaxItems, Is.EqualTo(250));
        }
    }
}
