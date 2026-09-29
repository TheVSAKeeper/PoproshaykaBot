namespace PoproshaykaBot.Core.Users;

public static class UserRankRules
{
    public static string? DescribeEmojiError(string? emoji)
    {
        return string.IsNullOrWhiteSpace(emoji) ? "Укажите значок" : null;
    }

    public static string? DescribeNameError(string? name)
    {
        return string.IsNullOrWhiteSpace(name) ? "Укажите название" : null;
    }
}
