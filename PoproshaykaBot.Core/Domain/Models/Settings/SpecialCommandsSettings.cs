namespace PoproshaykaBot.Core.Domain.Models.Settings;

public class SpecialCommandsSettings
{
    public decimal X2IllsonCoins { get; set; } = 93.94m;

    public decimal X2IllsonPurchasePrice { get; set; } = 33.08m;

    public List<string> AllowedUsers { get; set; } = ["qp_illson"];

    public string SuccessMessage { get; set; } = "Держи бро! 👋";

    public string UnauthorizedMessage { get; set; } = "Ты новенький? 🤔 Подрасти сначала для таких вещей.";
}