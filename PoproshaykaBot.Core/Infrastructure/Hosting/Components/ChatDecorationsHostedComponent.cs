using Microsoft.Extensions.Logging;
using PoproshaykaBot.Core.Chat;

namespace PoproshaykaBot.Core.Infrastructure.Hosting.Components;

internal sealed class ChatDecorationsHostedComponent(
    ChatDecorationsProvider provider,
    ILogger<ChatDecorationsHostedComponent> logger)
    : IHostedComponent
{
    public string Name => "Загрузка эмодзи и бэйджей...";

    public int StartOrder => 200;

    public async Task StartAsync(IProgress<string> progress, CancellationToken cancellationToken)
    {
        await provider.LoadAsync().ConfigureAwait(false);

        logger.LogInformation("Успешно загружено {GlobalEmotesCount} глобальных эмодзи и {GlobalBadgeSetsCount} наборов глобальных бэйджей",
            provider.GlobalEmotesCount,
            provider.GlobalBadgeSetsCount);

        progress.Report($"Загружено {provider.GlobalEmotesCount} глобальных эмодзи и {provider.GlobalBadgeSetsCount} типов глобальных бэйджей");
    }

    public Task StopAsync(IProgress<string> progress, CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }
}
