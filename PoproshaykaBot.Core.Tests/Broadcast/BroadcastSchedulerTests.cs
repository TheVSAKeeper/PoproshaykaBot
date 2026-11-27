using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using PoproshaykaBot.Core.Broadcast;
using PoproshaykaBot.Core.Chat;
using PoproshaykaBot.Core.Infrastructure.Events;
using PoproshaykaBot.Core.Infrastructure.Events.Broadcasting;
using PoproshaykaBot.Core.Platforms;
using PoproshaykaBot.Core.Settings;
using PoproshaykaBot.Core.Settings.Stores;
using PoproshaykaBot.Core.Streaming;
using PoproshaykaBot.Core.Debugging;
using PoproshaykaBot.Core.Tests.Server;
using PoproshaykaBot.Core.Twitch;
using PoproshaykaBot.Core.Twitch.Chat;
using PoproshaykaBot.Core.Twitch.Helix;

namespace PoproshaykaBot.Core.Tests.Broadcast;

[TestFixture]
public class BroadcastSchedulerTests
{
    [SetUp]
    public void SetUp()
    {
        _sendingAllowed = true;
        _onEnqueue = null;

        var targetChannel = Substitute.For<ITargetChannelProvider>();
        targetChannel.Current.Returns(_ =>
        {
            _onEnqueue?.Invoke();
            return new TargetChannelState("test-channel", "test-channel", false, _sendingAllowed);
        });

        var chatSender = new ChatSender(Substitute.For<ITwitchHelixClient>(),
            Substitute.For<IBroadcasterIdProvider>(),
            Substitute.For<IBotUserIdProvider>(),
            targetChannel,
            new(TimeProvider.System),
            NullLogger<ChatSender>.Instance);

        var settingsManager = Substitute.For<SettingsManager>(NullLogger<SettingsManager>.Instance, null);
        settingsManager.Current.Returns(new AppSettings());

        _published = new(0);
        var eventBus = Substitute.For<IEventBus>();
        eventBus.PublishAsync(Arg.Any<BroadcastSchedulerStateChanged>(), Arg.Any<CancellationToken>())
            .Returns(_ =>
            {
                _published.Release();
                return Task.CompletedTask;
            });

        _time = new();
        _logger = new();

        var messenger = new TwitchChatMessenger(chatSender,
            new AccountsStore(null, Path.Combine(Path.GetTempPath(), "accounts-" + Guid.NewGuid().ToString("N") + ".json")),
            NullLogger<TwitchChatMessenger>.Instance);
        var channels = new PlatformChannelRegistry();
        channels.SetChannel(StreamingPlatform.Twitch, "test-channel");
        var twitchPlatform = new TwitchChatPlatform(Substitute.For<IStreamStatus>(),
            messenger,
            new ChatDecorationsProvider(Substitute.For<ITwitchHelixClient>()),
            channels,
            new AccountsStore(null, Path.Combine(Path.GetTempPath(), "accounts2-" + Guid.NewGuid().ToString("N") + ".json")));
        var broadcastMessenger = new BroadcastMessenger(new ChatPlatformRegistry([twitchPlatform]),
            NullLogger<BroadcastMessenger>.Instance);

        _scheduler = new(broadcastMessenger,
            new DefaultBroadcastPlatformTargetsProvider(),
            settingsManager,
            eventBus,
            _time,
            _logger);
    }

    [TearDown]
    public async Task TearDown()
    {
        await _scheduler.DisposeAsync();
        _published.Dispose();
    }

    private bool _sendingAllowed;
    private Action? _onEnqueue;
    private SemaphoreSlim _published = null!;
    private ManualTimerProvider _time = null!;
    private RecordingLogger<BroadcastScheduler> _logger = null!;
    private BroadcastScheduler _scheduler = null!;

    [TestCase(true, true, 1)]
    [TestCase(false, true, 1)]
    [TestCase(true, false, 0)]
    [TestCase(false, false, 0)]
    public async Task Счётчик_растёт_только_когда_ChatSender_принял_сообщение_в_очередь(bool manual, bool sendingAllowed, int expectedSent)
    {
        _sendingAllowed = sendingAllowed;

        _scheduler.Start("test-channel");
        await WaitForPublishAsync();

        if (manual)
        {
            await _scheduler.ManualSendAsync();
        }
        else
        {
            _time.Tick();
            await WaitForPublishAsync();
        }

        var sentLines = _logger.Entries.Count(e => e.Level == LogLevel.Information && e.Message.Contains("принята в очередь"));
        var refusalLines = _logger.Entries.Count(e => e.Level == LogLevel.Debug && e.Message.Contains("режим наблюдателя"));

        Assert.Multiple(() =>
        {
            Assert.That(_scheduler.SentMessagesCount, Is.EqualTo(expectedSent));
            Assert.That(sentLines, Is.EqualTo(expectedSent));
            Assert.That(refusalLines, Is.EqualTo(1 - expectedSent));
            Assert.That(_scheduler.LastBroadcastAt.HasValue, Is.EqualTo(sendingAllowed));
        });
    }

    [Test]
    public async Task Отказ_не_сдвигает_номер_следующего_отправленного_сообщения()
    {
        _sendingAllowed = false;
        _scheduler.Start("test-channel");
        await WaitForPublishAsync();

        await _scheduler.ManualSendAsync();
        await _scheduler.ManualSendAsync();

        _sendingAllowed = true;
        await _scheduler.ManualSendAsync();

        Assert.Multiple(() =>
        {
            Assert.That(_scheduler.SentMessagesCount, Is.EqualTo(1));
            Assert.That(_logger.Entries.Any(e => e.Message.Contains("Номер рассылки: 1")), Is.True);
        });
    }

    [TestCase(false)]
    [TestCase(true)]
    public async Task Остановка_во_время_отправки_не_оставляет_счётчик_следующему_запуску(bool restart)
    {
        _scheduler.Start("test-channel");
        await WaitForPublishAsync();

        _onEnqueue = () =>
        {
            _onEnqueue = null;
            _scheduler.Stop();

            if (restart)
            {
                _scheduler.Start("test-channel");
            }
        };

        await _scheduler.ManualSendAsync();

        Assert.That(_scheduler.SentMessagesCount, Is.Zero);
    }

    private async Task WaitForPublishAsync()
    {
        Assert.That(await _published.WaitAsync(TimeSpan.FromSeconds(5)), Is.True, "Планировщик не опубликовал состояние");
    }

    private sealed class ManualTimerProvider : TimeProvider
    {
        private readonly List<ManualTimer> _timers = [];

        public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
        {
            var timer = new ManualTimer(callback, state);

            lock (_timers)
            {
                _timers.Add(timer);
            }

            return timer;
        }

        public void Tick()
        {
            ManualTimer[] timers;

            lock (_timers)
            {
                timers = [.. _timers];
            }

            foreach (var timer in timers)
            {
                timer.Fire();
            }
        }
    }

    private sealed class ManualTimer(TimerCallback callback, object? state) : ITimer
    {
        private bool _disposed;

        public void Fire()
        {
            if (!_disposed)
            {
                callback(state);
            }
        }

        public bool Change(TimeSpan dueTime, TimeSpan period)
        {
            return !_disposed;
        }

        public void Dispose()
        {
            _disposed = true;
        }

        public ValueTask DisposeAsync()
        {
            _disposed = true;
            return ValueTask.CompletedTask;
        }
    }
}
