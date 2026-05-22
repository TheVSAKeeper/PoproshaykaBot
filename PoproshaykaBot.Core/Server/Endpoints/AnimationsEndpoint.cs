using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using PoproshaykaBot.Core.Settings.Obs;

namespace PoproshaykaBot.Core.Server.Endpoints;

internal sealed class AnimationsEndpoint : IEndpointMapper
{
    public void Map(IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/api/animations", () =>
        {
            var payload = new AnimationsResponse
            {
                Entry = MessageAnimationType.EntryAnimations
                    .Select(a => new AnimationOption(a.Value, a.DisplayName))
                    .ToArray(),
                Exit = MessageAnimationType.ExitAnimations
                    .Select(a => new AnimationOption(a.Value, a.DisplayName))
                    .ToArray(),
            };

            return Results.Json(payload, ServerJsonOptions.Default);
        });
    }

    private sealed record AnimationOption(string Value, string Label);

    private sealed class AnimationsResponse
    {
        public IReadOnlyList<AnimationOption> Entry { get; init; } = [];
        public IReadOnlyList<AnimationOption> Exit { get; init; } = [];
    }
}
