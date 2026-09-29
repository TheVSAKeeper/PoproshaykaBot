using Microsoft.Extensions.DependencyInjection;
using PoproshaykaBot.Core.Server.Endpoints;
using PoproshaykaBot.Core.Tests.Polls;
using PoproshaykaBot.Core.Twitch.Helix;
using PoproshaykaBot.Core.Users.Avatars;
using System.Net;
using System.Text.Json;

namespace PoproshaykaBot.Core.Tests.Server.Endpoints;

[TestFixture]
public sealed class AvatarEndpointTests
{
    private const string ProfileImage = "https://static-cdn.jtvnw.net/jtv_user_pictures/abc-profile_image-300x300.png";

    private string _directory = null!;

    [SetUp]
    public void SetUp()
    {
        _directory = Path.Combine(Path.GetTempPath(), "poproshayka-avatar-endpoint-" + Guid.NewGuid().ToString("N"));
    }

    [TearDown]
    public void TearDown()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }

    private static Task<EndpointTestServer> CreateServerAsync(IUserAvatarCache cache)
    {
        return EndpointTestServer.CreateAsync(services =>
            {
                services.AddRouting();
                services.AddSingleton(cache);
            },
            sp => new AvatarEndpoint(sp.GetRequiredService<IUserAvatarCache>(), sp));
    }

    [Test]
    public async Task GetUserAvatar_NoIdQuery_Returns400()
    {
        using var server = await CreateServerAsync(Substitute.For<IUserAvatarCache>());
        using var client = server.CreateClient();

        using var response = await client.GetAsync("/api/user-avatar");

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest),
            "Без ?id endpoint должен явно отбивать 400, а не лезть к Helix за пустым userId");
    }

    [Test]
    public async Task GetUserAvatar_BlankIdQuery_Returns400()
    {
        using var server = await CreateServerAsync(Substitute.For<IUserAvatarCache>());
        using var client = server.CreateClient();

        using var response = await client.GetAsync("/api/user-avatar?id=%20%20");

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
    }

    [Test]
    public async Task Адрес_из_Helix_отдаётся_прежней_формой_и_второй_раз_берётся_из_кеша()
    {
        var helix = Substitute.For<ITwitchHelixClient>();
        helix.GetUsersByIdsAsync(Arg.Any<IEnumerable<string>>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<UserInfo>>(
                [new UserInfo("42", "viewer", "Viewer", "", "", null, ProfileImage, null, DateTime.UnixEpoch)]));

        using var server = await CreateServerAsync(CreateCache(helix));
        using var client = server.CreateClient();

        using var first = await client.GetAsync("/api/user-avatar?id=42");
        using var second = await client.GetAsync("/api/user-avatar?id=42");
        using var body = JsonDocument.Parse(await first.Content.ReadAsStringAsync());

        using (Assert.EnterMultipleScope())
        {
            Assert.That(first.StatusCode, Is.EqualTo(HttpStatusCode.OK));
            Assert.That(body.RootElement.GetProperty("url").GetString(), Is.EqualTo(ProfileImage),
                "оверлей получает тот же адрес Helix, без подмены размера под кеш страницы");
            Assert.That(first.Headers.CacheControl?.ToString(), Is.EqualTo("public, max-age=86400"));
            Assert.That(second.StatusCode, Is.EqualTo(HttpStatusCode.OK));

            await helix.Received(1).GetUsersByIdsAsync(Arg.Any<IEnumerable<string>>(), Arg.Any<CancellationToken>());
        }
    }

    [TestCase("123456", TestName = "Неизвестный_Twitch_пользователь_даёт_404")]
    [TestCase("..%2F..%2Fsettings", TestName = "Идентификатор_не_числом_даёт_404_и_в_Helix_не_уходит")]
    public async Task Нет_адреса_значит_404(string id)
    {
        var helix = Substitute.For<ITwitchHelixClient>();
        helix.GetUsersByIdsAsync(Arg.Any<IEnumerable<string>>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<UserInfo>>([]));

        using var server = await CreateServerAsync(CreateCache(helix));
        using var client = server.CreateClient();

        using var response = await client.GetAsync("/api/user-avatar?id=" + id);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.NotFound));

            await helix.Received(id.All(char.IsAsciiDigit) ? 1 : 0)
                .GetUsersByIdsAsync(Arg.Any<IEnumerable<string>>(), Arg.Any<CancellationToken>());
        }
    }

    private UserAvatarCache CreateCache(ITwitchHelixClient helix)
    {
        return new(
            helix,
            Substitute.For<IHttpClientFactory>(),
            new TestTimeProvider { UtcNow = new(2026, 9, 29, 12, 0, 0, TimeSpan.Zero) },
            new() { Directory = _directory },
            NullLogger<UserAvatarCache>.Instance);
    }
}
