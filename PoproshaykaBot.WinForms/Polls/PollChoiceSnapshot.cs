namespace PoproshaykaBot.WinForms.Polls;

public sealed record PollChoiceSnapshot(
    string ChoiceId,
    string Title,
    int Votes,
    int ChannelPointsVotes,
    int BitsVotes);
