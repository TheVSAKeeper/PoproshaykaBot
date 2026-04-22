using Microsoft.Extensions.Logging;
using PoproshaykaBot.WinForms.Infrastructure.Events;
using PoproshaykaBot.WinForms.Infrastructure.Events.Broadcasting;
using PoproshaykaBot.WinForms.Twitch.Helix;
using System.Net;

namespace PoproshaykaBot.WinForms.Broadcast.Profiles;

public sealed class ChannelInformationApplier(
    ITwitchChannelsApi channelsApi,
    IBroadcasterIdProvider idProvider,
    IEventBus eventBus,
    ILogger<ChannelInformationApplier> logger)
    : IChannelInformationApplier
{
    public async Task ApplyAsync(BroadcastProfile profile, CancellationToken cancellationToken)
    {
        var broadcasterId = await idProvider.GetAsync(cancellationToken);

        if (broadcasterId == null)
        {
            await eventBus.PublishAsync(new BroadcastProfileApplyFailed(profile, "Не удалось определить канал"),
                cancellationToken);

            return;
        }

        var request = new PatchChannelRequest
        {
            Title = string.IsNullOrEmpty(profile.Title) ? null : profile.Title,
            GameId = string.IsNullOrEmpty(profile.GameId) ? null : profile.GameId,
            BroadcasterLanguage = string.IsNullOrEmpty(profile.BroadcasterLanguage) ? null : profile.BroadcasterLanguage,
            Tags = profile.Tags.ToArray(),
        };

        try
        {
            await channelsApi.ModifyChannelInformationAsync(broadcasterId, request, cancellationToken);
            await eventBus.PublishAsync(new BroadcastProfileApplied(profile), cancellationToken);
        }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "Не удалось применить профиль {Profile}", profile.Name);
            await eventBus.PublishAsync(new BroadcastProfileApplyFailed(profile, SafeMessage(exception)),
                cancellationToken);
        }
    }

    public async Task ApplyPatchAsync(string? title, string? gameId, string? gameName, CancellationToken cancellationToken)
    {
        var broadcasterId = await idProvider.GetAsync(cancellationToken);

        var virtualProfile = new BroadcastProfile
        {
            Id = Guid.Empty,
            Name = "(ad-hoc)",
            Title = title ?? string.Empty,
            GameId = gameId ?? string.Empty,
            GameName = gameName ?? string.Empty,
        };

        if (broadcasterId == null)
        {
            await eventBus.PublishAsync(new BroadcastProfileApplyFailed(virtualProfile, "Не удалось определить канал"),
                cancellationToken);

            return;
        }

        var request = new PatchChannelRequest
        {
            Title = title,
            GameId = gameId,
            BroadcasterLanguage = null,
            Tags = null,
        };

        try
        {
            await channelsApi.ModifyChannelInformationAsync(broadcasterId, request, cancellationToken);
            await eventBus.PublishAsync(new BroadcastProfileApplied(virtualProfile), cancellationToken);
        }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "Не удалось применить патч канала");
            await eventBus.PublishAsync(new BroadcastProfileApplyFailed(virtualProfile, SafeMessage(exception)),
                cancellationToken);
        }
    }

    private static string SafeMessage(Exception exception)
    {
        if (exception is HelixRequestException helixEx)
        {
            return helixEx.StatusCode switch
            {
                HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden
                    => "Недостаточно прав Twitch для обновления канала. Проверь авторизацию.",
                HttpStatusCode.NotFound
                    => "Канал не найден.",
                HttpStatusCode.TooManyRequests
                    => "Слишком много запросов. Попробуй чуть позже.",
                _ => "Не удалось обновить канал. Попробуй ещё раз.",
            };
        }

        return exception switch
        {
            OperationCanceledException => "Операция отменена",
            TimeoutException => "Превышено время ожидания ответа Twitch",
            HttpRequestException => "Ошибка сети при обращении к Twitch",
            UnauthorizedAccessException => "Ошибка авторизации в Twitch",
            _ => "Twitch отклонил запрос на изменение канала",
        };
    }
}
