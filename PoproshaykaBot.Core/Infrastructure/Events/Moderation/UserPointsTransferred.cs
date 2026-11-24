using PoproshaykaBot.Core.Platforms;
using PoproshaykaBot.Core.Statistics;

namespace PoproshaykaBot.Core.Infrastructure.Events.Moderation;

public sealed record UserPointsTransferred(
    PointsTransferSource Source,
    ChannelKey Target,
    PointsTransferMode Mode,
    PointsTransferResult Result) : EventBase;
