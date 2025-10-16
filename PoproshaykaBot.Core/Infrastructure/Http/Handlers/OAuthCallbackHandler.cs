using System.Net;
using System.Text;

namespace PoproshaykaBot.Core.Infrastructure.Http.Handlers;

/// <summary>
/// Обработчик OAuth callback запросов.
/// </summary>
public class OAuthCallbackHandler : IHttpRequestHandler
{
    private TaskCompletionSource<string>? _oauthCodeTask;

    public event Action<string>? LogMessage;

    public bool CanHandle(HttpListenerRequest request)
    {
        return request.Url?.AbsolutePath == "/";
    }

    public async Task HandleAsync(HttpListenerContext context)
    {
        var request = context.Request;
        var response = context.Response;

        try
        {
            var code = request.QueryString["code"];
            var error = request.QueryString["error"];

            if (!string.IsNullOrEmpty(code))
            {
                _oauthCodeTask?.SetResult(code);

                var successHtml = """
                                  <!DOCTYPE html>
                                  <html>
                                  <head>
                                      <title>Авторизация успешна</title>
                                      <meta charset="utf-8">
                                      <style>
                                          body { font-family: Arial, sans-serif; text-align: center; margin-top: 50px; background: #f0f0f0; }
                                          .container { background: white; padding: 40px; border-radius: 8px; max-width: 500px; margin: 0 auto; box-shadow: 0 2px 10px rgba(0,0,0,0.1); }
                                          .success { color: #28a745; font-size: 24px; margin-bottom: 20px; }
                                      </style>
                                  </head>
                                  <body>
                                      <div class='container'>
                                          <div class='success'>✓ Авторизация успешна!</div>
                                          <p>Вы можете закрыть это окно и вернуться к приложению.</p>
                                      </div>
                                  </body>
                                  </html>
                                  """;

                await WriteHtmlResponse(response, successHtml);
            }
            else if (!string.IsNullOrEmpty(error))
            {
                _oauthCodeTask?.SetException(new InvalidOperationException($"OAuth ошибка: {error}"));

                var errorHtml = $$"""
                                  <!DOCTYPE html>
                                  <html>
                                  <head>
                                      <title>Ошибка авторизации</title>
                                      <meta charset="utf-8">
                                      <style>
                                          body { font-family: Arial, sans-serif; text-align: center; margin-top: 50px; background: #f0f0f0; }
                                          .container { background: white; padding: 40px; border-radius: 8px; max-width: 500px; margin: 0 auto; box-shadow: 0 2px 10px rgba(0,0,0,0.1); }
                                          .error { color: #dc3545; font-size: 24px; margin-bottom: 20px; }
                                      </style>
                                  </head>
                                  <body>
                                      <div class='container'>
                                          <div class='error'>✗ Ошибка авторизации</div>
                                          <p>Ошибка: {{error}}</p>
                                          <p>Попробуйте еще раз.</p>
                                      </div>
                                  </body>
                                  </html>
                                  """;

                await WriteHtmlResponse(response, errorHtml);
            }
            else
            {
                response.StatusCode = 400;
            }
        }
        catch (Exception ex)
        {
            LogMessage?.Invoke($"Ошибка обработки OAuth callback: {ex.Message}");
            response.StatusCode = 500;
        }
        finally
        {
            response.Close();
        }
    }

    public Task<string> WaitForOAuthCodeAsync()
    {
        _oauthCodeTask = new();
        return _oauthCodeTask.Task;
    }

    private static async Task WriteHtmlResponse(HttpListenerResponse response, string html)
    {
        var buffer = Encoding.UTF8.GetBytes(html);
        response.ContentType = "text/html; charset=utf-8";
        response.ContentLength64 = buffer.Length;
        await response.OutputStream.WriteAsync(buffer, 0, buffer.Length);
    }
}
