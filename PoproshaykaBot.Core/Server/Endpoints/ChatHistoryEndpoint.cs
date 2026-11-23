using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using PoproshaykaBot.Core.Broadcast;
using PoproshaykaBot.Core.Chat;
using PoproshaykaBot.Core.Chat.History;
using PoproshaykaBot.Core.Platforms;
using PoproshaykaBot.Core.Settings.Chat;
using PoproshaykaBot.Core.Settings.Stores;
using PoproshaykaBot.Core.YouTube.Streaming;

namespace PoproshaykaBot.Core.Server.Endpoints;

internal sealed class ChatHistoryEndpoint(
    ChatHistoryManager chatHistoryManager,
    ObsChatStore obsChatStore,
    IChatHistorySettingsProvider? chatSettingsProvider = null,
    TimeProvider? timeProvider = null,
    OverlayUserRankResolver? rankResolver = null,
    IChatPlatformRegistry? platformRegistry = null,
    IYouTubeActiveBroadcastSource? youtubeBroadcastSource = null,
    IBroadcastState? broadcastState = null)
    : IEndpointMapper
{
    public void Map(IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/api/history", (HttpContext httpContext) =>
        {
            var isPreview = string.Equals(httpContext.Request.Query["preview"], "true", StringComparison.OrdinalIgnoreCase);
            var obsSettings = obsChatStore.Load();
            var chatSettings = chatSettingsProvider?.GetCurrent() ?? new ChatSettings();
            var maxMessages = obsSettings.MaxMessages;
            var utcNow = (timeProvider ?? TimeProvider.System).GetUtcNow().UtcDateTime;

            var history = chatHistoryManager
                .GetActiveHistory()
                .Where(entry => ObsOverlayMessageFilter.Allows(entry, chatSettings)
                    && ObsOverlayMessageFilter.AllowsCurrentStream(entry, platformRegistry, youtubeBroadcastSource, broadcastState));

            if (!isPreview && obsSettings.EnableMessageFadeOut)
            {
                var cutoff = utcNow.AddSeconds(-obsSettings.MessageLifetimeSeconds);
                history = history.Where(entry => entry.IsHistorical || entry.Timestamp >= cutoff);
            }

            var finalHistory = history
                .TakeLast(maxMessages)
                .Select(message => DtoMapper.ToServerMessage(message, obsSettings, rankResolver?.Resolve(message)));

            return Results.Json(finalHistory, ServerJsonOptions.Default);
        });
    }
}
