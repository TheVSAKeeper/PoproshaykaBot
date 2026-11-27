using System.Text.Json.Nodes;

namespace PoproshaykaBot.Core.Settings.Stores;

internal static class AccountsTokenRedactor
{
    internal static readonly string[] AuthTokenKeys =
    [
        "accessToken",
        "refreshToken",
    ];

    private static readonly HashSet<string> SensitiveKeys = new(
        [.. AuthTokenKeys, "clientSecret"],
        StringComparer.OrdinalIgnoreCase);

    public static bool HasAnyToken(JsonNode? node, IReadOnlyCollection<string> keys)
    {
        switch (node)
        {
            case JsonObject obj:
                foreach (var pair in obj)
                {
                    if (keys.Contains(pair.Key, StringComparer.OrdinalIgnoreCase))
                    {
                        if (pair.Value is JsonValue value && value.TryGetValue(out string? text) && !string.IsNullOrWhiteSpace(text))
                        {
                            return true;
                        }

                        continue;
                    }

                    if (HasAnyToken(pair.Value, keys))
                    {
                        return true;
                    }
                }

                return false;

            case JsonArray array:
                return array.Any(item => HasAnyToken(item, keys));

            default:
                return false;
        }
    }

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
                foreach (var item in array.Where(i => i != null))
                {
                    RedactRecursive(item!);
                }

                break;
        }
    }
}
