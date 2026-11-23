using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using PoproshaykaBot.Wpf.ViewModels.Dialogs;
using PoproshaykaBot.Wpf.Views.Dialogs;

namespace PoproshaykaBot.Wpf.Infrastructure;

public sealed class EmbeddedYouTubeChatLoginDialog(
    IServiceScopeFactory scopeFactory,
    ILogger<EmbeddedYouTubeChatLoginDialog> logger) : IEmbeddedYouTubeChatLoginDialog
{
    public Task<EmbeddedYouTubeChatLoginResult> SignInAsync(EmbeddedYouTubeChatLoginRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (App.IsHeadless)
        {
            logger.LogWarning("Встроенный вход YouTube подавлен в headless-режиме");
            return Task.FromResult(EmbeddedYouTubeChatLoginResult.Unavailable(
                "Встроенный вход недоступен в автоматическом режиме."));
        }

        using var scope = scopeFactory.CreateScope();
        var viewModel = scope.ServiceProvider.GetRequiredService<EmbeddedYouTubeChatLoginDialogViewModel>();
        viewModel.Configure(request);

        var window = new EmbeddedYouTubeChatLoginWindow(viewModel)
        {
            Owner = DialogOwner.Resolve(),
        };

        window.ShowDialog();

        return Task.FromResult(viewModel.Result);
    }
}
