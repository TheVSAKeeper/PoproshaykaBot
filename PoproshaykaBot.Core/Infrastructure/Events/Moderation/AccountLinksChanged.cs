using PoproshaykaBot.Core.Platforms;
using PoproshaykaBot.Core.Statistics;

namespace PoproshaykaBot.Core.Infrastructure.Events.Moderation;

public sealed record AccountLinksChanged(
    AccountLinkOperationStatus Status,
    PlatformUserKey PrimaryUserKey,
    Guid? GroupId) : EventBase;
