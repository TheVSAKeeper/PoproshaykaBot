using Microsoft.Extensions.Logging;
using PoproshaykaBot.WinForms.Chat;
using PoproshaykaBot.WinForms.Server;
using PoproshaykaBot.WinForms.Settings;

namespace PoproshaykaBot.WinForms.Tests.Server;

[TestFixture]
public sealed class SseServiceTests
{
    [SetUp]
    public void SetUp()
    {
        _settings = new();
        _settingsManager = Substitute.For<SettingsManager>(NullLogger<SettingsManager>.Instance);

        _settingsManager.Current.Returns(_settings);
        _logger = new();
        _service = new(_settingsManager, _logger);
    }

    [TearDown]
    public async Task TearDown()
    {
        await _service.DisposeAsync();
    }

    private AppSettings _settings = null!;
    private SettingsManager _settingsManager = null!;
    private RecordingLogger<SseService> _logger = null!;
    private SseService _service = null!;

    [Test]
    public void DroppedMessageCount_FreshService_IsZero()
    {
        Assert.That(_service.DroppedMessageCount, Is.EqualTo(0));
    }

    [Test]
    public void AddChatMessage_WhenQueueOverflows_IncrementsDroppedCount()
    {
        var msg = SampleMessage();

        for (var i = 0; i < 1024; i++)
        {
            _service.AddChatMessage(msg);
        }

        Assert.That(_service.DroppedMessageCount, Is.GreaterThan(0),
            "При переполнении канала DropOldest должен быть учтён в DroppedMessageCount.");
    }

    [Test]
    public void AddChatMessage_WhenQueueOverflows_LogsWarningAtLeastOnce()
    {
        var msg = SampleMessage();

        for (var i = 0; i < 1024; i++)
        {
            _service.AddChatMessage(msg);
        }

        Assert.That(_logger.Entries.Any(e => e.Level == LogLevel.Warning && e.Message.Contains("очередь", StringComparison.OrdinalIgnoreCase)),
            Is.True,
            "При переполнении ожидался Warning о потере SSE-сообщений.");
    }

    [Test]
    public void AddChatMessage_BelowCapacity_DoesNotDrop()
    {
        var msg = SampleMessage();

        for (var i = 0; i < 100; i++)
        {
            _service.AddChatMessage(msg);
        }

        Assert.That(_service.DroppedMessageCount, Is.EqualTo(0));
    }

    private static ChatMessageData SampleMessage()
    {
        return new()
        {
            Timestamp = DateTime.UtcNow,
            DisplayName = "tester",
            Message = "hello",
            MessageType = ChatMessageType.UserMessage,
        };
    }
}
