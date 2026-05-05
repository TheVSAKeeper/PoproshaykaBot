using Microsoft.AspNetCore.Routing;

namespace PoproshaykaBot.Core.Server.Endpoints;

public interface IEndpointMapper
{
    void Map(IEndpointRouteBuilder endpoints);
}
