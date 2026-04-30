using PoproshaykaBot.WinForms.Settings;

namespace PoproshaykaBot.WinForms.Chat.Commands;

public sealed class RanksCommand(SettingsManager settingsManager) : IChatCommand
{
    public string Canonical => "ранги";
    public IReadOnlyCollection<string> Aliases => ["ranks", "чесс"];
    public string Description => "список всех рангов";

    public bool CanExecute(CommandContext context)
    {
        return true;
    }

    public OutgoingMessage Execute(CommandContext context)
    {
        var ranks = settingsManager.Current.Ranks.Ranks
            .OrderByDescending(x => x.MinMessages)
            .ToList();

        if (ranks.Count == 0)
        {
            return OutgoingMessage.Reply("Ранги не настроены", context.MessageId);
        }

        var parts = ranks.Select(x => $"{x.Emoji} {x.DisplayName} ({FormattingUtils.FormatNumber(x.MinMessages)})");
        var text = "🏆 Шахматная лестница: " + string.Join(", ", parts);

        return OutgoingMessage.Normal(text);
    }
}
