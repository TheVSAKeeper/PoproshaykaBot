namespace PoproshaykaBot.Wpf.ViewModels.Settings;

public sealed record RankTierOption(int Value, string Label)
{
    public static IReadOnlyList<RankTierOption> Standard { get; } =
    [
        new(0, "–"),
        new(1, "I"),
        new(2, "II"),
        new(3, "III"),
    ];

    public static IReadOnlyList<RankTierOption> For(int tier)
    {
        return Standard.Any(option => option.Value == tier)
            ? Standard
            : [.. Standard, new(tier, tier.ToString(System.Globalization.CultureInfo.InvariantCulture))];
    }
}
