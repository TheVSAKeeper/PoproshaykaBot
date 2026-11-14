using Microsoft.Extensions.DependencyInjection;
using PoproshaykaBot.Core.Broadcast;
using PoproshaykaBot.Core.Chat;
using PoproshaykaBot.Core.Debugging;
using PoproshaykaBot.Core.Diagnostics;
using PoproshaykaBot.Core.Infrastructure.Di;
using PoproshaykaBot.Core.Infrastructure.Hosting;
using PoproshaykaBot.Core.Infrastructure.Hosting.Components;
using PoproshaykaBot.Core.Infrastructure.Logging;
using PoproshaykaBot.Core.Obs;
using PoproshaykaBot.Core.Platforms;
using PoproshaykaBot.Core.Polls;
using PoproshaykaBot.Core.Server;
using PoproshaykaBot.Core.Settings;
using PoproshaykaBot.Core.Statistics;
using PoproshaykaBot.Core.Streaming;
using PoproshaykaBot.Core.Twitch;
using PoproshaykaBot.Core.Twitch.Chat;
using PoproshaykaBot.Core.Twitch.EventSub;
using PoproshaykaBot.Core.Twitch.GameArt;
using PoproshaykaBot.Core.Update;
using PoproshaykaBot.Core.Users.Avatars;

namespace PoproshaykaBot.Core.Tests.Infrastructure.Hosting;

[TestFixture]
public sealed class HostCompositionTests
{
    [Test]
    public async Task CoreContainer_BuildsWithValidation_AndSegregatesSharedAndPlatformHosts()
    {
        var services = new ServiceCollection();
        services
            .AddCoreInfrastructure(new UiLogSink(), disposeSerilog: false)
            .AddStatistics()
            .AddSettingsStores()
            .AddDebugChannel(DebugChannelOverride.None)
            .AddTwitchClients()
            .AddGameBoxArt(cacheOnly: true)
            .AddUserAvatars(cacheOnly: true)
            .AddChatPipeline()
            .AddStreamMonitoring()
            .AddBroadcasting()
            .AddPolls()
            .AddHttpServer()
            .AddObsIntegration()
            .AddSelfUpdate()
            .AddDiagnostics();

        services.AddSingleton<BotConnectionManager>();
        services.AddSingleton<IBotConnectionController>(sp => sp.GetRequiredService<BotConnectionManager>());

        await using var provider = services.BuildServiceProvider(new ServiceProviderOptions
        {
            ValidateOnBuild = true,
            ValidateScopes = true
        });

        Assert.That(provider.GetServices<IHostedComponent>().ToList(), Is.Empty);
        Assert.That(provider.GetServices<IStreamHostedComponent>().ToList(), Is.Empty);

        var sharedHost = provider.GetRequiredService<AppHost>();
        Assert.That(sharedHost.Components.Select(c => c.GetType()).ToArray(), Is.EqualTo(new[]
        {
            typeof(StatisticsAutoSaver),
            typeof(BroadcastSchedulerHostedComponent)
        }));

        var platformHosts = provider.GetServices<PlatformHost>().ToList();
        Assert.That(platformHosts, Has.Count.EqualTo(1));
        var twitchHost = provider.GetRequiredKeyedService<PlatformHost>(StreamingPlatform.Twitch);
        Assert.That(platformHosts[0], Is.SameAs(twitchHost));
        Assert.That(twitchHost.Platform, Is.EqualTo(StreamingPlatform.Twitch));
        Assert.That(twitchHost.Components.Select(c => c.GetType()).ToArray(), Is.EqualTo(new[]
        {
            typeof(ChatSender),
            typeof(PollHistoryStore),
            typeof(ChatDecorationsHostedComponent),
            typeof(ChatIngestionService)
        }));

        var monitoringHosts = provider.GetServices<StreamMonitoringHost>().ToList();
        Assert.That(monitoringHosts, Has.Count.EqualTo(1));
        var twitchMonitoringHost = provider.GetRequiredKeyedService<StreamMonitoringHost>(StreamingPlatform.Twitch);
        Assert.That(monitoringHosts[0], Is.SameAs(twitchMonitoringHost));
        Assert.That(twitchMonitoringHost.Platform, Is.EqualTo(StreamingPlatform.Twitch));
        Assert.That(twitchMonitoringHost.Components.Select(c => c.GetType()).ToArray(), Is.EquivalentTo(new[]
        {
            typeof(EventSubConnectionHost),
            typeof(EventSubConnectionHost),
            typeof(StreamStatusManager),
            typeof(StreamStatusWatchdog),
            typeof(ChannelUpdateSubscriber),
            typeof(PollEventSubscriber)
        }));

        var adapters = provider.GetServices<IPlatformConnectionAdapter>().ToList();
        Assert.That(adapters, Has.Count.EqualTo(1));
        Assert.That(adapters[0], Is.InstanceOf<TwitchPlatformConnectionAdapter>());
        Assert.That(adapters[0].Platform, Is.EqualTo(StreamingPlatform.Twitch));
        Assert.That(adapters[0].Host, Is.SameAs(twitchHost));

        _ = provider.GetRequiredService<IBotConnectionController>();
    }
}
