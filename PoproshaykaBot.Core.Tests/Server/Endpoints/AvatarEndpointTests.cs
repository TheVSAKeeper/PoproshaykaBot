using Microsoft.Extensions.DependencyInjection;
using PoproshaykaBot.Core.Server.Endpoints;
using PoproshaykaBot.Core.Users;
using System.Net;

namespace PoproshaykaBot.Core.Tests.Server.Endpoints;

[TestFixture]
public sealed class AvatarEndpointTests
{
    private static Task<EndpointTestServer> CreateServerAsync(IServiceProvider rootProvider)
    {
        return EndpointTestServer.CreateAsync(services =>
            {
                services.AddRouting();
                services.AddSingleton(new UserProfileImageProvider(rootProvider, NullLogger<UserProfileImageProvider>.Instance));
            },
            sp => new AvatarEndpoint(sp.GetRequiredService<UserProfileImageProvider>()));
    }

    [Test]
    public async Task GetUserAvatar_NoIdQuery_Returns400()
    {
        using var server = await CreateServerAsync(new ServiceCollection().BuildServiceProvider());
        using var client = server.CreateClient();

        using var response = await client.GetAsync("/api/user-avatar");

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest),
            "Без ?id endpoint должен явно отбивать 400, а не лезть к Helix за пустым userId");
    }

    [Test]
    public async Task GetUserAvatar_BlankIdQuery_Returns400()
    {
        using var server = await CreateServerAsync(new ServiceCollection().BuildServiceProvider());
        using var client = server.CreateClient();

        using var response = await client.GetAsync("/api/user-avatar?id=%20%20");

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
    }
}
