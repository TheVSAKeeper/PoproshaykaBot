using KeepShell.Services.Platform;
using Microsoft.Extensions.Logging.Abstractions;
using PoproshaykaBot.Core.Chat;
using PoproshaykaBot.Core.Infrastructure.Events;
using PoproshaykaBot.Core.Platforms;
using PoproshaykaBot.Core.Twitch.Chat;
using PoproshaykaBot.Wpf.ViewModels.Onboarding;

namespace PoproshaykaBot.Wpf.Tests.Onboarding;

[TestFixture]
public sealed class HealthCheckPageViewModelTests
{
    [TestCase(ChatEnqueueResult.Queued, true, "✓ Сообщение поставлено в очередь отправки.")]
    [TestCase(ChatEnqueueResult.ObserverMode, false, "✗ Сообщение не отправлено: режим наблюдателя.")]
    public void Тест_чата_показывает_исход_постановки(ChatEnqueueResult outcome, bool success, string expectedText)
    {
        var messenger = new FakeChatMessenger(outcome);

        using var viewModel = new HealthCheckPageViewModel(messenger,
            new InMemoryEventBus(NullLogger<InMemoryEventBus>.Instance),
            NullLogger<HealthCheckPageViewModel>.Instance,
            new FakeShellLauncher());

        viewModel.ChatTestMessage = "проверка";
        viewModel.SendChatTestCommand.Execute(null);

        Assert.Multiple(() =>
        {
            Assert.That(messenger.Sent, Is.EqualTo(new[] { "проверка" }));
            Assert.That(viewModel.ChatTestStatusText, Is.EqualTo(expectedText));
            Assert.That(viewModel.ChatTestSuccess, Is.EqualTo(success));
            Assert.That(viewModel.ChatTestError, Is.EqualTo(!success));
        });
    }

    private sealed class FakeChatMessenger(ChatEnqueueResult outcome) : IBroadcastMessenger
    {
        public List<string> Sent { get; } = [];

        public ChatEnqueueResult Send(StreamingPlatform platform, string text, ChatSendCategory category = ChatSendCategory.None)
        {
            Sent.Add(text);
            return outcome;
        }

        public BroadcastSendResult Send(string text, IReadOnlySet<StreamingPlatform> platforms, ChatSendCategory category = ChatSendCategory.None) => new(new Dictionary<StreamingPlatform, ChatEnqueueResult>());

        public BroadcastSendResult SendToConnected(string text, ChatSendCategory category = ChatSendCategory.None) => new(new Dictionary<StreamingPlatform, ChatEnqueueResult>());

        public BroadcastSendResult Send(Func<IChatPlatform, string?> messageFactory, IReadOnlySet<StreamingPlatform>? platforms = null, ChatSendCategory category = ChatSendCategory.None) => new(new Dictionary<StreamingPlatform, ChatEnqueueResult>());
    }

    private sealed class FakeShellLauncher : IShellLauncher
    {
        public bool Open(string pathOrUrl) => true;

        public bool Reveal(string path) => true;

        public bool Start(string executable, params string[] arguments) => true;
    }
}
