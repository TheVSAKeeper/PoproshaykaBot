using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Logging;
using PoproshaykaBot.Core.Server.Obs;
using PoproshaykaBot.Core.Settings.Obs;
using PoproshaykaBot.Core.Settings.Stores;
using System.Text.Json;

namespace PoproshaykaBot.Core.Server.Endpoints;

internal sealed class ChatSettingsEndpoint(ObsChatStore obsChatStore, ILogger<ChatSettingsEndpoint>? logger = null) : IEndpointMapper
{
    public void Map(IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/api/chat-settings", () =>
        {
            var settings = obsChatStore.Load();
            var cssSettings = ObsChatCssSettings.FromObsChatSettings(settings);
            return Results.Json(cssSettings, ServerJsonOptions.Default);
        });

        endpoints.MapGet("/api/chat-settings/raw", () =>
        {
            var settings = obsChatStore.Load();
            return Results.Json(settings, ServerJsonOptions.WithColors);
        });

        endpoints.MapGet("/api/chat-settings/schema", () =>
        {
            return Results.Json(ObsChatRangesSchema.All, ServerJsonOptions.Default);
        });

        endpoints.MapPost("/api/chat-settings", async (HttpRequest request, CancellationToken cancellationToken) =>
        {
            ObsChatSettings? incoming;
            try
            {
                incoming = await JsonSerializer.DeserializeAsync<ObsChatSettings>(request.Body, JsonStoreOptions.Default, cancellationToken);
            }
            catch (JsonException exception)
            {
                logger?.LogWarning(exception, "POST /api/chat-settings: ошибка разбора JSON");
                return Results.BadRequest(new { error = "Не удалось разобрать JSON.", details = exception.Message });
            }

            if (incoming == null)
            {
                return Results.BadRequest(new { error = "Тело запроса пустое." });
            }

            var normalized = ObsChatSettingsValidator.Clamp(incoming);
            obsChatStore.Save(normalized);

            logger?.LogInformation("POST /api/chat-settings: настройки чата применены через HTTP API");
            return Results.Ok(new { ok = true });
        });
    }
}
