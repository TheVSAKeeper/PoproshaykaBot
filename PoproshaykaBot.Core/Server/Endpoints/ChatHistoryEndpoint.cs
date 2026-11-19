using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using PoproshaykaBot.Core.Chat;
using PoproshaykaBot.Core.Chat.History;
using PoproshaykaBot.Core.Settings.Chat;
using PoproshaykaBot.Core.Settings.Stores;

namespace PoproshaykaBot.Core.Server.Endpoints;

internal sealed class ChatHistoryEndpoint(
    ChatHistoryManager chatHistoryManager,
    ObsChatStore obsChatStore,
    IChatHistorySettingsProvider? chatSettingsProvider = null,
    TimeProvider? timeProvider = null,
    OverlayUserRankResolver? rankResolver = null)
    : IEndpointMapper
{
    public void Map(IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/api/history", () =>
        {
            var obsSettings = obsChatStore.Load();
            var chatSettings = chatSettingsProvider?.GetCurrent() ?? new ChatSettings();
            var maxMessages = obsSettings.MaxMessages;
            var utcNow = (timeProvider ?? TimeProvider.System).GetUtcNow().UtcDateTime;

            var history = chatHistoryManager
                .GetActiveHistory()
                .Where(entry => ObsOverlayMessageFilter.Allows(entry, chatSettings));

            if (obsSettings.EnableMessageFadeOut)
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
