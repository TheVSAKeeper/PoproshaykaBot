using PoproshaykaBot.WinForms.Settings;

namespace PoproshaykaBot.WinForms.Chat.Commands;

public sealed class DonateCommand(SettingsManager settingsManager) : IChatCommand
{
    public string Canonical => "донат";
    public IReadOnlyCollection<string> Aliases => ["donate", "деньги"];
    public string Description => "информация о донате";

    public bool CanExecute(CommandContext context)
    {
        return true;
    }

    public OutgoingMessage Execute(CommandContext context)
    {
        var text = settingsManager.Current.Twitch.Messages.DonateCommandMessage;
        return OutgoingMessage.Normal(text);
    }
}
