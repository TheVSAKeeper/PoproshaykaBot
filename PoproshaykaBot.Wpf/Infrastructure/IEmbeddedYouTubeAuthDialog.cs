namespace PoproshaykaBot.Wpf.Infrastructure;

public interface IEmbeddedYouTubeAuthDialog
{
    Task<EmbeddedYouTubeAuthResult> AuthorizeAsync(
        EmbeddedYouTubeAuthRequest request,
        CancellationToken cancellationToken = default);
}
