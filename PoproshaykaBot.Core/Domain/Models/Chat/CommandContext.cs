namespace PoproshaykaBot.Core.Domain.Models.Chat;

public sealed class CommandContext
{
    public string Channel { get; init; } = string.Empty;
    public string MessageId { get; init; } = string.Empty;
    public string UserId { get; init; } = string.Empty;
    public string Username { get; init; } = string.Empty;
    public string DisplayName { get; init; } = string.Empty;
    public IReadOnlyList<string> Arguments { get; init; } = [];
}