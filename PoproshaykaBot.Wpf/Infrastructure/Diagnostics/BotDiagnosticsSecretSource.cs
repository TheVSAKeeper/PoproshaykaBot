using KeepShell.Diagnostics;
using Microsoft.Extensions.Logging;
using PoproshaykaBot.Core.Infrastructure.Diagnostics;

namespace PoproshaykaBot.Wpf.Infrastructure.Diagnostics;

public sealed class BotDiagnosticsSecretSource(
    IEnumerable<ISecretValueProvider> secretProviders,
    ILogger<BotDiagnosticsSecretSource> logger) : IDiagnosticsSecretSource
{
    private static readonly string[] BotKeyNames =
        ["clientsecret", "client_secret", "accesstoken", "access_token", "refreshtoken", "refresh_token", "password"];

    public DiagnosticsSecretRules Collect()
    {
        var values = new List<string>();
        var complete = true;

        foreach (var provider in secretProviders)
        {
            complete &= Read(values, provider.ReadSecrets, provider.SourceName);
        }

        return new(BotKeyNames, [.. values.Where(static value => !string.IsNullOrWhiteSpace(value))])
        {
            Complete = complete,
        };
    }

    private bool Read(List<string> values, Func<IReadOnlyList<string>> read, string source)
    {
        try
        {
            values.AddRange(read());

            return true;
        }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "Секреты не собрались из источника {Source}, пакет диагностики будет помечен неполным", source);

            return false;
        }
    }
}
