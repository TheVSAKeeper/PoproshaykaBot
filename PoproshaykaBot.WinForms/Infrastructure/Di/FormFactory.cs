using Microsoft.Extensions.DependencyInjection;

namespace PoproshaykaBot.WinForms.Infrastructure.Di;

public sealed class FormFactory(IServiceScopeFactory scopeFactory) : IFormFactory
{
    public T Create<T>() where T : Form
    {
        var scope = scopeFactory.CreateScope();
        var form = scope.ServiceProvider.GetRequiredService<T>();

        form.FormClosed += (_, _) => scope.Dispose();

        return form;
    }
}
