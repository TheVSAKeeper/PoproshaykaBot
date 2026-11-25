using CommunityToolkit.Mvvm.Input;

namespace PoproshaykaBot.Wpf.ViewModels;

public sealed class BotMessagingNavigation
{
    public event Action<string>? RuleRequested;
    public event Action<string>? CommandRequested;
    public event Action<Guid?>? ProfileTargetsRequested;

    public void OpenRule(string key) => RuleRequested?.Invoke(key);
    public void OpenCommand(string canonical) => CommandRequested?.Invoke(canonical);
    public void OpenProfileTargets(Guid? profileId) => ProfileTargetsRequested?.Invoke(profileId);
}

public sealed record BotMessagingLink(string Title, IRelayCommand OpenCommand);
