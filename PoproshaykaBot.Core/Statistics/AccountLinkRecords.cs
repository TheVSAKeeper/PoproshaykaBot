using PoproshaykaBot.Core.Platforms;
using System.Text.Json.Serialization;

namespace PoproshaykaBot.Core.Statistics;

public enum AccountLinkOrigin
{
    None = 0,
    ViewerConfirmed = 1,
    StreamerManual = 2,
}

public enum AccountLinkAuthorKind
{
    None = 0,
    Viewer = 1,
    Streamer = 2,
}

public sealed record AccountLinkAuthor(
    AccountLinkAuthorKind Kind,
    StreamingPlatform? ActorPlatform = null,
    string? ActorUserId = null,
    string? ActorDisplayName = null);

public sealed class AccountLinkMemberRecord
{
    public StreamingPlatform Platform { get; set; }
    public string UserId { get; set; } = string.Empty;
    public AccountLinkOrigin Origin { get; set; }
    public AccountLinkAuthor Author { get; set; } = new(AccountLinkAuthorKind.None);
    public DateTimeOffset LinkedAtUtc { get; set; }

    [JsonIgnore]
    public PlatformUserKey Key => new(Platform, UserId);

    public AccountLinkMemberRecord Clone()
    {
        return new()
        {
            Platform = Platform,
            UserId = UserId,
            Origin = Origin,
            Author = Author,
            LinkedAtUtc = LinkedAtUtc,
        };
    }
}

public sealed class AccountLinkGroupRecord
{
    public Guid GroupId { get; set; } = Guid.NewGuid();
    public DateTimeOffset CreatedAtUtc { get; set; }
    public List<AccountLinkMemberRecord> Members { get; set; } = [];

    public AccountLinkGroupRecord Clone()
    {
        return new()
        {
            GroupId = GroupId,
            CreatedAtUtc = CreatedAtUtc,
            Members = Members.Select(static member => member.Clone()).ToList(),
        };
    }
}

public enum AccountLinkOperationStatus
{
    None = 0,
    ChallengeIssued = 1,
    ClaimIssued = 2,
    Linked = 3,
    Unlinked = 4,
    AlreadyLinkedSameGroup = 5,
    TargetInAnotherGroup = 6,
    InvalidOrExpiredCode = 7,
    SameAccountRejected = 8,
    WrongConfirmingAccount = 9,
    ClaimLimitReached = 10,
    NotLinked = 11,
}
