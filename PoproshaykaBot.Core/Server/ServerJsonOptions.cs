using PoproshaykaBot.Core.Settings.Stores;
using System.Text.Encodings.Web;
using System.Text.Json;

namespace PoproshaykaBot.Core.Server;

internal static class ServerJsonOptions
{
    public static readonly JsonSerializerOptions Default = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        WriteIndented = false,
    };

    public static readonly JsonSerializerOptions WithColors = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        WriteIndented = false,
        Converters = { new ColorJsonConverter() },
    };
}
