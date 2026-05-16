using PoproshaykaBot.Core.Obs;
using PoproshaykaBot.Core.Settings;
using System.Text.Json;

namespace PoproshaykaBot.Core.Tests.Obs;

[TestFixture]
public sealed class ObsIntegrationServiceTests
{
    [SetUp]
    public void SetUp()
    {
        _settings = new()
        {
            Twitch =
            {
                HttpServerPort = 8099,
            },
        };

        _settingsManager = Substitute.For<SettingsManager>(NullLogger<SettingsManager>.Instance);
        _settingsManager.Current.Returns(_settings);
        _client = new();
        _service = new(_client, _settingsManager, NullLogger<ObsIntegrationService>.Instance);
    }

    private AppSettings _settings = null!;
    private SettingsManager _settingsManager = null!;
    private FakeObsWebSocketClient _client = null!;
    private ObsIntegrationService _service = null!;

    [Test]
    public async Task ProvisionBrowserSourceAsyncCreatesBrowserSourceWithOverlayUrlWhenSourceMissingAsync()
    {
        _client.EnqueueResponse("GetVersion", """{"obsVersion":"31.0.0","obsWebSocketVersion":"5.5.2"}""");
        _client.EnqueueResponse("GetInputKindList", """{"inputKinds":["browser_source"]}""");
        _client.EnqueueResponse("GetSceneList", """{"currentProgramSceneName":"Main","scenes":[{"sceneName":"Main"}]}""");
        _client.EnqueueResponse("GetInputList", """{"inputs":[]}""");

        var result = await _service.ProvisionBrowserSourceAsync(new()
        {
            SourceName = "PoproshaykaBot Chat",
            Width = 1280,
            Height = 720,
        }, CancellationToken.None);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.Created, Is.True);
            Assert.That(result.Url, Is.EqualTo("http://localhost:8099/chat"));
        }

        var createRequest = _client.Requests.Single(r =>
            string.Equals(r.RequestType, "CreateInput", StringComparison.Ordinal));

        var data = createRequest.RequestData!.Value;
        using (Assert.EnterMultipleScope())
        {
            Assert.That(data.GetProperty("sceneName").GetString(), Is.EqualTo("Main"));
            Assert.That(data.GetProperty("inputName").GetString(), Is.EqualTo("PoproshaykaBot Chat"));
            Assert.That(data.GetProperty("inputKind").GetString(), Is.EqualTo("browser_source"));
            Assert.That(data.GetProperty("inputSettings").GetProperty("url").GetString(), Is.EqualTo("http://localhost:8099/chat"));
            Assert.That(data.GetProperty("inputSettings").GetProperty("width").GetInt32(), Is.EqualTo(1280));
            Assert.That(data.GetProperty("inputSettings").GetProperty("height").GetInt32(), Is.EqualTo(720));
        }
    }

    [Test]
    public async Task ProvisionBrowserSourceAsyncUpdatesInputSettingsWhenBrowserSourceExistsAsync()
    {
        _client.EnqueueResponse("GetVersion", """{"obsVersion":"31.0.0","obsWebSocketVersion":"5.5.2"}""");
        _client.EnqueueResponse("GetInputKindList", """{"inputKinds":["browser_source"]}""");
        _client.EnqueueResponse("GetSceneList", """{"currentProgramSceneName":"Main","scenes":[{"sceneName":"Main"}]}""");
        _client.EnqueueResponse("GetInputList", """{"inputs":[{"inputName":"PoproshaykaBot Chat","inputKind":"browser_source"}]}""");

        var result = await _service.ProvisionBrowserSourceAsync(new()
        {
            SourceName = "PoproshaykaBot Chat",
            SceneName = "Main",
        }, CancellationToken.None);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.Created, Is.False);
            Assert.That(_client.Requests.Exists(r =>
                string.Equals(r.RequestType, "CreateInput", StringComparison.Ordinal)), Is.False);
        }

        var updateRequest = _client.Requests.Single(r =>
            string.Equals(r.RequestType, "SetInputSettings", StringComparison.Ordinal));

        var data = updateRequest.RequestData!.Value;
        using (Assert.EnterMultipleScope())
        {
            Assert.That(data.GetProperty("inputName").GetString(), Is.EqualTo("PoproshaykaBot Chat"));
            Assert.That(data.GetProperty("inputSettings").GetProperty("url").GetString(), Is.EqualTo("http://localhost:8099/chat"));
        }
    }

    private sealed class FakeObsWebSocketClient : IObsWebSocketClient
    {
        private readonly Dictionary<string, Queue<JsonElement?>> _responses = new(StringComparer.Ordinal);

        public bool IsConnected { get; private set; }

        public List<RequestRecord> Requests { get; } = [];

        public Task<ObsConnectionSnapshot> ConnectAsync(ObsConnectionOptions options, CancellationToken cancellationToken)
        {
            IsConnected = true;
            return Task.FromResult(new ObsConnectionSnapshot(true, "31.0.0", "5.5.2", null));
        }

        public Task DisconnectAsync(CancellationToken cancellationToken)
        {
            IsConnected = false;
            return Task.CompletedTask;
        }

        public Task<JsonElement?> SendRequestAsync(string requestType, object? requestData, CancellationToken cancellationToken)
        {
            Requests.Add(new(requestType, requestData is null ? null : JsonSerializer.SerializeToElement(requestData)));

            if (_responses.TryGetValue(requestType, out var queue) && queue.Count > 0)
            {
                return Task.FromResult(queue.Dequeue());
            }

            return Task.FromResult<JsonElement?>(null);
        }

        public void EnqueueResponse(string requestType, string json)
        {
            if (!_responses.TryGetValue(requestType, out var queue))
            {
                queue = new();
                _responses[requestType] = queue;
            }

            using var document = JsonDocument.Parse(json);
            queue.Enqueue(document.RootElement.Clone());
        }

        public ValueTask DisposeAsync()
        {
            return ValueTask.CompletedTask;
        }
    }

    private sealed record RequestRecord(string RequestType, JsonElement? RequestData);
}
