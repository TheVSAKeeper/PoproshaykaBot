using System.Net;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;

namespace PoproshaykaBot.WinForms.Services.Http.Handlers;

public sealed class ApiHistoryHandler(ChatHistoryManager historyManager) : IHttpHandler
{
    private static readonly JsonSerializerOptions JsonSerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        WriteIndented = false,
    };

    public Task HandleAsync(HttpListenerContext context)
    {
        var response = context.Response;

        try
        {
            var history = historyManager.GetHistory()
                .TakeLast(10)
                .Select(DtoMapper.ToServerMessage);

            var json = JsonSerializer.Serialize(history, JsonSerializerOptions);
            var buffer = Encoding.UTF8.GetBytes(json);

            response.ContentType = "application/json; charset=utf-8";
            response.ContentLength64 = buffer.Length;
            response.OutputStream.Write(buffer, 0, buffer.Length);
        }
        catch
        {
            response.StatusCode = 500;
        }
        finally
        {
            response.Close();
        }

        return Task.CompletedTask;
    }
}
