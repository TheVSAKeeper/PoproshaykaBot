namespace PoproshaykaBot.Core.Infrastructure.Diagnostics;

public interface ISecretValueProvider
{
    string SourceName { get; }

    IReadOnlyList<string> ReadSecrets();
}
