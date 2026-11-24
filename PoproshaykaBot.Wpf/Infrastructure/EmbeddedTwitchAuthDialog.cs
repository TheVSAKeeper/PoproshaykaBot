using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using PoproshaykaBot.Wpf.Bootstrap;
using PoproshaykaBot.Wpf.ViewModels.Dialogs;
using PoproshaykaBot.Wpf.Views.Dialogs;
using System.Windows;

namespace PoproshaykaBot.Wpf.Infrastructure;

public sealed class EmbeddedTwitchAuthDialog(
    IServiceScopeFactory scopeFactory,
    ILogger<EmbeddedTwitchAuthDialog> logger) : IEmbeddedTwitchAuthDialog
{
    public Task<EmbeddedTwitchAuthResult> AuthorizeAsync(
        EmbeddedTwitchAuthRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();

        if (App.IsHeadless)
        {
            logger.EmbeddedAuthSuppressedHeadless(request.Role);

            return Task.FromResult(EmbeddedTwitchAuthResult.Unavailable(
                "Встроенный вход недоступен в автоматическом режиме."));
        }

        using var scope = scopeFactory.CreateScope();
        var viewModel = scope.ServiceProvider.GetRequiredService<EmbeddedTwitchAuthDialogViewModel>();
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
