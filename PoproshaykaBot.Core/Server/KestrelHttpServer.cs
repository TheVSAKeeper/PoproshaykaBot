using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using PoproshaykaBot.Core.Server.Endpoints;
using PoproshaykaBot.Core.Settings;

namespace PoproshaykaBot.Core.Server;

public sealed class KestrelHttpServer(
    SseService sseService,
    SettingsManager settingsManager,
    IEnumerable<IEndpointMapper> endpointMappers,
    ILogger<KestrelHttpServer> logger,
    ILoggerFactory loggerFactory)
    : IAsyncDisposable, IHttpServerStatus
{
    private static readonly HttpServerRuntimeSnapshot StoppedSnapshot = new(false, null);

    private readonly IReadOnlyList<IEndpointMapper> _endpointMappers = endpointMappers.ToArray();

    private WebApplication? _app;
    private volatile HttpServerRuntimeSnapshot _runtimeSnapshot = StoppedSnapshot;

    public bool IsRunning => _runtimeSnapshot.IsRunning;

    public int? Port => _runtimeSnapshot.Port;

    public HttpServerRuntimeSnapshot CaptureSnapshot() => _runtimeSnapshot;

    public async Task StartAsync()
    {
        if (IsRunning)
        {
            return;
        }

        var port = settingsManager.Current.Twitch.HttpServerPort;

        try
        {
            var builder = WebApplication.CreateSlimBuilder(new WebApplicationOptions
            {
                Args = [],
            });

            builder.Services.AddSingleton(loggerFactory);

            builder.WebHost.ConfigureKestrel(options =>
            {
                options.ListenLocalhost(port);
            });

            _app = builder.Build();

            _app.Use(async (ctx, next) =>
            {
                if (logger.IsEnabled(LogLevel.Debug))
                {
                    var maskedQuery = QueryStringMasker.Mask(ctx.Request.QueryString.Value ?? string.Empty);
                    logger.LogDebug("HTTP запрос: {Method} {Path}{Query} (UA \"{UserAgent}\", тело {ContentLength} б, от {RemoteIp})",
                        ctx.Request.Method,
                        ctx.Request.Path,
                        maskedQuery,
                        ctx.Request.Headers.UserAgent.ToString(),
                        ctx.Request.ContentLength ?? 0,
                        ctx.Connection.RemoteIpAddress);
                }

                await next(ctx);
            });

            foreach (var mapper in _endpointMappers)
            {
                mapper.Map(_app);
            }

            sseService.Start();

            await _app.StartAsync();

            _runtimeSnapshot = new(true, port);

            logger.LogInformation("HTTP сервер запущен на порту {Port}", port);
        }
        catch (Exception ex)
        {
            await sseService.StopAsync();

            if (_app != null)
            {
                await _app.DisposeAsync();
                _app = null;
            }

            logger.LogError(ex, "Ошибка запуска HTTP сервера на порту {Port}", port);
            throw new InvalidOperationException($"Не удалось запустить HTTP-сервер на порту {port}", ex);
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
            _runtimeSnapshot = StoppedSnapshot;
            await sseService.StopAsync();

            if (_app != null)
            {
                await _app.StopAsync();
                await _app.DisposeAsync();
                _app = null;
            }

            logger.LogInformation("HTTP сервер остановлен");
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Ошибка остановки HTTP сервера");
        }
    }

    public async ValueTask DisposeAsync()
    {
        await StopAsync();
    }
}
