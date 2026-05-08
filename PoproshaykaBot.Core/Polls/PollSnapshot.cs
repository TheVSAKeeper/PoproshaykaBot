namespace PoproshaykaBot.Core.Polls;

public sealed record PollSnapshot(
    string PollId,
    Guid? SourceProfileId,
    string Title,
    IReadOnlyList<PollChoiceSnapshot> Choices,
    DateTime StartedAtUtc,
    DateTime EndsAtUtc,
    bool ChannelPointsVotingEnabled,
    int ChannelPointsPerVote,
    PollSnapshotStatus Status,
    DateTime? EndedAtUtc)
{
    public int TotalVotes => Choices.Sum(c => c.Votes);

    public PollChoiceSnapshot? Leader
    {
        get
        {
            if (Choices.Count == 0)
            {
                return null;
            }

            var maxVotes = Choices.Max(c => c.Votes);
            return Choices.First(c => c.Votes == maxVotes);
        }
    }

    public bool LeaderIsTie
    {
        get
        {
            if (Choices.Count == 0)
            {
                return false;
            }

            var maxVotes = Choices.Max(c => c.Votes);
            return Choices.Count(c => c.Votes == maxVotes) > 1 && maxVotes > 0;
        }
    }
}
