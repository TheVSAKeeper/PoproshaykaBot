namespace PoproshaykaBot.Core.Users;

public sealed class RanksSettings
{
    public PointTerm PointTerm { get; set; } = new();

    public List<UserRank> Ranks { get; set; } =
    [
        new("♚", "КОРОЛЬ", 5000),

        new("♛", "ФЕРЗЬ", 2000, 1),
        new("♛", "ФЕРЗЬ", 1000, 2),
        new("♛", "ФЕРЗЬ", 750, 3),

        new("♜", "ЛАДЬЯ", 550, 1),
        new("♜", "ЛАДЬЯ", 400, 2),
        new("♜", "ЛАДЬЯ", 300, 3),

        new("♝", "СЛОН", 230, 1),
        new("♝", "СЛОН", 170, 2),
        new("♝", "СЛОН", 120, 3),

        new("♞", "КОНЬ", 80, 1),
        new("♞", "КОНЬ", 50, 2),
        new("♞", "КОНЬ", 25, 3),

        new("♟", "ПЕШКА", 10, 1),
        new("♟", "ПЕШКА", 3, 2),
        new("♟", "ПЕШКА", 0, 3),
    ];
}
