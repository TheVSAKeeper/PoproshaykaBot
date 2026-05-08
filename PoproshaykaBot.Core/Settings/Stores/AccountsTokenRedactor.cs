using System.Text.Json.Nodes;

namespace PoproshaykaBot.Core.Settings.Stores;

internal static class AccountsTokenRedactor
{
    private static readonly HashSet<string> SensitiveKeys = new(StringComparer.OrdinalIgnoreCase)
    {
        "accessToken",
        "refreshToken",
    };

    public static string Redact(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return "{}";
        }

        JsonNode? root;
        try
        {
            root = JsonNode.Parse(raw);
        }
        catch
        {
            return "{}";
        }

        if (root == null)
        {
            return "{}";
        }

        RedactRecursive(root);
        return root.ToJsonString(JsonStoreOptions.Default);
    }

    private static void RedactRecursive(JsonNode node)
    {
        switch (node)
        {
            case JsonObject obj:
                foreach (var key in obj.Select(p => p.Key).ToList())
                {
                    if (SensitiveKeys.Contains(key))
                    {
                        obj[key] = string.Empty;
                        continue;
                    }

                    if (obj[key] is { } child)
                    {
                        RedactRecursive(child);
                    }
                }

                break;

            case JsonArray array:
                foreach (var item in array)
                {
                    if (item != null)
                    {
                        RedactRecursive(item);
                    }
                }

                break;
        }
    }
}
