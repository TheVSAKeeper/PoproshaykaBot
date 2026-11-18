using PoproshaykaBot.Core.Debugging;
using PoproshaykaBot.Core.Platforms;
using PoproshaykaBot.Core.Platforms.Auth;
using PoproshaykaBot.Core.Settings.Stores;
using PoproshaykaBot.Core.Twitch.Auth;

namespace PoproshaykaBot.Core.Twitch;

public sealed class TwitchChannelAuthorization(
    AccountsStore accounts,
    ChannelCatalogStore catalog,
    PlatformChannelRegistry channels,
    ITargetChannelProvider target)
{
    public TwitchAccountSettings? GetAccount(PlatformAuthRole role)
    {
        return TryGetAccount(role, out _, out var account) ? account : null;
    }

    public bool TryGetAccount(PlatformAuthRole role, out string contextId, out TwitchAccountSettings account)
    {
        var binding = channels.GetBinding(StreamingPlatform.Twitch);
        var entry = binding is not null
            ? catalog.FindEntry(binding.Key)
            : target.Current.IsDebugSession ? null : catalog.GetActiveChannel(StreamingPlatform.Twitch);
        var assignment = entry?.GetAccountAssignment(role);
        var isBot = role == PlatformAuthRole.Bot;
        contextId = AuthContextIds.Normalize(binding is not null
            ? isBot ? binding.BotContextId : binding.BroadcasterContextId
            : isBot ? entry?.BotContextId : entry?.BroadcasterContextId);
        account = accounts.Load(role, contextId);

        if (entry is null || (binding is not null && assignment is not null && assignment.ContextId != contextId))
        {
            return true;
        }

        return assignment is { IsVerified: true }
            && string.Equals(assignment.Account.AccountId, account.UserId, StringComparison.Ordinal);
    }
}
