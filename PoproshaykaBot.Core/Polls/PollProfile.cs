namespace PoproshaykaBot.Core.Polls;

public sealed class PollProfile
{
    public const int MinChoices = 2;
    public const int MaxChoices = 5;
    public const int MinDurationSeconds = 15;
    public const int MaxDurationSeconds = 1800;
    public const int MinChannelPointsPerVote = 1;
    public const int MaxChannelPointsPerVote = 1_000_000;

    public Guid Id { get; set; } = Guid.NewGuid();

    public string Name { get; set; } = string.Empty;

    public string Title { get; set; } = string.Empty;

    public List<string> Choices { get; set; } = [];

    public int DurationSeconds { get; set; } = 60;

    public bool ChannelPointsVotingEnabled { get; set; }

    public int ChannelPointsPerVote { get; set; } = 100;

    public PollAutoTrigger AutoTrigger { get; set; } = new();
}
