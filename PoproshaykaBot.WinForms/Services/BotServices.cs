using PoproshaykaBot.WinForms.Broadcast;
using PoproshaykaBot.WinForms.Chat;
using PoproshaykaBot.WinForms.Settings;
using TwitchLib.Api;

namespace PoproshaykaBot.WinForms.Services;

public sealed record BotServices(
    TwitchSettings Settings,
    StatisticsCollector StatisticsCollector,
    TwitchAPI TwitchApi,
    ChatDecorationsProvider ChatDecorationsProvider,
    AudienceTracker AudienceTracker,
    ChatHistoryManager ChatHistoryManager,
    BroadcastScheduler BroadcastScheduler,
    ChatCommandProcessor CommandProcessor,
    StreamStatusManager StreamStatusManager,
    UserMessagesManagementService UserMessagesManagementService);
