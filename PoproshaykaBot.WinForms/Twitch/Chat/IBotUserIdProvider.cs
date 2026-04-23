using PoproshaykaBot.WinForms.Twitch.Helix;

namespace PoproshaykaBot.WinForms.Twitch.Chat;

public interface IBotUserIdProvider
{
    Task<string?> GetAsync(CancellationToken cancellationToken);

    Task<UserInfo?> GetUserAsync(CancellationToken cancellationToken);
}
