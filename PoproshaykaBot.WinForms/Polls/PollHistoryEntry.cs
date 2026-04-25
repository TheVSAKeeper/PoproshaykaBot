namespace PoproshaykaBot.WinForms.Polls;

public sealed class PollHistoryEntry
{
    public string PollId { get; set; } = string.Empty;

    public Guid? SourceProfileId { get; set; }

    public string Title { get; set; } = string.Empty;

    public List<PollHistoryChoice> FinalChoices { get; set; } = [];

    public DateTime StartedAtUtc { get; set; }

    public DateTime EndedAtUtc { get; set; }

    public PollSnapshotStatus FinalStatus { get; set; }

    public string? WinnerChoiceId { get; set; }

    public bool WinnerIsTie { get; set; }
}

public sealed class PollHistoryChoice
{
    public string ChoiceId { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public int Votes { get; set; }
    public int ChannelPointsVotes { get; set; }
    public int BitsVotes { get; set; }
}
