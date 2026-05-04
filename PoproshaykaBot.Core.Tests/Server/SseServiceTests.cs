using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using PoproshaykaBot.Core.Chat;
using PoproshaykaBot.Core.Server;
using PoproshaykaBot.Core.Settings;
using PoproshaykaBot.Core.Tests.Server;
using System.Text;

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

    [Test]
    public async Task AddChatMessage_WithRegisteredClient_DeliversFormattedSseToClient()
    {
        _service.Start();

        var http = new DefaultHttpContext();
        var responseBody = new MemoryStream();
        http.Response.Body = responseBody;

        _service.AddClient(http.Response);

        _service.AddChatMessage(SampleMessage());

        await WaitForBytesAsync(responseBody, 1, TimeSpan.FromSeconds(2));

        var text = Encoding.UTF8.GetString(responseBody.ToArray());
        Assert.That(text, Does.StartWith("event: message\n"),
            "Клиент должен получить SSE-сообщение типа 'message'.");
    }

    [Test]
    public async Task AddChatMessage_WhenSlowClient_DoesNotBlockOtherClient()
    {
        _service.Start();

        var slowHttp = new DefaultHttpContext();
        var slowStream = new BlockingStream();
        slowHttp.Response.Body = slowStream;

        var fastHttp = new DefaultHttpContext();
        var fastStream = new MemoryStream();
        fastHttp.Response.Body = fastStream;

        _service.AddClient(slowHttp.Response);
        _service.AddClient(fastHttp.Response);

        _service.AddChatMessage(SampleMessage());

        await WaitForBytesAsync(fastStream, 1, TimeSpan.FromSeconds(2));

        slowStream.Release();

        Assert.That(fastStream.Length, Is.GreaterThan(0),
            "Быстрый клиент должен получить сообщение независимо от заблокированного клиента.");
    }

    private static async Task WaitForBytesAsync(MemoryStream stream, long expectedAtLeast, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            if (stream.Length >= expectedAtLeast)
            {
                return;
            }

            await Task.Delay(20);
        }

        throw new TimeoutException($"Ожидалось >= {expectedAtLeast} байт в потоке, но за {timeout.TotalSeconds:F1}с пришло {stream.Length}.");
    }

    private sealed class BlockingStream : MemoryStream
    {
        private readonly TaskCompletionSource _gate = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public override async Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
        {
            await _gate.Task.WaitAsync(cancellationToken);
            await base.WriteAsync(buffer, offset, count, cancellationToken);
        }

        public override async ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
        {
            await _gate.Task.WaitAsync(cancellationToken);
            await base.WriteAsync(buffer, cancellationToken);
        }

        public void Release()
        {
            _gate.TrySetResult();
        }
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
