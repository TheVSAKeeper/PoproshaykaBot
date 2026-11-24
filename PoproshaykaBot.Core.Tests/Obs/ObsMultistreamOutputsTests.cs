using PoproshaykaBot.Core.Obs;
using PoproshaykaBot.Core.Settings;
using PoproshaykaBot.Core.Settings.Obs;
using System.Text.Json;

namespace PoproshaykaBot.Core.Tests.Obs;

[TestFixture]
public sealed class ObsMultistreamOutputsTests
{
    private AppSettings _settings = null!;
    private SettingsManager _settingsManager = null!;
    private FakeObsWebSocketClient _client = null!;
    private ObsIntegrationService _service = null!;

    [SetUp]
    public void SetUp()
    {
        _settings = new();
        _settingsManager = Substitute.For<SettingsManager>(NullLogger<SettingsManager>.Instance, null, null);
        _settingsManager.Current.Returns(_settings);
        _client = new();
        _service = new(_client, _settingsManager, NullLogger<ObsIntegrationService>.Instance);
    }

    [TearDown]
    public void TearDown()
    {
        _service.Dispose();
    }

    [Test]
    public async Task Автодетект_приоритетно_выбирает_AitumStreamSuite_и_читает_телеметрию()
    {
        EnqueueBasicDashboardResponses(streaming: true);
        _client.EnqueueVendorResponse(
            "aitum-stream-suite",
            "version",
            """{"vendorName":"aitum-stream-suite","requestType":"version","responseData":{"version":"1.2.0","success":true}}""");
        _client.EnqueueVendorResponse(
            "aitum-stream-suite",
            "get_outputs",
            """
            {
              "vendorName": "aitum-stream-suite",
              "requestType": "get_outputs",
              "responseData": {
                "success": true,
                "outputs": [
                  {"name":"YouTube","type":"stream","active":true,"reconnecting":false,"total_frames":1000,"dropped_frames":5,"total_bytes":1048576},
                  {"name":"Kick","type":"stream","active":false,"reconnecting":false,"total_frames":0,"dropped_frames":0,"total_bytes":0}
                ]
              }
            }
            """);
        _client.EnqueueResponse(
            "GetOutputList",
            """
            {
              "outputs": [
                {"outputName":"adv_stream","outputKind":"rtmp_output","outputActive":true},
                {"outputName":"Aitum Stream Suite Output YouTube","outputKind":"rtmp_output","outputActive":true}
              ]
            }
            """);
        _client.EnqueueOutputStatus(
            "Aitum Stream Suite Output YouTube",
            """
            {
              "outputActive": true,
              "outputReconnecting": false,
              "outputTimecode": "00:15:30.000",
              "outputDuration": 930000,
              "outputBytes": 2097152,
              "outputCongestion": 0.02,
              "outputSkippedFrames": 12,
              "outputTotalFrames": 2000
            }
            """);

        var snapshot = await _service.GetDashboardSnapshotAsync(new() { Enabled = true }, true, CancellationToken.None);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(snapshot.SecondaryOutputs, Has.Count.EqualTo(2));

            var youtube = snapshot.SecondaryOutputs[0];
            Assert.That(youtube.Name, Is.EqualTo("YouTube"));
            Assert.That(youtube.OutputId, Is.EqualTo("Aitum Stream Suite Output YouTube"));
            Assert.That(youtube.Vendor, Is.EqualTo(ObsMultistreamVendor.AitumStreamSuite));
            Assert.That(youtube.IsActive, Is.True);
            Assert.That(youtube.Timecode, Is.EqualTo("00:15:30.000"));
            Assert.That(youtube.Bytes, Is.EqualTo(2097152));
            Assert.That(youtube.SkippedFrames, Is.EqualTo(12));
            Assert.That(youtube.TotalFrames, Is.EqualTo(2000));
            Assert.That(youtube.CanControl, Is.True);

            var kick = snapshot.SecondaryOutputs[1];
            Assert.That(kick.Name, Is.EqualTo("Kick"));
            Assert.That(kick.IsActive, Is.False);
            Assert.That(kick.CanControl, Is.True);
        }
    }

    [Test]
    public async Task Автодетект_переходит_на_AitumMultistream_при_ошибке_600()
    {
        EnqueueBasicDashboardResponses(streaming: true);
        _client.EnqueueVendorError("aitum-stream-suite", "version", 600, "No vendor was found by that name.");
        _client.EnqueueVendorResponse(
            "aitum-multistream",
            "version",
            """{"vendorName":"aitum-multistream","requestType":"version","responseData":{"version":"1.0.9","success":true}}""");
        _client.EnqueueVendorResponse(
            "aitum-multistream",
            "get_outputs",
            """
            {
              "vendorName": "aitum-multistream",
              "requestType": "get_outputs",
              "responseData": {
                "success": true,
                "outputs": [
                  {"name":"Вывод YouTube","vertical":false},
                  {"name":"Шортсы","vertical":true}
                ]
              }
            }
            """);
        _client.EnqueueResponse(
            "GetOutputList",
            """
            {
              "outputs": [
                {"outputName":"adv_stream","outputKind":"rtmp_output","outputActive":true},
                {"outputName":"aitum_multi_output_Вывод YouTube","outputKind":"rtmp_output","outputActive":true}
              ]
            }
            """);
        _client.EnqueueOutputStatus(
            "aitum_multi_output_Вывод YouTube",
            """
            {
              "outputActive": true,
              "outputReconnecting": false,
              "outputTimecode": "00:05:10.000",
              "outputDuration": 310000,
              "outputBytes": 524288,
              "outputCongestion": 0.0,
              "outputSkippedFrames": 3,
              "outputTotalFrames": 1000
            }
            """);

        var snapshot = await _service.GetDashboardSnapshotAsync(new() { Enabled = true }, true, CancellationToken.None);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(snapshot.SecondaryOutputs, Has.Count.EqualTo(2));

            var youtube = snapshot.SecondaryOutputs[0];
            Assert.That(youtube.Name, Is.EqualTo("Вывод YouTube"));
            Assert.That(youtube.OutputId, Is.EqualTo("aitum_multi_output_Вывод YouTube"));
            Assert.That(youtube.Vendor, Is.EqualTo(ObsMultistreamVendor.AitumMultistream));
            Assert.That(youtube.IsActive, Is.True);
            Assert.That(youtube.IsVertical, Is.False);
            Assert.That(youtube.Timecode, Is.EqualTo("00:05:10.000"));
            Assert.That(youtube.SkippedFrames, Is.EqualTo(3));
            Assert.That(youtube.CanControl, Is.True);

            var shorts = snapshot.SecondaryOutputs[1];
            Assert.That(shorts.Name, Is.EqualTo("Шортсы"));
            Assert.That(shorts.IsActive, Is.False);
            Assert.That(shorts.IsVertical, Is.True);
            Assert.That(shorts.CanControl, Is.True);
        }
    }

    [Test]
    public async Task Отсутствие_Aitum_включает_readonly_fallback_из_GetOutputList_без_основных_выходов()
    {
        EnqueueBasicDashboardResponses(streaming: true);
        _client.EnqueueVendorError("aitum-stream-suite", "version", 600, "No vendor was found by that name.");
        _client.EnqueueVendorError("aitum-multistream", "version", 600, "No vendor was found by that name.");
        _client.EnqueueResponse(
            "GetOutputList",
            """
            {
              "outputs": [
                {"outputName":"adv_stream","outputKind":"rtmp_output","outputActive":true},
                {"outputName":"simple_stream","outputKind":"rtmp_output","outputActive":true},
                {"outputName":"adv_file_output","outputKind":"ffmpeg_muxer","outputActive":true},
                {"outputName":"replay_buffer","outputKind":"replay_buffer","outputActive":true},
                {"outputName":"Custom RTMP","outputKind":"rtmp_output","outputActive":true}
              ]
            }
            """);
        _client.EnqueueOutputStatus(
            "Custom RTMP",
            """
            {
              "outputActive": true,
              "outputReconnecting": true,
              "outputTimecode": "00:01:02.000",
              "outputDuration": 62000,
              "outputBytes": 123456,
              "outputCongestion": 0.1,
              "outputSkippedFrames": 2,
              "outputTotalFrames": 500
            }
            """);

        var snapshot = await _service.GetDashboardSnapshotAsync(new() { Enabled = true }, true, CancellationToken.None);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(snapshot.SecondaryOutputs, Has.Count.EqualTo(1));
            var custom = snapshot.SecondaryOutputs[0];
            Assert.That(custom.Name, Is.EqualTo("Custom RTMP"));
            Assert.That(custom.Vendor, Is.EqualTo(ObsMultistreamVendor.ObsOutput));
            Assert.That(custom.CanControl, Is.False);
            Assert.That(custom.IsActive, Is.True);
            Assert.That(custom.IsReconnecting, Is.True);
            Assert.That(custom.Timecode, Is.EqualTo("00:01:02.000"));
        }
    }

    [Test]
    public async Task Запуск_доп_выхода_блокируется_при_неактивном_основном_эфире()
    {
        await _service.ConnectAsync(new() { Enabled = true }, CancellationToken.None);
        _client.EnqueueResponse("GetStreamStatus", """{"outputActive":false}""");

        var exception = Assert.ThrowsAsync<InvalidOperationException>(() =>
            _service.StartSecondaryOutputAsync("Вывод YouTube", CancellationToken.None));

        using (Assert.EnterMultipleScope())
        {
            Assert.That(exception?.Message, Is.EqualTo("Сначала запустите основной эфир OBS"));
            Assert.That(_client.Requests.Exists(r => string.Equals(r.RequestType, "CallVendorRequest", StringComparison.Ordinal)), Is.False);
        }
    }

    [Test]
    public async Task Старт_и_стоп_доп_выхода_нормализуют_префикс_и_учитывают_ключи_вендоров()
    {
        await _service.ConnectAsync(new() { Enabled = true }, CancellationToken.None);
        _client.EnqueueVendorResponse(
            "aitum-stream-suite",
            "version",
            """{"vendorName":"aitum-stream-suite","requestType":"version","responseData":{"version":"1.2.0","success":true}}""");
        _client.EnqueueResponse("GetStreamStatus", """{"outputActive":true}""");
        _client.EnqueueVendorResponse(
            "aitum-stream-suite",
            "start_output",
            """{"vendorName":"aitum-stream-suite","requestType":"start_output","responseData":{"success":true}}""");

        await _service.StartSecondaryOutputAsync("Aitum Stream Suite Output YouTube", CancellationToken.None);

        var suiteStart = _client.Requests.Last(r => string.Equals(r.RequestType, "CallVendorRequest", StringComparison.Ordinal));
        using (Assert.EnterMultipleScope())
        {
            Assert.That(suiteStart.RequestData!.Value.GetProperty("vendorName").GetString(), Is.EqualTo("aitum-stream-suite"));
            Assert.That(suiteStart.RequestData!.Value.GetProperty("requestType").GetString(), Is.EqualTo("start_output"));
            Assert.That(suiteStart.RequestData!.Value.GetProperty("requestData").GetProperty("output").GetString(), Is.EqualTo("YouTube"));
        }

        await _service.DisconnectAsync(CancellationToken.None);
        await _service.ConnectAsync(new() { Enabled = true }, CancellationToken.None);

        _client.EnqueueVendorError("aitum-stream-suite", "version", 600, "No vendor was found by that name.");
        _client.EnqueueVendorResponse(
            "aitum-multistream",
            "version",
            """{"vendorName":"aitum-multistream","requestType":"version","responseData":{"version":"1.0.9","success":true}}""");
        _client.EnqueueVendorResponse(
            "aitum-multistream",
            "stop_output",
            """{"vendorName":"aitum-multistream","requestType":"stop_output","responseData":{"success":true}}""");

        await _service.StopSecondaryOutputAsync("aitum_multi_output_Вывод YouTube", CancellationToken.None);

        var multiStop = _client.Requests.Last(r => string.Equals(r.RequestType, "CallVendorRequest", StringComparison.Ordinal));
        using (Assert.EnterMultipleScope())
        {
            Assert.That(multiStop.RequestData!.Value.GetProperty("vendorName").GetString(), Is.EqualTo("aitum-multistream"));
            Assert.That(multiStop.RequestData!.Value.GetProperty("requestType").GetString(), Is.EqualTo("stop_output"));
            Assert.That(multiStop.RequestData!.Value.GetProperty("requestData").GetProperty("name").GetString(), Is.EqualTo("Вывод YouTube"));
        }
    }

    [Test]
    public void Разбор_VendorEvent_нормализует_события_обоих_вендоров()
    {
        using var multiDoc = JsonDocument.Parse("""
            {
              "vendorName": "aitum-multistream",
              "eventType": "output_started",
              "eventData": {"name": "aitum_multi_output_Вывод YouTube"}
            }
            """);
        using var suiteDoc = JsonDocument.Parse("""
            {
              "vendorName": "aitum-stream-suite",
              "eventType": "stop_output",
              "eventData": {"output": "Aitum Stream Suite Output Kick", "code": -1, "last_error": "disconnected"}
            }
            """);

        var multiParsed = ObsMultistreamOutputsClient.TryParseVendorEvent(
            new("VendorEvent", 512, multiDoc.RootElement.Clone()),
            out var multiEvent);
        var suiteParsed = ObsMultistreamOutputsClient.TryParseVendorEvent(
            new("VendorEvent", 512, suiteDoc.RootElement.Clone()),
            out var suiteEvent);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(multiParsed, Is.True);
            Assert.That(multiEvent, Is.Not.Null);
            Assert.That(multiEvent!.Vendor, Is.EqualTo(ObsMultistreamVendor.AitumMultistream));
            Assert.That(multiEvent.Kind, Is.EqualTo(ObsSecondaryOutputEventKind.Started));
            Assert.That(multiEvent.OutputName, Is.EqualTo("Вывод YouTube"));

            Assert.That(suiteParsed, Is.True);
            Assert.That(suiteEvent, Is.Not.Null);
            Assert.That(suiteEvent!.Vendor, Is.EqualTo(ObsMultistreamVendor.AitumStreamSuite));
            Assert.That(suiteEvent.Kind, Is.EqualTo(ObsSecondaryOutputEventKind.Stopped));
            Assert.That(suiteEvent.OutputName, Is.EqualTo("Kick"));
            Assert.That(suiteEvent.ErrorCode, Is.EqualTo(-1));
            Assert.That(suiteEvent.LastError, Is.EqualTo("disconnected"));
        }
    }

    private void EnqueueBasicDashboardResponses(bool streaming)
    {
        _client.EnqueueResponse("GetVersion", """{"obsVersion":"33.0.1","obsWebSocketVersion":"5.7.6"}""");
        _client.EnqueueResponse("GetCurrentProgramScene", """{"currentProgramSceneName":"Main"}""");
        _client.EnqueueResponse("GetStreamStatus", streaming ? """{"outputActive":true}""" : """{"outputActive":false}""");
        _client.EnqueueResponse("GetRecordStatus", """{"outputActive":false}""");
        _client.EnqueueResponse("GetInputList", """{"inputs":[]}""");
    }

    private sealed class FakeObsWebSocketClient : IObsWebSocketClient
    {
        private readonly Dictionary<string, Queue<Func<Task<JsonElement?>>>> _responses = new(StringComparer.Ordinal);
        private readonly Dictionary<(string Vendor, string RequestType), Queue<Func<Task<JsonElement?>>>> _vendorResponses = new();
        private readonly Dictionary<string, Queue<Func<Task<JsonElement?>>>> _outputStatuses = new(StringComparer.Ordinal);

        public event EventHandler<ObsWebSocketEventArgs>? EventReceived
        {
            add { }
            remove { }
        }

        public bool IsConnected { get; private set; }

        public DateTimeOffset? LastExchangeAt { get; private set; }

        public List<RequestRecord> Requests { get; } = [];

        public Task<ObsConnectionSnapshot> ConnectAsync(ObsConnectionOptions options, CancellationToken cancellationToken)
        {
            IsConnected = true;
            return Task.FromResult(new ObsConnectionSnapshot(true, "33.0.1", "5.7.6", null));
        }

        public Task DisconnectAsync(CancellationToken cancellationToken)
        {
            IsConnected = false;
            return Task.CompletedTask;
        }

        public Task<JsonElement?> SendRequestAsync(string requestType, object? requestData, CancellationToken cancellationToken)
        {
            var data = requestData is null ? (JsonElement?)null : JsonSerializer.SerializeToElement(requestData);
            Requests.Add(new(requestType, data));

            if (string.Equals(requestType, "CallVendorRequest", StringComparison.Ordinal)
                && data is { } vendorPayload)
            {
                var vendor = vendorPayload.GetProperty("vendorName").GetString() ?? string.Empty;
                var vendorReq = vendorPayload.GetProperty("requestType").GetString() ?? string.Empty;
                if (_vendorResponses.TryGetValue((vendor, vendorReq), out var vendorQueue) && vendorQueue.Count > 0)
                {
                    LastExchangeAt = DateTimeOffset.UtcNow;
                    return vendorQueue.Dequeue().Invoke();
                }
            }

            if (string.Equals(requestType, "GetOutputStatus", StringComparison.Ordinal)
                && data is { } statusPayload
                && statusPayload.TryGetProperty("outputName", out var nameProp)
                && nameProp.GetString() is { } outputName
                && _outputStatuses.TryGetValue(outputName, out var statusQueue)
                && statusQueue.Count > 0)
            {
                LastExchangeAt = DateTimeOffset.UtcNow;
                return statusQueue.Dequeue().Invoke();
            }

            if (_responses.TryGetValue(requestType, out var queue) && queue.Count > 0)
            {
                LastExchangeAt = DateTimeOffset.UtcNow;
                return queue.Dequeue().Invoke();
            }

            return Task.FromResult<JsonElement?>(null);
        }

        public void EnqueueResponse(string requestType, string json)
        {
            Enqueue(_responses, requestType, Parse(json));
        }

        public void EnqueueVendorResponse(string vendorName, string requestType, string json)
        {
            Enqueue(_vendorResponses, (vendorName, requestType), Parse(json));
        }

        public void EnqueueVendorError(string vendorName, string requestType, int code, string comment)
        {
            Enqueue(_vendorResponses, (vendorName, requestType), () => Task.FromException<JsonElement?>(new ObsRequestException("CallVendorRequest", code, comment)));
        }

        public void EnqueueOutputStatus(string outputName, string json)
        {
            Enqueue(_outputStatuses, outputName, Parse(json));
        }

        public ValueTask DisposeAsync()
        {
            return ValueTask.CompletedTask;
        }

        private static Func<Task<JsonElement?>> Parse(string json)
        {
            using var doc = JsonDocument.Parse(json);
            var element = doc.RootElement.Clone();
            return () => Task.FromResult<JsonElement?>(element);
        }

        private static void Enqueue<TKey>(Dictionary<TKey, Queue<Func<Task<JsonElement?>>>> map, TKey key, Func<Task<JsonElement?>> action)
            where TKey : notnull
        {
            if (!map.TryGetValue(key, out var queue))
            {
                queue = new();
                map[key] = queue;
            }

            queue.Enqueue(action);
        }
    }

    private sealed record RequestRecord(string RequestType, JsonElement? RequestData);
}
