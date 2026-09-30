using System.Text.RegularExpressions;

namespace PoproshaykaBot.Core.Server;

public static partial class QueryStringMasker
{
    public static string Mask(string queryString)
    {
        if (string.IsNullOrEmpty(queryString))
        {
            return queryString;
        }

        return SecretPattern().Replace(queryString, m => $"{m.Groups["key"].Value}=***");
    }

    [GeneratedRegex(@"(?i)(?<=^|[?&])(?<key>code|access_token|refresh_token|u)=[^&]+", RegexOptions.CultureInvariant | RegexOptions.ExplicitCapture, matchTimeoutMilliseconds: 1000)]
    private static partial Regex SecretPattern();
}
