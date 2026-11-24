namespace PoproshaykaBot.Wpf.Infrastructure;

public interface IEmbeddedTwitchAuthDialog
{
    Task<EmbeddedTwitchAuthResult> AuthorizeAsync(EmbeddedTwitchAuthRequest request, CancellationToken cancellationToken = default);
}
