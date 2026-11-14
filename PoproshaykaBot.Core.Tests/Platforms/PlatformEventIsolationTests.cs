using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using PoproshaykaBot.Core.Chat;
using PoproshaykaBot.Core.Chat.Handlers;
using PoproshaykaBot.Core.Debugging;
using PoproshaykaBot.Core.Infrastructure.Events;
using PoproshaykaBot.Core.Infrastructure.Events.Chat;
using PoproshaykaBot.Core.Infrastructure.Events.Streaming;
using PoproshaykaBot.Core.Platforms;
using PoproshaykaBot.Core.Server;
using PoproshaykaBot.Core.Settings;
using PoproshaykaBot.Core.Settings.Stores;
using PoproshaykaBot.Core.Statistics;
using PoproshaykaBot.Core.Streaming;
using PoproshaykaBot.Core.Tests.Server;
using PoproshaykaBot.Core.Users;

namespace PoproshaykaBot.Core.Tests.Platforms;

[TestFixture]
public sealed class PlatformEventIsolationTests
{
    private const string ClearChatLogLine = "Инициация события очистки чата для всех SSE клиентов";

    private static readonly HashSet<(Type Handler, Type EventType)> ExplicitPlatformNeutralHandlers =
    [
        (typeof(StatisticsTrackingHandler), typeof(ChatMessageReceived)),
        (typeof(ChatHistoryLoggingHandler), typeof(ChatMessageReceived)),
        (typeof(SseChatBridgeHandler), typeof(ChatMessageReceived)),
    ];

    [Test]
    public async Task TwitchSubscribedCoreHandlers_IgnoreYouTubeEvents_WhileNeutralHandlersProcessThem()
    {
        var root = Path.Combine(Path.GetTempPath(), "poproshayka-isolation-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        var bus = new InMemoryEventBus(NullLogger<InMemoryEventBus>.Instance);

        var commandUsage = new CommandUsageRepository(TimeProvider.System);
        commandUsage.Track("ранг", "ранг");
        using var resetHandler = new CommandUsageStreamResetHandler(
            commandUsage,
            bus,
            NullLogger<CommandUsageStreamResetHandler>.Instance);

        var statsRepository = new UserStatisticsRepository(NullLogger<UserStatisticsRepository>.Instance);
        var botStats = Substitute.For<IBotStatisticsRepository>();
        var targetChannel = Substitute.For<ITargetChannelProvider>();
        targetChannel.Current.Returns(new TargetChannelState("channel", "channel", false, true));
        using var trackingHandler = new StatisticsTrackingHandler(
            statsRepository,
            botStats,
            targetChannel,
            bus);

        var settingsManager = Substitute.For<SettingsManager>(NullLogger<SettingsManager>.Instance, null, null);
        settingsManager.Current.Returns(new AppSettings());
        var obsChatStore = new ObsChatStore(bus, NullLogger<ObsChatStore>.Instance, Path.Combine(root, "obs-chat.json"));
        var sseLogger = new RecordingLogger<SseService>();
        var sseService = new SseService(settingsManager, obsChatStore, sseLogger, new(), new(), new());
        using var bridge = new SseChatBridgeHandler(sseService, bus);

        try
        {
            await bus.PublishAsync(new StreamWentOnline(StreamingPlatform.YouTube, "yt-channel", new StreamInfo { Id = "yt-stream" }));
            Assert.That(commandUsage.GetSnapshot().Single().StreamCount, Is.EqualTo(1),
                "Raw StreamWentOnline must not bypass BroadcastAggregator for CommandUsageStreamResetHandler");

            await bus.PublishAsync(new BroadcastStarted(Guid.NewGuid(), StreamingPlatform.Twitch, "tw-channel", new StreamInfo { Id = "tw-stream" }, DateTimeOffset.UtcNow));
            Assert.That(commandUsage.GetSnapshot().Single().StreamCount, Is.Zero);

            await bus.PublishAsync(new ChatHistoryCleared(StreamingPlatform.YouTube, "yt-channel"));
            Assert.That(sseLogger.Entries.Count(entry => entry.Message.Contains(ClearChatLogLine, StringComparison.Ordinal)), Is.Zero);

            await bus.PublishAsync(new ChatHistoryCleared(StreamingPlatform.Twitch, "tw-channel"));
            Assert.That(sseLogger.Entries.Count(entry => entry.Message.Contains(ClearChatLogLine, StringComparison.Ordinal)), Is.EqualTo(1));

            await bus.PublishAsync(new ChatMessageReceived(
                StreamingPlatform.YouTube,
                "yt-channel",
                "msg-1",
                "yt-user-1",
                "ytuser",
                "YtUser",
                "hello youtube",
                UserStatus.None,
                false,
                new() { Platform = StreamingPlatform.YouTube },
                false));

            Assert.That(statsRepository.ForPlatform(StreamingPlatform.YouTube).GetById("yt-user-1")!.MessageCount, Is.EqualTo(1ul));
            Assert.That(statsRepository.ForPlatform(StreamingPlatform.Twitch).GetById("yt-user-1"), Is.Null);
        }
        finally
        {
            await sseService.DisposeAsync();
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    [Test]
    public void PlatformEventHandlers_DoNotSubscribeWithoutFilteringUnlessAllowlisted()
    {
        var coreAssembly = typeof(IPlatformEvent).Assembly;
        var subscriberTypes = coreAssembly.GetTypes()
            .Where(static t => !t.IsAbstract && !t.IsInterface && typeof(IEventSubscriber).IsAssignableFrom(t));

        var unlisted = new List<string>();

        foreach (var type in subscriberTypes)
        {
            foreach (var iface in type.GetInterfaces())
            {
                if (!iface.IsGenericType || iface.GetGenericTypeDefinition() != typeof(IEventHandler<>))
                {
                    continue;
                }

                var eventType = iface.GetGenericArguments()[0];
                if (!typeof(IPlatformEvent).IsAssignableFrom(eventType))
                {
                    continue;
                }

                var isNeutral = ExplicitPlatformNeutralHandlers.Contains((type, eventType));
                var hasCtorTakingBus = type.GetConstructors().Any(static c => c.GetParameters().Any(static p => p.ParameterType == typeof(IEventBus)));

                if (!isNeutral && !hasCtorTakingBus)
                {
                    unlisted.Add($"{type.Name}<{eventType.Name}>");
                }
            }
        }

        Assert.That(unlisted, Is.Empty);
    }
}
