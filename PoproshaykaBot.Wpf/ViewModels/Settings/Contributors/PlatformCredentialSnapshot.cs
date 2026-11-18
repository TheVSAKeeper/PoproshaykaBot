using PoproshaykaBot.Core.Platforms;
using PoproshaykaBot.Core.Platforms.Auth;

namespace PoproshaykaBot.Wpf.ViewModels.Settings.Contributors;

internal readonly record struct PlatformCredentialSnapshot(
    string ClientId,
    string ClientSecret)
{
    public IReadOnlyList<SettingsSectionEvent> CollectAuthorizationRefreshes(
        StreamingPlatform platform,
        PlatformCredentialSnapshot current,
        Func<PlatformAuthRole, IReadOnlyList<AuthContextSummary>> listContexts)
    {
        if (this == current)
        {
            return [];
        }

        return PlatformAuthRoles.Active
            .SelectMany(listContexts)
            .Where(context => context.HasToken)
            .Select(context => SettingsSectionEvent.Create(new PlatformAuthorizationRefreshed(platform, context.Role, context.ContextId)))
            .ToArray();
    }
}
