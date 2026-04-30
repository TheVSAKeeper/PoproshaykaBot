namespace PoproshaykaBot.WinForms.Infrastructure.Di;

public interface IControlFactory
{
    T Create<T>() where T : Control, new();
}
