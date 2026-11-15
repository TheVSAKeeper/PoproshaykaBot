namespace PoproshaykaBot.Core.Infrastructure.Diagnostics;

public sealed class DelegateSecretValueProvider(string sourceName, Func<IReadOnlyList<string>> readSecrets)
    : ISecretValueProvider
{
    public string SourceName { get; } = sourceName;

    public IReadOnlyList<string> ReadSecrets()
    {
        return readSecrets();
    }
}
