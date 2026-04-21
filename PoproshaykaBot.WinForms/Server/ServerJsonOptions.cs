using System.Text.Encodings.Web;
using System.Text.Json;

namespace PoproshaykaBot.WinForms.Server;

internal static class ServerJsonOptions
{
    public static readonly JsonSerializerOptions Default = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        WriteIndented = false,
    };
}
