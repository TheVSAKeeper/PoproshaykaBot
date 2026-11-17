using PoproshaykaBot.Core.Platforms;
using PoproshaykaBot.Core.Statistics;
using PoproshaykaBot.Core.Statistics.Linking;

namespace PoproshaykaBot.Core.Chat.Commands;

public sealed class LinkAccountCommand(
    IAccountLinkingService linkingService,
    IUserStatisticsRepository statistics) : IChatCommand
{
    public string Canonical => "привязать";
    public IReadOnlyCollection<string> Aliases => ["link", "связать"];
    public string Description => "связать аккаунты зрителя между площадками или каналами";

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
            var challenge = linkingService.InitiateViewerChallenge(caller, callerName);
            return OutgoingMessage.Reply(
                $"Чтобы привязать второй аккаунт, напиши с него в чат: !привязать {challenge.Code}",
                context.MessageId);
        }

        var firstArg = context.Arguments[0].Trim();

        if (firstArg.StartsWith("R-", StringComparison.OrdinalIgnoreCase))
        {
            var claim = linkingService.SubmitViewerClaim(caller, callerName, firstArg);
            return OutgoingMessage.Reply(FormatViewerResult(claim), context.MessageId);
        }

        if (firstArg.StartsWith("C-", StringComparison.OrdinalIgnoreCase))
        {
            var confirmed = await linkingService.ConfirmViewerLinkAsync(caller, callerName, firstArg, cancellationToken)
                .ConfigureAwait(false);
            return OutgoingMessage.Reply(FormatViewerResult(confirmed), context.MessageId);
        }

        if (context.IsBroadcaster && context.Arguments.Count >= 2)
        {
            var resolvedKeys = new List<PlatformUserKey>(context.Arguments.Count);
            foreach (var rawArg in context.Arguments)
            {
                if (!TryResolveUserKey(rawArg, context.Platform, out var key))
                {
                    return OutgoingMessage.Reply($"Аккаунт «{rawArg}» не найден в статистике.", context.MessageId);
                }

                resolvedKeys.Add(key);
            }

            var author = new AccountLinkAuthor(
                AccountLinkAuthorKind.Streamer,
                context.Platform,
                context.UserId,
                callerName);
            var manual = await linkingService.LinkManualAsync(resolvedKeys, author, cancellationToken)
                .ConfigureAwait(false);

            return OutgoingMessage.Reply(FormatManualResult(manual), context.MessageId);
        }

        return OutgoingMessage.Reply("Код привязки не найден или устарел. Запроси новый командой !привязать", context.MessageId);
    }

    internal static bool TryParsePlatformKey(string raw, out PlatformUserKey key)
    {
        var separator = raw.IndexOf(':');
        if (separator > 0 && separator < raw.Length - 1)
        {
            var platformPart = raw[..separator].Trim();
            var userIdPart = raw[(separator + 1)..].Trim();
            if (Enum.TryParse<StreamingPlatform>(platformPart, ignoreCase: true, out var platform)
                && Enum.IsDefined(platform)
                && !string.IsNullOrWhiteSpace(userIdPart))
            {
                key = new(platform, userIdPart);
                return true;
            }
        }

        key = default;
        return false;
    }

    private bool TryResolveUserKey(string rawArg, StreamingPlatform preferredPlatform, out PlatformUserKey key)
    {
        if (TryParsePlatformKey(rawArg, out key))
        {
            return true;
        }

        var viewer = statistics.FindViewerByName(StatisticsSlice.All(), rawArg, preferredPlatform);
        if (viewer is not null)
        {
            key = ResolveMatchedAccountKey(viewer, rawArg, preferredPlatform);
            return true;
        }

        key = default;
        return false;
    }

    internal static PlatformUserKey ResolveMatchedAccountKey(
        ViewerAggregateStatistics viewer,
        string rawTarget,
        StreamingPlatform preferredPlatform)
    {
        var normalized = rawTarget.Trim().TrimStart('@');
        return viewer.Accounts
                   .Where(a => string.Equals(a.Name.Trim().TrimStart('@'), normalized, StringComparison.OrdinalIgnoreCase))
                   .OrderByDescending(a => a.Key.Platform == preferredPlatform)
                   .ThenByDescending(static a => a.LastSeen)
                   .FirstOrDefault()?.Key
               ?? viewer.PrimaryKey;
    }

    private static string FormatViewerResult(AccountLinkActionResult result)
    {
        return result.Status switch
        {
            AccountLinkOperationStatus.ClaimIssued =>
                $"Заявка от {result.TargetKey?.Platform} ({result.TargetName}) создана. Подтверди её с первого аккаунта ({result.InitiatorKey?.Platform}: {result.InitiatorName}): !привязать {result.Code}",
            AccountLinkOperationStatus.Linked =>
                $"Аккаунты {result.InitiatorKey?.Platform} ({result.InitiatorName}) и {result.TargetKey?.Platform} ({result.TargetName}) связаны.",
            AccountLinkOperationStatus.AlreadyLinkedSameGroup =>
                "Эти аккаунты уже связаны между собой.",
            AccountLinkOperationStatus.TargetInAnotherGroup =>
                "Этот аккаунт уже состоит в другой группе связей. Сначала отвяжи его командой !отвязать.",
            AccountLinkOperationStatus.SameAccountRejected =>
                "Нужно отправить код с другого аккаунта, а не с того же самого.",
            AccountLinkOperationStatus.WrongConfirmingAccount =>
                $"Подтвердить эту заявку может только инициатор ({result.InitiatorKey?.Platform}: {result.InitiatorName}).",
            AccountLinkOperationStatus.ClaimLimitReached =>
                "По этой заявке достигнут лимит претендентов. Создай новую заявку командой !привязать.",
            _ => "Код привязки не найден или устарел. Запроси новый командой !привязать.",
        };
    }

    private static string FormatManualResult(AccountLinkActionResult result)
    {
        return result.Status switch
        {
            AccountLinkOperationStatus.Linked => $"Аккаунты связаны вручную ({result.Group?.Members.Count ?? 0} в группе).",
            AccountLinkOperationStatus.AlreadyLinkedSameGroup => "Эти аккаунты уже находятся в одной связке.",
            AccountLinkOperationStatus.SameAccountRejected => "Укажи минимум два разных аккаунта.",
            _ => "Не удалось связать указанные аккаунты.",
        };
    }
}
