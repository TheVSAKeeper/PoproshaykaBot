using PoproshaykaBot.Core.Chat;
using PoproshaykaBot.Core.Platforms;
using PoproshaykaBot.Core.Streaming;
using PoproshaykaBot.Core.Twitch.Chat;

using PlatformStreamStatus = PoproshaykaBot.Core.Streaming.StreamStatus;

namespace PoproshaykaBot.Core.Tests.Fakes;

internal sealed class FakeChatPlatform : IChatPlatform
{
    private readonly PlatformChannelRegistry? _channels;
    private bool _isConnected;

    public FakeChatPlatform(
        StreamingPlatform id,
        string displayName,
        PlatformCapabilities capabilities = PlatformCapabilities.None,
        bool isEnabled = true,
        bool isConnected = true,
        PlatformBotIdentity? botIdentity = null,
        IChatDecorations? decorations = null,
        PlatformChannelRegistry? channels = null)
    {
        Id = id;
        DisplayName = displayName;
        Capabilities = capabilities;
        IsEnabled = isEnabled;
        _isConnected = isConnected;
        _channels = channels;
        BotIdentity = botIdentity;
        Decorations = decorations ?? NullChatDecorations.Instance;
    }

    public StreamingPlatform Id { get; }

    public string DisplayName { get; }

    public PlatformCapabilities Capabilities { get; set; }

    public bool IsEnabled { get; set; }

    public bool IsConnected
    {
        get => _channels is not null
            ? !string.IsNullOrWhiteSpace(_channels.GetChannel(Id))
            : _isConnected;
        set => _isConnected = value;
    }

    public StreamStatusStub FakeStreamStatus { get; } = new();

    public IStreamStatus StreamStatus => FakeStreamStatus;

    public PlatformMessengerStub FakeMessenger { get; } = new();

    public IPlatformMessenger Messenger => FakeMessenger;

    public IChatDecorations Decorations { get; set; }

    public PlatformBotIdentity? BotIdentity { get; set; }

    public sealed class StreamStatusStub : IStreamStatus
    {
        private StreamStatusSnapshot _snapshot = new(PlatformStreamStatus.Offline, null, null);

        public StreamStatus CurrentStatus => _snapshot.Status;

        public StreamInfo? CurrentStream => _snapshot.Stream;

        public StreamStatusSnapshot GetSnapshot() => _snapshot;

        public Task RefreshLiveSnapshotAsync() => Task.CompletedTask;

        public void SetOffline()
        {
            _snapshot = new(PlatformStreamStatus.Offline, null, null);
        }

        public void SetUnknown()
        {
            _snapshot = new(PlatformStreamStatus.Unknown, null, null);
        }

        public void SetOnline(StreamInfo? stream, DateTimeOffset? viewerSampledAtUtc)
        {
            _snapshot = new(PlatformStreamStatus.Online, stream, viewerSampledAtUtc);
        }

        public void SetSnapshot(StreamStatusSnapshot snapshot)
        {
            _snapshot = snapshot;
        }
    }

    public sealed class PlatformMessengerStub : IPlatformMessenger
    {
        public ChatEnqueueResult EnqueueResult { get; set; } = ChatEnqueueResult.Queued;

        public List<(string Text, CommandResponseMark? Mark)> Sent { get; } = [];

        public List<(string ReplyToMessageId, string CallerDisplayName, string Text, CommandResponseMark? Mark)> Replies { get; } = [];

        public List<(string ToUserId, string FallbackReplyToMessageId, string CallerDisplayName, string Text, CommandResponseMark? Mark)> Whispers { get; } = [];

        public ChatEnqueueResult Send(string text, CommandResponseMark? commandResponse = null)
        {
            if (EnqueueResult == ChatEnqueueResult.Queued)
            {
                Sent.Add((text, commandResponse));
            }

            return EnqueueResult;
        }

        public void Reply(string replyToMessageId, string callerDisplayName, string text, CommandResponseMark? commandResponse = null)
        {
            Replies.Add((replyToMessageId, callerDisplayName, text, commandResponse));
        }

        public void Whisper(
            string toUserId,
            string fallbackReplyToMessageId,
            string callerDisplayName,
            string text,
            CommandResponseMark? commandResponse = null)
        {
            Whispers.Add((toUserId, fallbackReplyToMessageId, callerDisplayName, text, commandResponse));
        }
    }
}
