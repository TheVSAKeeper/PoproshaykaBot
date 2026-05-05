using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using PoproshaykaBot.Core.Server.Endpoints;

namespace PoproshaykaBot.Core.Tests.Server.Endpoints;

internal sealed class EndpointTestServer : IDisposable
{
    private readonly IHost _host;

    private EndpointTestServer(IHost host)
    {
        _host = host;
    }

    public TestServer Server => _host.GetTestServer();

    public static async Task<EndpointTestServer> CreateAsync(
        Action<IServiceCollection> configureServices,
        Func<IServiceProvider, IEndpointMapper> mapperFactory)
    {
        var builder = new HostBuilder()
            .ConfigureWebHost(webHost =>
            {
                webHost.UseTestServer()
                    .ConfigureServices(configureServices)
                    .Configure(app =>
                    {
                        app.UseRouting();
                        app.UseEndpoints(endpoints =>
                        {
                            var mapper = mapperFactory(app.ApplicationServices);
                            mapper.Map(endpoints);
                        });
                    });
            });

        var host = await builder.StartAsync();
        return new(host);
    }

    public HttpClient CreateClient()
    {
        return _host.GetTestClient();
    }

    public void Dispose()
    {
        _host.Dispose();
    }
}
