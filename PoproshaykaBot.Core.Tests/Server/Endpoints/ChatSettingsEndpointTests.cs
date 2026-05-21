using Microsoft.Extensions.DependencyInjection;
using PoproshaykaBot.Core.Infrastructure.Events;
using PoproshaykaBot.Core.Infrastructure.Events.Settings;
using PoproshaykaBot.Core.Server.Endpoints;
using PoproshaykaBot.Core.Settings.Obs;
using PoproshaykaBot.Core.Settings.Stores;
using System.Net;
using System.Text;
using System.Text.Json;

namespace PoproshaykaBot.Core.Tests.Server.Endpoints;

[TestFixture]
public sealed class ChatSettingsEndpointTests
{
    [SetUp]
    public void SetUp()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "poproshayka-chat-settings-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
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

    private (ObsChatStore Store, InMemoryEventBus Bus) CreateStoreWithBus(ObsChatSettings? initial = null)
    {
        var bus = new InMemoryEventBus(NullLogger<InMemoryEventBus>.Instance);
        var path = Path.Combine(_tempDir, "obs-chat.json");
        var store = new ObsChatStore(bus, NullLogger<ObsChatStore>.Instance, path);

        if (initial != null)
        {
            store.Save(initial);
        }

        return (store, bus);
    }

    private ObsChatStore CreateStore(ObsChatSettings? initial = null)
    {
        return CreateStoreWithBus(initial).Store;
    }

    private static StringContent JsonBody(string json)
    {
        return new(json, Encoding.UTF8, "application/json");
    }

    [Test]
    public async Task GetChatSettings_ReturnsDefaultsWhenNoSavedFile()
    {
        var store = CreateStore();

        using var server = await EndpointTestServer.CreateAsync(services =>
            {
                services.AddRouting();
                services.AddSingleton(store);
            },
            sp => new ChatSettingsEndpoint(sp.GetRequiredService<ObsChatStore>()));

        using var client = server.CreateClient();
        using var response = await client.GetAsync("/api/chat-settings");

        var json = await response.Content.ReadAsStringAsync();
        var doc = JsonDocument.Parse(json).RootElement;

        using (Assert.EnterMultipleScope())
        {
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
            Assert.That(response.Content.Headers.ContentType?.MediaType, Is.EqualTo("application/json"));
            Assert.That(doc.GetProperty("maxMessages").GetInt32(), Is.GreaterThan(0));
            Assert.That(doc.GetProperty("fontSize").GetString(), Does.EndWith("px"));
        }
    }

    [Test]
    public async Task GetChatSettings_ReflectsSavedSettings()
    {
        var store = CreateStore(new()
        {
            MaxMessages = 13,
            FontSize = 22,
            FontBold = true,
        });

        using var server = await EndpointTestServer.CreateAsync(services =>
            {
                services.AddRouting();
                services.AddSingleton(store);
            },
            sp => new ChatSettingsEndpoint(sp.GetRequiredService<ObsChatStore>()));

        using var client = server.CreateClient();
        using var response = await client.GetAsync("/api/chat-settings");

        var json = await response.Content.ReadAsStringAsync();
        var doc = JsonDocument.Parse(json).RootElement;

        using (Assert.EnterMultipleScope())
        {
            Assert.That(doc.GetProperty("maxMessages").GetInt32(), Is.EqualTo(13));
            Assert.That(doc.GetProperty("fontSize").GetString(), Is.EqualTo("22px"));
            Assert.That(doc.GetProperty("fontWeight").GetString(), Is.EqualTo("bold"));
        }
    }

