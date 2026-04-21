using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using PoproshaykaBot.WinForms.Auth;
using PoproshaykaBot.WinForms.Chat;
using PoproshaykaBot.WinForms.Settings;
using System.Text.Encodings.Web;

namespace PoproshaykaBot.WinForms.Server;

public sealed class KestrelHttpServer(
    ChatHistoryManager chatHistoryManager,
    SseService sseService,
    SettingsManager settingsManager,
    TwitchOAuthService twitchOAuthService,
    ILoggerFactory loggerFactory)
    : IAsyncDisposable
{
    private const string OAuthSuccessHtml =
        """
        <!DOCTYPE html>
        <html>
        <head>
            <title>Авторизация успешна</title>
            <style>
                body { font-family: Arial, sans-serif; text-align: center; margin-top: 50px; }
                .success { color: green; font-size: 24px; }
            </style>
        </head>
        <body>
            <div class='success'>✓ Авторизация успешна!</div>
            <p>Вы можете закрыть это окно и вернуться к приложению.</p>
        </body>
        </html>
        """;

    private const string OAuthErrorHtmlTemplate =
        """
        <!DOCTYPE html>
        <html>
        <head>
            <title>Ошибка авторизации</title>
            <style>
                body {{ font-family: Arial, sans-serif; text-align: center; margin-top: 50px; }}
                .error {{ color: red; font-size: 24px; }}
            </style>
        </head>
        <body>
            <div class='error'>✗ Ошибка авторизации</div>
            <p>Ошибка: {0}</p>
            <p>Попробуйте еще раз.</p>
        </body>
        </html>
        """;

    private static readonly string OverlayHtml = ResourceLoader.LoadResourceText("PoproshaykaBot.WinForms.Assets.ObsOverlay.html");
    private static readonly byte[] ObsCssBytes = ResourceLoader.LoadResourceBytes("PoproshaykaBot.WinForms.Assets.obs.css");
    private static readonly byte[] ObsJsBytes = ResourceLoader.LoadResourceBytes("PoproshaykaBot.WinForms.Assets.obs.js");
    private static readonly byte[] FaviconBytes = ResourceLoader.LoadResourceBytes("PoproshaykaBot.WinForms.icon.ico");

    private WebApplication? _app;

    public event Action<string>? LogMessage;

    public bool IsRunning { get; private set; }

    public async Task StartAsync()
    {
        if (IsRunning)
        {
            return;
        }

        try
        {
            var port = settingsManager.Current.Twitch.HttpServerPort;

            var builder = WebApplication.CreateSlimBuilder(new WebApplicationOptions
            {
                Args = [],
            });

            builder.Services.AddSingleton(loggerFactory);

            builder.Services.AddSingleton(chatHistoryManager);
            builder.Services.AddSingleton(sseService);
            builder.Services.AddSingleton(settingsManager);
            builder.Services.AddSingleton(twitchOAuthService);

            builder.Services.AddCors(options =>
            {
                options.AddDefaultPolicy(policy =>
                    policy.AllowAnyOrigin()
                        .AllowAnyHeader()
                        .AllowAnyMethod());
            });

            builder.WebHost.ConfigureKestrel(options =>
            {
                options.ListenLocalhost(port);
            });

            _app = builder.Build();

            _app.UseCors();

            _app.Use(async (ctx, next) =>
            {
                LogMessage?.Invoke($"HTTP запрос: {ctx.Request.Method} {ctx.Request.Path}{ctx.Request.QueryString}");
                await next(ctx);
            });

            MapEndpoints(_app);

            await _app.StartAsync();

            sseService.Start();

            IsRunning = true;

            LogMessage?.Invoke($"HTTP сервер запущен на порту {port}");
        }
        catch (Exception ex)
        {
            LogMessage?.Invoke($"Ошибка запуска HTTP сервера: {ex.Message}");
            throw;
        }
    }

    public async Task StopAsync()
    {
        if (!IsRunning)
        {
            return;
        }

        try
        {
            IsRunning = false;
            await sseService.StopAsync();

            if (_app != null)
            {
                await _app.StopAsync();
                await _app.DisposeAsync();
                _app = null;
            }

            LogMessage?.Invoke("HTTP сервер остановлен");
        }
        catch (Exception ex)
        {
            LogMessage?.Invoke($"Ошибка остановки HTTP сервера: {ex.Message}");
        }
    }

    public async ValueTask DisposeAsync()
    {
        await StopAsync();
    }

    private void MapEndpoints(WebApplication app)
    {
        app.MapGet("/", (HttpContext ctx) =>
        {
            var code = ctx.Request.Query["code"].FirstOrDefault();
            var state = ctx.Request.Query["state"].FirstOrDefault();
            var error = ctx.Request.Query["error"].FirstOrDefault();

            if (!string.IsNullOrEmpty(code))
            {
                twitchOAuthService.SetAuthResult(code, state);
                return Results.Content(OAuthSuccessHtml, "text/html; charset=utf-8");
            }

            if (string.IsNullOrEmpty(error))
            {
                return Results.BadRequest();
            }

            twitchOAuthService.SetAuthError(new InvalidOperationException($"OAuth ошибка: {error}"));
            var html = string.Format(OAuthErrorHtmlTemplate, HtmlEncoder.Default.Encode(error));
            return Results.Content(html, "text/html; charset=utf-8");
        });

        app.MapGet("/chat", () => Results.Content(OverlayHtml, "text/html; charset=utf-8"));

        app.MapGet("/events", async ctx =>
        {
            ctx.Response.ContentType = "text/event-stream";
            ctx.Response.Headers.CacheControl = "no-cache";
            ctx.Response.Headers.Connection = "keep-alive";

            var bufferingFeature = ctx.Features.Get<IHttpResponseBodyFeature>();
            bufferingFeature?.DisableBuffering();

            await ctx.Response.Body.FlushAsync();

            sseService.AddClient(ctx.Response);

            try
            {
                await Task.Delay(Timeout.Infinite, ctx.RequestAborted);
            }
            catch (OperationCanceledException)
            {
            }
            finally
            {
                sseService.RemoveClient(ctx.Response);
            }
        });

        app.MapGet("/api/history", () =>
        {
            var obsSettings = settingsManager.Current.Twitch.ObsChat;
            var maxMessages = obsSettings.MaxMessages;
            var history = chatHistoryManager.GetHistory();

            if (obsSettings.EnableMessageFadeOut)
            {
                var cutoff = DateTime.UtcNow.AddSeconds(-obsSettings.MessageLifetimeSeconds);
                history = history.Where(x => x.Timestamp >= cutoff).ToList();
            }

            var finalHistory = history
                .TakeLast(maxMessages)
                .Select(DtoMapper.ToServerMessage);

            return Results.Json(finalHistory, ServerJsonOptions.Default);
        });

        app.MapGet("/api/chat-settings", () =>
        {
            var settings = settingsManager.Current.Twitch.ObsChat;
            var cssSettings = ObsChatCssSettings.FromObsChatSettings(settings);
            return Results.Json(cssSettings, ServerJsonOptions.Default);
        });

        app.MapGet("/assets/obs.css", () => Results.Bytes(ObsCssBytes, "text/css; charset=utf-8"));

        app.MapGet("/assets/obs.js", () => Results.Bytes(ObsJsBytes, "application/javascript; charset=utf-8"));

        app.MapGet("/favicon.ico", () => Results.Bytes(FaviconBytes, "image/x-icon"));
    }
}
