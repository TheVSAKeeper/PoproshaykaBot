using System.Text.Encodings.Web;
using System.Text.Json;

namespace PoproshaykaBot.Core.Settings.Stores;

internal static class JsonStoreOptions
{
    public static JsonSerializerOptions Default { get; } = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };
}