    [Test]
    public async Task PostChatSettings_PersistsAndPublishesEvent()
    {
        var (store, bus) = CreateStoreWithBus();
        var eventTcs = new TaskCompletionSource<ObsChatSettings>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var subscription = bus.Subscribe<ChatSettingsChangedEvent>((@event, _) =>
        {
            eventTcs.TrySetResult(@event.Settings);
            return Task.CompletedTask;
        });

        using var server = await EndpointTestServer.CreateAsync(services =>
            {
                services.AddRouting();
                services.AddSingleton(store);
            },
            sp => new ChatSettingsEndpoint(sp.GetRequiredService<ObsChatStore>()));

        var payload = JsonSerializer.Serialize(new ObsChatSettings
        {
            MaxMessages = 77,
            FontSize = 22,
            FontBold = true,
            UsernameColor = Color.FromArgb(255, 200, 100, 50),
            UserMessageAnimation = MessageAnimationType.PopIn,
            FadeOutAnimationType = MessageAnimationType.Dissolve,
            MessageLifetimeSeconds = 15,
        }, JsonStoreOptions.Default);

        using var client = server.CreateClient();
        using var response = await client.PostAsync("/api/chat-settings", JsonBody(payload));

        var persisted = store.Load();
        var broadcastTask = await Task.WhenAny(eventTcs.Task, Task.Delay(TimeSpan.FromSeconds(2)));
        var published = broadcastTask == eventTcs.Task ? await eventTcs.Task : null;

        using (Assert.EnterMultipleScope())
        {
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
            Assert.That(persisted.MaxMessages, Is.EqualTo(77));
            Assert.That(persisted.FontSize, Is.EqualTo(22));
            Assert.That(persisted.FontBold, Is.True);
            Assert.That(persisted.UsernameColor.ToArgb(), Is.EqualTo(Color.FromArgb(255, 200, 100, 50).ToArgb()));
            Assert.That(persisted.UserMessageAnimation, Is.EqualTo(MessageAnimationType.PopIn));
            Assert.That(persisted.FadeOutAnimationType, Is.EqualTo(MessageAnimationType.Dissolve));
            Assert.That(persisted.MessageLifetimeSeconds, Is.EqualTo(15));
            Assert.That(published, Is.Not.Null, "Событие ChatSettingsChangedEvent должно быть опубликовано");
            Assert.That(published!.MaxMessages, Is.EqualTo(77));
        }
    }

    [Test]
    public async Task PostChatSettings_ClampsOutOfRangeValuesSilently()
    {
        var store = CreateStore();

        using var server = await EndpointTestServer.CreateAsync(services =>
            {
                services.AddRouting();
                services.AddSingleton(store);
            },
            sp => new ChatSettingsEndpoint(sp.GetRequiredService<ObsChatStore>()));

        var payload = JsonSerializer.Serialize(new ObsChatSettings
        {
            FontSize = 9999,
            MaxMessages = -5,
            ScrollPauseAfterUserMs = 1_000_000,
            UserMessageAnimation = "totally-not-a-real-animation",
            FadeOutAnimationType = "also-bogus",
        }, JsonStoreOptions.Default);

        using var client = server.CreateClient();
        using var response = await client.PostAsync("/api/chat-settings", JsonBody(payload));

        var persisted = store.Load();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
            Assert.That(persisted.FontSize, Is.EqualTo(ObsChatRanges.FontSizeMax));
            Assert.That(persisted.MaxMessages, Is.EqualTo(ObsChatRanges.MaxMessagesMin));
            Assert.That(persisted.ScrollPauseAfterUserMs, Is.EqualTo(ObsChatRanges.ScrollPauseAfterUserMsMax));
            Assert.That(persisted.UserMessageAnimation, Is.EqualTo(MessageAnimationType.SlideInRight));
            Assert.That(persisted.FadeOutAnimationType, Is.EqualTo(MessageAnimationType.FadeOut));
        }
    }

    [Test]
    public async Task PostChatSettings_ReturnsBadRequestForInvalidJson()
    {
        var store = CreateStore();

        using var server = await EndpointTestServer.CreateAsync(services =>
            {
                services.AddRouting();
                services.AddSingleton(store);
            },
            sp => new ChatSettingsEndpoint(sp.GetRequiredService<ObsChatStore>()));

        using var client = server.CreateClient();
        using var response = await client.PostAsync("/api/chat-settings", JsonBody("{ this is not json"));

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
    }

    [Test]
    public async Task PostChatSettings_ReturnsBadRequestForNullBody()
    {
        var store = CreateStore();

        using var server = await EndpointTestServer.CreateAsync(services =>
            {
                services.AddRouting();
                services.AddSingleton(store);
            },
            sp => new ChatSettingsEndpoint(sp.GetRequiredService<ObsChatStore>()));

        using var client = server.CreateClient();
        using var response = await client.PostAsync("/api/chat-settings", JsonBody("null"));

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
    }
}
