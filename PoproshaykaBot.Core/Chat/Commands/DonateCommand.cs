using PoproshaykaBot.Core.Settings;

namespace PoproshaykaBot.Core.Chat.Commands;

public sealed class DonateCommand(SettingsManager settingsManager) : IChatCommand
{
    public string Canonical => "донат";
    public IReadOnlyCollection<string> Aliases => ["donate", "деньги"];
    public string Description => "информация о донате";

    public bool CanExecute(CommandContext context)
    {
        return true;
    }

    public Task<OutgoingMessage?> ExecuteAsync(CommandContext context, CancellationToken cancellationToken)
    {
        var text = settingsManager.Current.Twitch.Messages.DonateCommandMessage;
        return Task.FromResult<OutgoingMessage?>(OutgoingMessage.Normal(text));
    }
}
