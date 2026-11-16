using PoproshaykaBot.Core.Platforms;
using Microsoft.Extensions.Logging;
using PoproshaykaBot.Core.Chat;
using PoproshaykaBot.Core.Chat.Commands;
using PoproshaykaBot.Core.Chat.History;
using PoproshaykaBot.Core.Infrastructure.Events.Chat;
using PoproshaykaBot.Core.Server;
using PoproshaykaBot.Core.Settings;
using PoproshaykaBot.Core.Settings.Stores;
using PoproshaykaBot.Core.Users;
using PoproshaykaBot.Core.Infrastructure.Events;

namespace PoproshaykaBot.Core.Tests.Server;

[TestFixture]
public sealed class SseChatBridgeHandlerTests
{
    private const string AddedMessageLine = "Подготовка нового сообщения чата";

    [SetUp]
    public void SetUp()
    {
        var settingsManager = Substitute.For<SettingsManager>(NullLogger<SettingsManager>.Instance, null, null);
        settingsManager.Current.Returns(new AppSettings());

        _root = Path.Combine(Path.GetTempPath(), "poproshayka-sse-bridge-" + Guid.NewGuid().ToString("N"));

        var obsChatStore = new ObsChatStore(new InMemoryEventBus(NullLogger<InMemoryEventBus>.Instance),
            NullLogger<ObsChatStore>.Instance,
            Path.Combine(_root, "obs-chat.json"));

        _logger = new();
        _bus = new(NullLogger<InMemoryEventBus>.Instance);
        _chatSettingsStore = new(null, null, Path.Combine(_root, "chat.json"));
        _service = new(settingsManager, obsChatStore, _logger, new(), new(), new());
        _handler = new(_service, _bus, _chatSettingsStore);
    }

    [TearDown]
    public async Task TearDown()
    {
        _handler.Dispose();
        await _service.DisposeAsync();

        try
        {
            Directory.Delete(_root, true);
        }
        catch (IOException)
        {
        }
    }

    private string _root = null!;
    private RecordingLogger<SseService> _logger = null!;
    private InMemoryEventBus _bus = null!;
    private ChatSettingsStore _chatSettingsStore = null!;
    private SseService _service = null!;
    private SseChatBridgeHandler _handler = null!;

    [TestCase(CommandResponseTarget.Chat, false)]
    [TestCase(CommandResponseTarget.None, false)]
    [TestCase(CommandResponseTarget.Overlay, true)]
    [TestCase(CommandResponseTarget.Chat | CommandResponseTarget.Overlay, true)]
    public async Task Эхо_ответа_команды_уходит_в_оверлей_только_с_целью_оверлея(CommandResponseTarget target, bool expected)
    {
        await _handler.HandleAsync(CreateEvent(new("ранг", target)), CancellationToken.None);

        Assert.That(AddedMessages(), Is.EqualTo(expected ? 1 : 0));
    }

    [Test]
    public async Task Обычное_сообщение_чата_в_оверлей_уходит_как_прежде()
    {
        await _handler.HandleAsync(CreateEvent(null), CancellationToken.None);

        Assert.That(AddedMessages(), Is.EqualTo(1));
    }

    [TestCase(StreamingPlatform.Twitch)]
    [TestCase(StreamingPlatform.YouTube)]
    public async Task Очистка_истории_каждой_площадки_передаётся_в_SSE(StreamingPlatform platform)
    {
        await _bus.PublishAsync(new ChatHistoryCleared(platform, "test-channel"));

        Assert.That(_logger.Entries.Count(entry => entry.Message == "Инициация события очистки чата для всех SSE клиентов"), Is.EqualTo(1));
    }

    [Test]
    public async Task Загруженная_история_уходит_в_SSE_только_при_включённой_настройке_OBS()
    {
        var scope = ChatHistoryScope.ForTwitch("1", "chan");
        var entries = new ChatMessageData[]
        {
            new()
            {
                Platform = StreamingPlatform.Twitch,
                MessageId = "h-1",
                Timestamp = DateTime.UtcNow.AddMinutes(-5),
                UserId = "u1",
                DisplayName = "user",
                Message = "past",
                IsHistorical = true,
            },
            new()
            {
                Platform = StreamingPlatform.Twitch,
                MessageId = "h-cmd",
                Timestamp = DateTime.UtcNow.AddMinutes(-4),
                UserId = "bot",
                DisplayName = "bot",
                Message = "hidden",
                IsHistorical = true,
                IsBot = true,
                CommandResponse = new("ранг", CommandSettings.CallerOnly),
            },
        };

        await _bus.PublishAsync(new ChatHistoryLoaded(scope, entries, entries));
        Assert.That(AddedMessages(), Is.Zero);

        _chatSettingsStore.Save(new() { ShowHistoryInObs = true });
        await _bus.PublishAsync(new ChatHistoryLoaded(scope, entries, entries));
        Assert.That(AddedMessages(), Is.Zero);

        _service.Start();
        await _bus.PublishAsync(new ChatHistoryLoaded(scope, entries, entries));
        Assert.That(AddedMessages(), Is.EqualTo(1));
    }

    private static ChatMessageReceived CreateEvent(CommandResponseMark? mark)
    {
        var historyEntry = new ChatMessageData
        {
            Platform = StreamingPlatform.Twitch,
            MessageId = "msg-1",
            Timestamp = DateTime.UtcNow,
            UserId = "u1",
            DisplayName = "Бот",
            Message = "ответ",
            MessageType = mark is null ? ChatMessageType.UserMessage : ChatMessageType.BotResponse,
        };

        return new(StreamingPlatform.Twitch,
            "test-channel",
            "msg-1",
            "u1",
            "bot",
            "Бот",
            "ответ",
            UserStatus.None,
            false,
            historyEntry,
            mark is not null,
            mark);
    }

    private int AddedMessages()
    {
        return _logger.Entries.Count(entry => entry.Message.Contains(AddedMessageLine, StringComparison.Ordinal));
    }
}
