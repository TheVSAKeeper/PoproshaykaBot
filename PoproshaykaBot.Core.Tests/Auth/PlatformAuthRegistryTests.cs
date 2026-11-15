using Microsoft.Extensions.DependencyInjection;
using PoproshaykaBot.Core.Debugging;
using PoproshaykaBot.Core.Infrastructure.Events;
using PoproshaykaBot.Core.Platforms;
using PoproshaykaBot.Core.Platforms.Auth;
using PoproshaykaBot.Core.Settings;
using PoproshaykaBot.Core.Streaming;
using PoproshaykaBot.Core.Twitch;
using PoproshaykaBot.Core.Twitch.Auth;
using PoproshaykaBot.Core.YouTube;
using PoproshaykaBot.Core.YouTube.Auth;

namespace PoproshaykaBot.Core.Tests.Auth;

[TestFixture]
public sealed class PlatformAuthRegistryTests
{
    [Test]
    public void TryGetAndGetRequired_ReturnRegisteredServiceAndRejectMissingOrDuplicate()
    {
        var twitch = Substitute.For<IPlatformAuthService>();
        twitch.Platform.Returns(StreamingPlatform.Twitch);

        var registry = new PlatformAuthRegistry([twitch]);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(registry.TryGet(StreamingPlatform.Twitch, out var found), Is.True);
            Assert.That(found, Is.SameAs(twitch));
            Assert.That(registry.GetRequired(StreamingPlatform.Twitch), Is.SameAs(twitch));

            Assert.That(registry.TryGet(StreamingPlatform.YouTube, out var missing), Is.False);
            Assert.That(missing, Is.Null);
            Assert.That(() => registry.GetRequired(StreamingPlatform.YouTube), Throws.TypeOf<KeyNotFoundException>());
        }

        var duplicate = Substitute.For<IPlatformAuthService>();
        duplicate.Platform.Returns(StreamingPlatform.Twitch);
        Assert.That(() => new PlatformAuthRegistry([twitch, duplicate]), Throws.InvalidOperationException);
    }

    [Test]
    public void DiContainer_ResolvesTwitchAndYouTubeServicesIntoPlatformAuthRegistry()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSettingsStores();
        services.AddSingleton(Substitute.For<IEventBus>());
        services.AddSingleton(Substitute.For<ITargetChannelProvider>());
        services.AddTwitchClients();
        services.AddYouTubeClients();

        using var provider = services.BuildServiceProvider();
        var registry = provider.GetRequiredService<IPlatformAuthRegistry>();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(registry.GetRequired(StreamingPlatform.Twitch), Is.SameAs(provider.GetRequiredService<ITwitchOAuthService>()));
            Assert.That(registry.GetRequired(StreamingPlatform.YouTube), Is.SameAs(provider.GetRequiredService<IYouTubeOAuthService>()));
        }
    }
}
