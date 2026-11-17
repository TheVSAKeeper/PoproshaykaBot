using PoproshaykaBot.Core.Platforms;
using PoproshaykaBot.Core.Statistics;
using PoproshaykaBot.Core.Statistics.Linking;

namespace PoproshaykaBot.Core.Chat.Commands;

public sealed class UnlinkAccountCommand(
    IAccountLinkingService linkingService,
    IUserStatisticsRepository statistics) : IChatCommand
{
    public string Canonical => "отвязать";
    public IReadOnlyCollection<string> Aliases => ["unlink"];
    public string Description => "отвязать свой аккаунт от связанной группы зрителя";

    public bool CanExecute(CommandContext context)
    {
        return context.RecordsUserData && !string.IsNullOrWhiteSpace(context.UserId);
    }

    public async Task<OutgoingMessage?> ExecuteAsync(CommandContext context, CancellationToken cancellationToken)
    {
        if (!context.RecordsUserData || string.IsNullOrWhiteSpace(context.UserId))
        {
            return null;
        }

        var caller = new PlatformUserKey(context.Platform, context.UserId);
        var callerName = string.IsNullOrWhiteSpace(context.DisplayName) ? context.Username : context.DisplayName;

        if (context.Arguments.Count == 0)
        {
            var selfResult = await linkingService.UnlinkSelfAsync(caller, callerName, cancellationToken).ConfigureAwait(false);
            return OutgoingMessage.Reply(
                selfResult.Status == AccountLinkOperationStatus.Unlinked
                    ? "Твой аккаунт отвязан. История и счётчики каждого канала сохранены отдельно."
                    : "Этот аккаунт не связан с другими аккаунтами.",
                context.MessageId);
        }

        if (!context.IsBroadcaster)
        {
            return OutgoingMessage.Reply("Отвязать можно только свой аккаунт командой !отвязать", context.MessageId);
        }

        var rawTarget = context.Arguments[0].Trim();
        PlatformUserKey targetKey;
        if (!LinkAccountCommand.TryParsePlatformKey(rawTarget, out targetKey))
        {
            var viewer = statistics.FindViewerByName(StatisticsSlice.All(), rawTarget, context.Platform);
            if (viewer is null)
            {
                return OutgoingMessage.Reply($"Аккаунт «{rawTarget}» не найден в статистике.", context.MessageId);
            }

            targetKey = LinkAccountCommand.ResolveMatchedAccountKey(viewer, rawTarget, context.Platform);
        }

        var author = new AccountLinkAuthor(
            AccountLinkAuthorKind.Streamer,
            context.Platform,
            context.UserId,
            callerName);
        var manualResult = await linkingService.UnlinkManualAsync(targetKey, author, cancellationToken).ConfigureAwait(false);

        return OutgoingMessage.Reply(
            manualResult.Status == AccountLinkOperationStatus.Unlinked
                ? $"Аккаунт {targetKey} отвязан от группы."
                : $"Аккаунт {targetKey} не связан с другими аккаунтами.",
            context.MessageId);
    }
}
