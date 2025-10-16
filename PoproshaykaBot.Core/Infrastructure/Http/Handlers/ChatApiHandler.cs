using PoproshaykaBot.Core.Application.Chat;
using PoproshaykaBot.Core.Domain.Models.Chat;
using PoproshaykaBot.Core.Domain.Models.Settings;
using PoproshaykaBot.Core.Infrastructure.Persistence;
using System.Net;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;

namespace PoproshaykaBot.Core.Infrastructure.Http.Handlers;

/// <summary>
/// Обработчик API endpoints для чата (история, настройки).
/// </summary>
public class ChatApiHandler : IHttpRequestHandler
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        WriteIndented = false,
    };

    private readonly ChatHistoryManager _chatHistoryManager;
    private readonly SettingsManager _settingsManager;

    public ChatApiHandler(ChatHistoryManager chatHistoryManager, SettingsManager settingsManager)
    {
        _chatHistoryManager = chatHistoryManager ?? throw new ArgumentNullException(nameof(chatHistoryManager));
        _settingsManager = settingsManager ?? throw new ArgumentNullException(nameof(settingsManager));
    }

    public event Action<string>? LogMessage;

    public bool CanHandle(HttpListenerRequest request)
    {
        var path = request.Url?.AbsolutePath;
        return path == "/api/history" || path == "/api/chat-settings";
    }

    public async Task HandleAsync(HttpListenerContext context)
    {
        var request = context.Request;
        var response = context.Response;

        try
        {
            switch (request.Url?.AbsolutePath)
            {
                case "/api/history":
                    await ServeHistory(response);
                    break;

                case "/api/chat-settings":
                    await ServeChatSettings(response);
                    break;

                default:
                    response.StatusCode = 404;
                    response.Close();
                    break;
            }
        }
        catch (Exception ex)
        {
            LogMessage?.Invoke($"Ошибка API запроса {request.Url?.AbsolutePath}: {ex.Message}");
            response.StatusCode = 500;
            await WriteErrorResponse(response, "Внутренняя ошибка сервера");
            response.Close();
        }
    }

    private static object ToServerMessage(ChatMessageData chatMessage)
    {
        return new
        {
            username = chatMessage.DisplayName,
            displayName = chatMessage.DisplayName,
            message = chatMessage.Message,
            timestamp = chatMessage.Timestamp.ToString("yyyy-MM-ddTHH:mm:ss.fffZ"),
            messageType = chatMessage.MessageType.ToString(),
            isFirstTime = chatMessage.IsFirstTime,
            status = chatMessage.Status,
            emotes = chatMessage.Emotes.Select(e => new
                {
                    id = e.Id,
                    name = e.Name,
                    imageUrl = e.ImageUrl,
                    startIndex = e.StartIndex,
                    endIndex = e.EndIndex,
                })
                .ToArray(),
            badges = chatMessage.Badges.Select(b => new
                {
                    type = b.Key,
                    version = b.Value,
                    imageUrl = chatMessage.BadgeUrls.GetValueOrDefault($"{b.Key}/{b.Value}", ""),
                })
                .ToArray(),
        };
    }

    private static async Task WriteJsonResponse(HttpListenerResponse response, string json)
    {
        var buffer = Encoding.UTF8.GetBytes(json);
        response.ContentType = "application/json; charset=utf-8";
        response.ContentLength64 = buffer.Length;
        await response.OutputStream.WriteAsync(buffer);
    }

    private static async Task WriteErrorResponse(HttpListenerResponse response, string errorMessage)
    {
        try
        {
            var errorObj = new { error = errorMessage };
            var json = JsonSerializer.Serialize(errorObj, JsonOptions);
            await WriteJsonResponse(response, json);
        }
        catch
        {
            // Игнорируем ошибки записи ошибки
        }
    }

    private async Task ServeHistory(HttpListenerResponse response)
    {
        try
        {
            var history = _chatHistoryManager.GetHistory()
                .TakeLast(10)
                .Select(ToServerMessage);

            var json = JsonSerializer.Serialize(history, JsonOptions);
            await WriteJsonResponse(response, json);
        }
        catch (Exception ex)
        {
            LogMessage?.Invoke($"Ошибка отдачи истории: {ex.Message}");
            response.StatusCode = 500;
        }
        finally
        {
            response.Close();
        }
    }

    private async Task ServeChatSettings(HttpListenerResponse response)
    {
        try
        {
            var settings = _settingsManager.Current.Twitch.ObsChat;
            var cssSettings = ObsChatCssSettings.FromObsChatSettings(settings);

            var json = JsonSerializer.Serialize(cssSettings, JsonOptions);

            response.ContentType = "application/json; charset=utf-8";
            response.Headers.Add("Access-Control-Allow-Origin", "*");
            response.Headers.Add("Cache-Control", "no-cache");

            await WriteJsonResponse(response, json);

            LogMessage?.Invoke("Настройки чата успешно отданы клиенту");
        }
        catch (JsonException ex)
        {
            LogMessage?.Invoke($"Ошибка сериализации настроек чата: {ex.Message}");
            response.StatusCode = 500;
            await WriteErrorResponse(response, "Ошибка обработки настроек");
        }
        catch (Exception ex)
        {
            LogMessage?.Invoke($"Ошибка отдачи настроек чата: {ex.Message}");
            response.StatusCode = 500;
            await WriteErrorResponse(response, "Внутренняя ошибка сервера");
        }
        finally
        {
            try
            {
                response.Close();
            }
            catch (Exception ex)
            {
                LogMessage?.Invoke($"Ошибка закрытия ответа: {ex.Message}");
            }
        }
    }
}
