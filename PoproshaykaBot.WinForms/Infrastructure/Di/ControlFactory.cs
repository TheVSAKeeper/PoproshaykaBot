namespace PoproshaykaBot.WinForms.Infrastructure.Di;

public sealed class ControlFactory(IServiceProvider services) : IControlFactory
{
    public T Create<T>() where T : Control, new()
    {
        var control = new T();
        services.HydrateDescendants(control);
        return control;
    }
}
