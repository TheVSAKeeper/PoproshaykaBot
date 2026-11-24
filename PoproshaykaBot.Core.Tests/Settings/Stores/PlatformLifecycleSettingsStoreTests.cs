using Microsoft.Extensions.Logging.Abstractions;
using PoproshaykaBot.Core.Platforms;
using PoproshaykaBot.Core.Settings;
using PoproshaykaBot.Core.Settings.Stores;
using PoproshaykaBot.Core.Streaming;

namespace PoproshaykaBot.Core.Tests.Settings.Stores;

[TestFixture]
public sealed class PlatformLifecycleSettingsStoreTests
{
    private DirectoryInfo _directory = null!;

    [SetUp]
    public void SetUp()
    {
        _directory = Directory.CreateTempSubdirectory("platform-lifecycle-store");
    }

    [TearDown]
    public void TearDown()
    {
        _directory.Delete(recursive: true);
    }

    [Test]
    public void Fallback_UsesLegacyTwitchSettings_AndDisablesOthers_UntilSaved()
    {
        var settingsPath = Path.Combine(_directory.FullName, "settings.json");
        var lifecyclePath = Path.Combine(_directory.FullName, "platform-lifecycle.json");

        var settingsManager = new SettingsManager(NullLogger<SettingsManager>.Instance, settingsPath);
        settingsManager.Mutate(settings =>
        {
            settings.Twitch.BotLifecycleAutomation.AutoConnectOnStreamOnline = true;
            settings.Twitch.BotLifecycleAutomation.AutoDisconnectOnStreamOffline = false;
        });

        var store = new PlatformLifecycleSettingsStore(settingsManager, null, lifecyclePath);
        var provider = new BotLifecycleAutomationSettingsProvider(store);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(provider.Get(StreamingPlatform.Twitch).AutoConnectOnStreamOnline, Is.True);
            Assert.That(provider.Get(StreamingPlatform.Twitch).AutoDisconnectOnStreamOffline, Is.False);
            Assert.That(provider.Get(StreamingPlatform.YouTube).AutoConnectOnStreamOnline, Is.False);
            Assert.That(provider.Get(StreamingPlatform.YouTube).AutoDisconnectOnStreamOffline, Is.False);
            Assert.That(provider.Get(StreamingPlatform.VkLive).AutoConnectOnStreamOnline, Is.False);
            Assert.That(File.Exists(lifecyclePath), Is.False);
        }

        store.Save(new()
        {
            Platforms =
            {
                [StreamingPlatform.Twitch] = new() { AutoConnectOnStreamOnline = false, AutoDisconnectOnStreamOffline = true },
                [StreamingPlatform.YouTube] = new() { AutoConnectOnStreamOnline = true, AutoDisconnectOnStreamOffline = true },
                [StreamingPlatform.VkLive] = new() { AutoConnectOnStreamOnline = true, AutoDisconnectOnStreamOffline = false },
            },
        });

        var reloaded = new PlatformLifecycleSettingsProviderScope(settingsManager, lifecyclePath);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(reloaded.Provider.Get(StreamingPlatform.Twitch).AutoConnectOnStreamOnline, Is.False);
            Assert.That(reloaded.Provider.Get(StreamingPlatform.Twitch).AutoDisconnectOnStreamOffline, Is.True);
            Assert.That(reloaded.Provider.Get(StreamingPlatform.YouTube).AutoConnectOnStreamOnline, Is.True);
            Assert.That(reloaded.Provider.Get(StreamingPlatform.YouTube).AutoDisconnectOnStreamOffline, Is.True);
            Assert.That(reloaded.Provider.Get(StreamingPlatform.VkLive).AutoConnectOnStreamOnline, Is.True);
            Assert.That(reloaded.Provider.Get(StreamingPlatform.VkLive).AutoDisconnectOnStreamOffline, Is.False);
        }
    }

    private sealed class PlatformLifecycleSettingsProviderScope
    {
        public PlatformLifecycleSettingsProviderScope(SettingsManager settingsManager, string filePath)
        {
            Store = new(settingsManager, null, filePath);
            Provider = new(Store);
        }

        public PlatformLifecycleSettingsStore Store { get; }

        public BotLifecycleAutomationSettingsProvider Provider { get; }
    }
}
