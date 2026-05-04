namespace PoproshaykaBot.Core.Polls;

public sealed record PollChoiceSnapshot(
    string ChoiceId,
    string Title,
    int Votes,
    int ChannelPointsVotes,
    int BitsVotes);
