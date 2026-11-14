using Microsoft.Extensions.Logging;
using PoproshaykaBot.Core.Chat;
using PoproshaykaBot.Core.Platforms;
using PoproshaykaBot.Core.Settings.Stores;
using PoproshaykaBot.Core.Streaming;

namespace PoproshaykaBot.Core.Twitch;

public sealed class TwitchChatPlatform(
    IStreamStatus streamStatus,
    TwitchChatMessenger messenger,
    ChatDecorationsProvider decorations,
    PlatformChannelRegistry channels,
    AccountsStore accountsStore,
    ILogger<TwitchChatPlatform>? logger = null) : IChatPlatform
{
    private const string BotFallbackDisplayName = "PoproshaykaBot";

    public StreamingPlatform Id => StreamingPlatform.Twitch;

    public string DisplayName => "Twitch";

    public PlatformCapabilities Capabilities =>
        PlatformCapabilities.Reply
        | PlatformCapabilities.Whisper
        | PlatformCapabilities.NativePolls
        | PlatformCapabilities.ChannelMetadataEdit
        | PlatformCapabilities.EmotePositions;

    public bool IsEnabled => true;

    public bool IsConnected => !string.IsNullOrWhiteSpace(channels.GetChannel(StreamingPlatform.Twitch));

    public IStreamStatus StreamStatus => streamStatus;

    public IPlatformMessenger Messenger => messenger;

    public IChatDecorations Decorations => decorations;

    public PlatformBotIdentity? BotIdentity
    {
        get
        {
            var bot = accountsStore.LoadBot();

            if (string.IsNullOrWhiteSpace(bot.UserId) && string.IsNullOrWhiteSpace(bot.Login))
            {
                logger?.LogDebug("Учётная запись бота Twitch ещё не содержит идентификатора и логина");
                return null;
            }

            var displayName = string.IsNullOrWhiteSpace(bot.Login) ? BotFallbackDisplayName : bot.Login;
            return new(bot.UserId, displayName);
        }
    }
}
