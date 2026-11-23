using PoproshaykaBot.Core.Platforms;
using PoproshaykaBot.Core.Settings;
using PoproshaykaBot.Core.Streaming;

namespace PoproshaykaBot.Wpf.Mcp;

public sealed record ChannelsStateDto(
    IReadOnlyList<ChannelsStateDto.PlatformRoutingDto> Platforms,
    IReadOnlyList<ChannelsStateDto.ChannelEntryDto> Channels,
    IReadOnlyList<ChannelsStateDto.ChannelGroupDto> Groups,
    IReadOnlyList<ChannelsStateDto.AutoDiscoverPromptDto>? ActiveAutoDiscoverPrompts = null)
{
    public sealed record PlatformRoutingDto(
        StreamingPlatform Platform,
        string? ActiveChannelId,
        ChannelSwitchMode SwitchMode,
        string? PreselectedNextChannelId);

    public sealed record RoleAssignmentDto(
        string? AccountId,
        string ContextId,
        string? VerifiedSubjectId,
        bool IsVerified);

    public sealed record ChannelMonitoringDto(
        ChannelMonitoringAvailability Availability,
        MonitoringUnavailableReason Reason,
        string? Detail,
        StreamStatus Status,
        string? Title,
        string? GameName,
        int? ViewerCount,
        DateTimeOffset? ObservedAtUtc);

    public sealed record ChannelEntryDto(
        StreamingPlatform Platform,
        string ChannelId,
        string DisplayName,
        string Handle,
        string ChannelUrl,
        bool IsConfigured,
        bool MonitoringEnabled,
        bool IsActive,
        RoleAssignmentDto? BotRole,
        RoleAssignmentDto? BroadcasterRole,
        ChannelMonitoringDto? Monitoring);

    public sealed record ChannelGroupMemberDto(StreamingPlatform Platform, string ChannelId);

    public sealed record ChannelGroupDto(
        string Id,
        string Name,
        IReadOnlyList<ChannelGroupMemberDto> Channels);

    public sealed record AutoDiscoverCandidateDto(
        StreamingPlatform Platform,
        string ChannelId,
        string ChannelDisplayName,
        string StreamKey,
        string? StreamTitle,
        DateTimeOffset StartedAtUtc,
        DateTimeOffset DetectedAtUtc);

    public sealed record AutoDiscoverPromptDto(
        StreamingPlatform Platform,
        string? CurrentActiveChannelId,
        string? CurrentActiveDisplayName,
        IReadOnlyList<AutoDiscoverCandidateDto> Candidates);
}
