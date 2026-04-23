namespace PoproshaykaBot.WinForms.Infrastructure.Di;

public interface IFormFactory
{
    T Create<T>() where T : Form;
}
