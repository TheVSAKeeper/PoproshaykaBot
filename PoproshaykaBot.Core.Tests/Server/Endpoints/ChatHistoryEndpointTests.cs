using PoproshaykaBot.Core.Platforms;
using Microsoft.Extensions.DependencyInjection;
using PoproshaykaBot.Core.Chat;
using PoproshaykaBot.Core.Chat.Commands;
using PoproshaykaBot.Core.Infrastructure.Events;
using PoproshaykaBot.Core.Server.Endpoints;
using PoproshaykaBot.Core.Settings;
using PoproshaykaBot.Core.Settings.Stores;
using System.Net;
using System.Text.Json;

namespace PoproshaykaBot.Core.Tests.Server.Endpoints;

[TestFixture]
public sealed class ChatHistoryEndpointTests
{
    [SetUp]
    public void SetUp()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "poproshayka-history-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);

        _bus = new(NullLogger<InMemoryEventBus>.Instance);

        _settingsManager = Substitute.For<SettingsManager>(NullLogger<SettingsManager>.Instance, null, null);
        _settings = new();
        _settingsManager.Current.Returns(_settings);

        _historyManager = new(_settingsManager, _bus);
        _obsChatStore = new(_bus, NullLogger<ObsChatStore>.Instance, Path.Combine(_tempDir, "obs-chat.json"));
    }

    [TearDown]
    public void TearDown()
    {
        try
        {
            Directory.Delete(_tempDir, true);
        }
        catch
        {
        }
    }

    private string _tempDir = null!;
    private InMemoryEventBus _bus = null!;
    private SettingsManager _settingsManager = null!;
    private AppSettings _settings = null!;
    private ChatHistoryManager _historyManager = null!;
    private ObsChatStore _obsChatStore = null!;

    private Task<EndpointTestServer> CreateServerAsync(ChatSettingsStore? chatSettingsStore = null)
    {
        return EndpointTestServer.CreateAsync(services =>
            {
                services.AddRouting();
                services.AddSingleton(_historyManager);
                services.AddSingleton(_obsChatStore);
            },
            sp => new ChatHistoryEndpoint(
                sp.GetRequiredService<ChatHistoryManager>(),
                sp.GetRequiredService<ObsChatStore>(),
                chatSettingsStore));
    }

    private static ChatMessageData NewMessage(string id, DateTime timestamp, string text = "msg")
    {
        return new()
        {
            Platform = StreamingPlatform.Twitch,
            MessageId = id,
            Timestamp = timestamp,
            UserId = "u",
            DisplayName = "user",
            Message = text,
        };
    }

    [Test]
    public async Task GetHistory_EmptyHistory_ReturnsEmptyArray()
    {
        using var server = await CreateServerAsync();
        using var client = server.CreateClient();

        using var response = await client.GetAsync("/api/history");

        var json = await response.Content.ReadAsStringAsync();
        var doc = JsonDocument.Parse(json).RootElement;

        using (Assert.EnterMultipleScope())
        {
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
            Assert.That(response.Content.Headers.ContentType?.MediaType, Is.EqualTo("application/json"));
            Assert.That(doc.GetArrayLength(), Is.Zero);
        }
    }

    [Test]
    public async Task GetHistory_TruncatesToMaxMessages()
    {
        _obsChatStore.Save(new()
        {
            MaxMessages = 3,
            EnableMessageFadeOut = false,
        });

        for (var i = 0; i < 10; i++)
        {
            _historyManager.AddMessage(NewMessage($"m{i}", DateTime.UtcNow.AddSeconds(-(10 - i)), $"msg{i}"));
        }

        using var server = await CreateServerAsync();
        using var client = server.CreateClient();

        using var response = await client.GetAsync("/api/history");
        var json = await response.Content.ReadAsStringAsync();
        var doc = JsonDocument.Parse(json).RootElement;

        Assert.That(doc.GetArrayLength(), Is.EqualTo(3),
            "Endpoint обязан отдавать только последние MaxMessages, иначе OBS-overlay получит лишний контент при перезагрузке");
    }

    [Test]
    public async Task GetHistory_FadeOutEnabled_FiltersOldMessages()
    {
        _obsChatStore.Save(new()
        {
            MaxMessages = 100,
            EnableMessageFadeOut = true,
            MessageLifetimeSeconds = 60,
        });

        var now = DateTime.UtcNow;
        _historyManager.AddMessage(NewMessage("old", now.AddMinutes(-5), "old"));
        _historyManager.AddMessage(NewMessage("fresh", now.AddSeconds(-10), "fresh"));

        using var server = await CreateServerAsync();
        using var client = server.CreateClient();

        using var response = await client.GetAsync("/api/history");
        var json = await response.Content.ReadAsStringAsync();
        var doc = JsonDocument.Parse(json).RootElement;

        using (Assert.EnterMultipleScope())
        {
            Assert.That(doc.GetArrayLength(), Is.EqualTo(1),
                "Старые сообщения за пределами MessageLifetimeSeconds должны отфильтроваться при включённом fade-out");

            var only = doc[0];
            Assert.That(only.GetProperty("message").GetString(), Is.EqualTo("fresh"));
        }
    }

    [Test]
    public async Task GetHistory_FiltersImportedHistoryWhenObsHistoryDisabled_AndKeepsWhenEnabled()
    {
        var chatSettingsStore = new ChatSettingsStore(null, null, Path.Combine(_tempDir, "chat.json"));
        _obsChatStore.Save(new()
        {
            MaxMessages = 100,
            EnableMessageFadeOut = true,
            MessageLifetimeSeconds = 30,
        });

        var now = DateTime.UtcNow;
        var scope = Core.Chat.History.ChatHistoryScope.ForTwitch("1", "chan");

        _historyManager.ImportHistory(scope,
        [
            new()
            {
                Platform = StreamingPlatform.Twitch,
                Channel = "chan",
                ChannelId = "1",
                MessageId = "hist-old",
                Timestamp = now.AddMinutes(-10),
                UserId = "u1",
                DisplayName = "user",
                Message = "imported",
            },
            new()
            {
                Platform = StreamingPlatform.Twitch,
                Channel = "chan",
                ChannelId = "1",
                MessageId = "cmd-chat-only",
                Timestamp = now.AddMinutes(-9),
                UserId = "bot",
                DisplayName = "bot",
                Message = "chat-only",
                IsBot = true,
                CommandResponse = new("ранг", CommandSettings.CallerOnly),
            },
        ]);

        _historyManager.AddMessage(new()
        {
            Platform = StreamingPlatform.Twitch,
            Channel = "chan",
            ChannelId = "1",
            MessageId = "live-fresh",
            Timestamp = now.AddSeconds(-5),
            UserId = "u2",
            DisplayName = "user2",
            Message = "live",
        });

        using var server = await CreateServerAsync(chatSettingsStore);
        using var client = server.CreateClient();

        var disabledJson = await client.GetStringAsync("/api/history");
        var disabledDoc = JsonDocument.Parse(disabledJson).RootElement;

        chatSettingsStore.Save(new() { ShowHistoryInObs = true });

        var enabledJson = await client.GetStringAsync("/api/history");
        var enabledDoc = JsonDocument.Parse(enabledJson).RootElement;

        using (Assert.EnterMultipleScope())
        {
            Assert.That(disabledDoc.GetArrayLength(), Is.EqualTo(1));
            Assert.That(disabledDoc[0].GetProperty("messageId").GetString(), Is.EqualTo("live-fresh"));
            Assert.That(disabledDoc[0].GetProperty("isHistory").GetBoolean(), Is.False);

            Assert.That(enabledDoc.GetArrayLength(), Is.EqualTo(2));
            Assert.That(enabledDoc[0].GetProperty("messageId").GetString(), Is.EqualTo("hist-old"));
            Assert.That(enabledDoc[0].GetProperty("isHistory").GetBoolean(), Is.True);
            Assert.That(enabledDoc[1].GetProperty("messageId").GetString(), Is.EqualTo("live-fresh"));
        }
    }

    [Test]
    public async Task GetHistory_IsolatesActiveScopePerPlatform()
    {
        var chatSettingsStore = new ChatSettingsStore(null, null, Path.Combine(_tempDir, "chat-scope.json"));
        chatSettingsStore.Save(new() { ShowHistoryInObs = true });

        var now = DateTime.UtcNow;
        var oldLive = Core.Chat.History.ChatHistoryScope.ForYouTube("UC-1", "live-1", "Chan");
        var currentLive = Core.Chat.History.ChatHistoryScope.ForYouTube("UC-1", "live-2", "Chan");

        _historyManager.ImportHistory(oldLive,
        [
            new()
            {
                Platform = StreamingPlatform.YouTube,
                Channel = "Chan",
                ChannelId = "UC-1",
                ChatSessionId = "live-1",
                MessageId = "old-stream-msg",
                Timestamp = now.AddMinutes(-10),
                UserId = "u1",
                DisplayName = "user1",
                Message = "from live 1",
            },
        ]);

        _historyManager.ImportHistory(currentLive,
        [
            new()
            {
                Platform = StreamingPlatform.YouTube,
                Channel = "Chan",
                ChannelId = "UC-1",
                ChatSessionId = "live-2",
                MessageId = "current-stream-msg",
                Timestamp = now.AddMinutes(-2),
                UserId = "u2",
                DisplayName = "user2",
                Message = "from live 2",
            },
        ]);

        using var server = await CreateServerAsync(chatSettingsStore);
        using var client = server.CreateClient();

        var json = await client.GetStringAsync("/api/history");
        var doc = JsonDocument.Parse(json).RootElement;

        using (Assert.EnterMultipleScope())
        {
            Assert.That(doc.GetArrayLength(), Is.EqualTo(1));
            Assert.That(doc[0].GetProperty("messageId").GetString(), Is.EqualTo("current-stream-msg"));
            Assert.That(doc[0].GetProperty("channelId").GetString(), Is.EqualTo("UC-1"));
            Assert.That(doc[0].GetProperty("chatSessionId").GetString(), Is.EqualTo("live-2"));
        }
    }
}
