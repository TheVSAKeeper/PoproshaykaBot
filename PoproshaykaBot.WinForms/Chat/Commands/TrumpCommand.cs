using PoproshaykaBot.WinForms.Settings;
using System.Text.Json;

namespace PoproshaykaBot.WinForms.Chat.Commands;

public sealed class TrumpCommand : IChatCommand
{
    private const string CryptoApiUrl = "https://api.coingecko.com/api/v3/coins/official-trump?localization=false&tickers=true&market_data=true&community_data=false&developer_data=false";

    private readonly SettingsManager _settingsManager;
    private readonly HttpClient _httpClient;

    public TrumpCommand(SettingsManager settingsManager)
    {
        _settingsManager = settingsManager;

        _httpClient = new();
        _httpClient.DefaultRequestHeaders.Add("User-Agent", "PoproshaykaBot/1.0");
    }

    public string Canonical => "x2illson";
    public IReadOnlyCollection<string> Aliases => ["trump"];
    public string Description => "курс TRUMP с расчетом X2Illson профита/луса (только для qp_illson)";

    public bool CanExecute(CommandContext context)
    {
        return true;
    }

    public OutgoingMessage? Execute(CommandContext context)
    {
        // TODO: Сделать асинхронные IChatCommand
        return ExecuteAsync(context).GetAwaiter().GetResult();
    }

    private static string FormatNumber(decimal number)
    {
        return number switch
        {
            >= 1_000_000_000 => $"{number / 1_000_000_000:F2}B",
            >= 1_000_000 => $"{number / 1_000_000:F2}M",
            >= 1_000 => $"{number / 1_000:F2}K",
            _ => number.ToString("F2"),
        };
    }

    private async Task<OutgoingMessage?> ExecuteAsync(CommandContext context)
    {
        var settings = _settingsManager.Current.SpecialCommands;
        var isAllowed = settings.AllowedUsers.Contains(context.Username, StringComparer.OrdinalIgnoreCase);

        if (!isAllowed)
        {
            return OutgoingMessage.Reply(settings.UnauthorizedMessage, context.MessageId);
        }

        try
        {
            var trumpData = await GetTrumpDataAsync();

            if (trumpData == null)
            {
                return OutgoingMessage.Reply("❌ Не удалось получить данные о TRUMP монете", context.MessageId);
            }

            var x2IllsonCoins = settings.X2IllsonCoins;
            var x2IllsonPurchasePrice = settings.X2IllsonPurchasePrice;

            var totalInvestment = x2IllsonCoins * x2IllsonPurchasePrice;
            var currentPortfolioValue = x2IllsonCoins * trumpData.CurrentPrice;
            var profitPerCoin = trumpData.CurrentPrice - x2IllsonPurchasePrice;
            var totalProfit = profitPerCoin * x2IllsonCoins;
            var profitPercent = profitPerCoin / x2IllsonPurchasePrice * 100;

            var profitEmoji = totalProfit >= 0 ? "💰" : "📉";
            var profitSign = totalProfit >= 0 ? "+" : "";
            var profitText = totalProfit >= 0 ? "ПРОФИТ" : "ЛУЗ";

            var currentValueEmoji = totalProfit >= 0 ? "🟢" : "🔴";
            var changeEmoji = trumpData.Change24HPercent >= 0 ? "📈" : "📉";
            var changeSign = trumpData.Change24HPercent >= 0 ? "+" : "";

            var text =
                $"""
                 💰 TRUMP: ${trumpData.CurrentPrice:F4}
                 {changeEmoji} Изменение 24ч: {changeSign}{trumpData.Change24HPercent:F2}%
                 📊 Объем 24ч: ${FormatNumber(trumpData.Volume24H)} | Рыночная кап.: ${FormatNumber(trumpData.MarketCap)}
                 {profitEmoji} X2Illson {profitText}: {profitSign}${Math.Abs(totalProfit):F2} ({profitSign}{profitPercent:F2}%) | Монет: {x2IllsonCoins:F2} | Цена покупки: ${x2IllsonPurchasePrice:F2} | Вложено: ${totalInvestment:F2} | {currentValueEmoji} Сейчас: ${currentPortfolioValue:F2}
                 """;

            return OutgoingMessage.Reply($"{settings.SuccessMessage} {text}", context.MessageId);
        }
        catch (Exception)
        {
            return OutgoingMessage.Reply("❌ Ошибка при получении данных о TRUMP монете", context.MessageId);
        }
    }

    private async Task<TrumpData?> GetTrumpDataAsync()
    {
        try
        {
            var response = await _httpClient.GetAsync(CryptoApiUrl);

            if (!response.IsSuccessStatusCode)
            {
                return null;
            }

            var json = await response.Content.ReadAsStringAsync();
            var data = JsonSerializer.Deserialize<JsonElement>(json);

            if (!data.TryGetProperty("market_data", out var marketData))
            {
                return null;
            }

            var currentPrice = marketData.GetProperty("current_price").GetProperty("usd").GetDecimal();
            var change24H = marketData.GetProperty("price_change_percentage_24h").GetDecimal();
            var volume24H = marketData.GetProperty("total_volume").GetProperty("usd").GetDecimal();
            var marketCap = marketData.GetProperty("market_cap").GetProperty("usd").GetDecimal();

            return new(currentPrice, change24H, volume24H, marketCap);
        }
        catch
        {
            return null;
        }
    }

    private sealed record TrumpData(decimal CurrentPrice, decimal Change24HPercent, decimal Volume24H, decimal MarketCap);
}
