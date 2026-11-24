using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using PoproshaykaBot.Wpf.Bootstrap;
using PoproshaykaBot.Wpf.ViewModels.Dialogs;
using PoproshaykaBot.Wpf.Views.Dialogs;

namespace PoproshaykaBot.Wpf.Infrastructure;

public sealed class EmbeddedYouTubeAuthDialog(
    IServiceScopeFactory scopeFactory,
    ILogger<EmbeddedYouTubeAuthDialog> logger) : IEmbeddedYouTubeAuthDialog
{
    public Task<EmbeddedYouTubeAuthResult> AuthorizeAsync(
        EmbeddedYouTubeAuthRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();

        if (App.IsHeadless)
        {
            logger.EmbeddedAuthSuppressedHeadless(request.Role);

            return Task.FromResult(EmbeddedYouTubeAuthResult.Unavailable(
                "Встроенный вход недоступен в автоматическом режиме."));
        }

        using var scope = scopeFactory.CreateScope();
        var viewModel = scope.ServiceProvider.GetRequiredService<EmbeddedYouTubeAuthDialogViewModel>();
        viewModel.Configure(request);

        var window = new EmbeddedTwitchAuthWindow(viewModel)
        {
            Owner = DialogOwner.Resolve(),
        };

        using var registration = cancellationToken.Register(() =>
        {
            _ = window.Dispatcher.BeginInvoke(() =>
            {
                if (window.IsLoaded)
                {
                    window.Close();
                }
            });
        });

        window.ShowDialog();
        cancellationToken.ThrowIfCancellationRequested();

        return Task.FromResult(viewModel.Result);
    }
}
