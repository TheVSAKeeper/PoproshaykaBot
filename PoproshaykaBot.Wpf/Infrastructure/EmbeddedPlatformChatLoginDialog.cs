using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using PoproshaykaBot.Wpf.ViewModels.Dialogs;
using PoproshaykaBot.Wpf.Views.Dialogs;

namespace PoproshaykaBot.Wpf.Infrastructure;

public sealed class EmbeddedPlatformChatLoginDialog(
    IServiceScopeFactory scopeFactory,
    ILogger<EmbeddedPlatformChatLoginDialog> logger) : IPlatformWebChatLoginDialog
{
    public Task<PlatformWebChatLoginResult> SignInAsync(PlatformWebChatLoginRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (App.IsHeadless)
        {
            logger.LogWarning("Встроенный вход веб-чата {Platform} подавлен в headless-режиме", request.Descriptor.PlatformDisplayName);
            return Task.FromResult(PlatformWebChatLoginResult.Unavailable(
                "Встроенный вход недоступен в автоматическом режиме."));
        }

        using var scope = scopeFactory.CreateScope();
        var viewModel = scope.ServiceProvider.GetRequiredService<EmbeddedPlatformChatLoginDialogViewModel>();
        viewModel.Configure(request);

        var window = new EmbeddedPlatformChatLoginWindow(viewModel)
        {
            Owner = DialogOwner.Resolve(),
        };

        window.ShowDialog();

        return Task.FromResult(viewModel.Result);
    }
}
