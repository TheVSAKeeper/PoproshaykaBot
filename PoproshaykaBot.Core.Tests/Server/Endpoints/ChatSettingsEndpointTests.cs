using Microsoft.Extensions.DependencyInjection;
using PoproshaykaBot.Core.Infrastructure.Events;
using PoproshaykaBot.Core.Server.Endpoints;
using PoproshaykaBot.Core.Settings.Obs;
using PoproshaykaBot.Core.Settings.Stores;
using System.Net;
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

    private ObsChatStore CreateStore(ObsChatSettings? initial = null)
    {
        var bus = new InMemoryEventBus(NullLogger<InMemoryEventBus>.Instance);
        var path = Path.Combine(_tempDir, "obs-chat.json");
        var store = new ObsChatStore(bus, NullLogger<ObsChatStore>.Instance, path);

        if (initial != null)
        {
            store.Save(initial);
        }

        return store;
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
}
