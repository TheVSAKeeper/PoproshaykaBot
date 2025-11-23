namespace PoproshaykaBot.WinForms;

public sealed class UserRankService
{
    // TODO: В конфиги вынести
    private static readonly UserRank[] Ranks =
    [
        new("♔", "КОРОЛЬ", 5000),
        new("♛", "ФЕРЗЬ", 2500),
        new("♜", "ЛАДЬЯ", 1000),
        new("♝", "СЛОН", 500),
        new("♞", "КОНЬ", 250),
        new("♟", "ПЕШКА", 0),
    ];

    public UserRank GetRank(ulong messageCount)
    {
        foreach (var rank in Ranks.Where(rank => messageCount >= rank.MinMessages))
        {
            return rank;
        }

        return Ranks[^1];
    }
}