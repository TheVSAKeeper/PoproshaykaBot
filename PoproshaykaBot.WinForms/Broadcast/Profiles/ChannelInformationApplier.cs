using Microsoft.Extensions.Logging;
using PoproshaykaBot.WinForms.Infrastructure.Events;
using PoproshaykaBot.WinForms.Infrastructure.Events.Broadcasting;
using PoproshaykaBot.WinForms.Twitch.Helix;

namespace PoproshaykaBot.WinForms.Broadcast.Profiles;

public sealed class ChannelInformationApplier(
    ITwitchChannelsApi channelsApi,
    IBroadcasterIdProvider idProvider,
    IChannelUpdateConfirmation confirmation,
    IEventBus eventBus,
    ILogger<ChannelInformationApplier> logger)
    : IChannelInformationApplier
{
    private static readonly TimeSpan ConfirmationTimeout = TimeSpan.FromSeconds(8);

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

        var confirmationTask = confirmation.AwaitAsync(request.Title, request.GameId, ConfirmationTimeout, cancellationToken);

        try
        {
            await channelsApi.ModifyChannelInformationAsync(broadcasterId, request, cancellationToken);
        }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "Не удалось применить профиль {Profile}", profile.Name);
            await eventBus.PublishAsync(new BroadcastProfileApplyFailed(profile, HelixErrorMessages.SafeMessage(exception)),
                cancellationToken);

            return;
        }

        var confirmed = await confirmationTask;

        if (!confirmed)
        {
            logger.LogInformation("Не получено подтверждение channel.update для профиля {Profile}, публикуем Applied по успешному Helix-ответу",
                profile.Name);
        }

        await eventBus.PublishAsync(new BroadcastProfileApplied(profile), cancellationToken);
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

        var confirmationTask = confirmation.AwaitAsync(title, gameId, ConfirmationTimeout, cancellationToken);

        try
        {
            await channelsApi.ModifyChannelInformationAsync(broadcasterId, request, cancellationToken);
        }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "Не удалось применить патч канала");
            await eventBus.PublishAsync(new BroadcastProfileApplyFailed(virtualProfile, HelixErrorMessages.SafeMessage(exception)),
                cancellationToken);

            return;
        }

        var confirmed = await confirmationTask;

        if (!confirmed)
        {
            logger.LogInformation("Не получено подтверждение channel.update для ad-hoc патча, публикуем Applied по успешному Helix-ответу");
        }

        await eventBus.PublishAsync(new BroadcastProfileApplied(virtualProfile), cancellationToken);
    }
}
