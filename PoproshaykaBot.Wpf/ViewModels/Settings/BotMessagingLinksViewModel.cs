using CommunityToolkit.Mvvm.Input;

namespace PoproshaykaBot.Wpf.ViewModels.Settings;

public sealed class BotMessagingLinksViewModel
{
    public BotMessagingLinksViewModel(params (string Title, Action Open)[] links)
    {
        Links = [.. links.Select(link => new BotMessagingLink(link.Title, new RelayCommand(link.Open)))];
    }

    public IReadOnlyList<BotMessagingLink> Links { get; }
}
