using KeepShell.Testing;
using Microsoft.Extensions.Logging.Abstractions;
using PoproshaykaBot.Core.Chat;
using PoproshaykaBot.Core.Chat.History;
using PoproshaykaBot.Core.Infrastructure.Events;
using PoproshaykaBot.Core.Infrastructure.Events.Chat;
using PoproshaykaBot.Core.Platforms;
using PoproshaykaBot.Core.Server.Images;
using PoproshaykaBot.Core.Settings;
using PoproshaykaBot.Core.Settings.Chat;
using PoproshaykaBot.Core.Settings.Obs;
using PoproshaykaBot.Core.Settings.Stores;
using PoproshaykaBot.Core.Users;
using PoproshaykaBot.Wpf.Infrastructure;
using PoproshaykaBot.Wpf.ViewModels.Tiles;
using PoproshaykaBot.Wpf.ViewModels.Tiles.Chat;
using PoproshaykaBot.Wpf.Views.Tiles;
using System.IO;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace PoproshaykaBot.Wpf.Tests.Tiles;

[TestFixture]
[Apartment(ApartmentState.STA)]
[NonParallelizable]
public sealed class MultiplatformChatTileImagesTests
{
    private static readonly string[] Dictionaries =
    [
        "pack://application:,,,/KeepShell;component/Resources/Themes/Light.xaml",
        "pack://application:,,,/KeepShell;component/Resources/Themes/Tokens.xaml",
        "pack://application:,,,/KeepShell;component/Resources/Converters.xaml",
        "pack://application:,,,/KeepShell;component/Resources/Styles/Controls.xaml",
        "pack://application:,,,/PoproshaykaBot.Wpf;component/Infrastructure/Converters/AppConverters.xaml",
    ];

    private DirectoryInfo _directory = null!;

    [OneTimeSetUp]
    public void EnsureApplication()
    {
        TestResources.Ensure(Dictionaries);
    }

    [SetUp]
    public void SetUp()
    {
        _directory = Directory.CreateTempSubdirectory("chat-feed-images");
    }

    [TearDown]
    public void TearDown()
    {
        _directory.Delete(recursive: true);
    }

    [Test]
    public async Task Messages_RespectObsChatSettings_LoadImagesAndReactToStoreChangesUntilDisposed()
    {
        var bus = new InMemoryEventBus(NullLogger<InMemoryEventBus>.Instance);
        var settings = new SettingsManager(NullLogger<SettingsManager>.Instance, Path.Combine(_directory.FullName, "settings.json"));
        var catalog = new ChannelCatalogStore(NullLogger<ChannelCatalogStore>.Instance, Path.Combine(_directory.FullName, "channels.json"));
        catalog.Save(new()
        {
            Channels =
            [
                new() { Platform = StreamingPlatform.Twitch, ChannelId = "100", DisplayName = "chanA", IsConfigured = true },
            ],
            Platforms =
            [
                new() { Platform = StreamingPlatform.Twitch, ActiveChannelId = "100" },
            ],
        });

        var obsChatStore = new ObsChatStore(bus, NullLogger<ObsChatStore>.Instance, Path.Combine(_directory.FullName, "obs-chat.json"));
        obsChatStore.Save(new ObsChatSettings
        {
            ShowMessageImages = true,
            MessageImageRoles = MessageImageSenderRoles.Broadcaster | MessageImageSenderRoles.Bot,
            MessageImageMaxHeightPixels = 180,
        });

        var pngBytes = CreatePngBytes();
        var provider = new ChatMessageImageProvider(
            (_, _, _) => Task.FromResult<MessageImageContent?>(new MessageImageContent(pngBytes, "image/png")),
            NullLogger<ChatMessageImageProvider>.Instance);

        var historySettings = new StaticChatHistorySettingsProvider(new ChatSettings { ShowHistoryInApp = true });
        using var history = new ChatHistoryManager(settings, bus, historySettings, channelCatalogStore: catalog);
        var tile = new MultiplatformChatTileViewModel(
            history,
            bus,
            historySettings,
            obsChatStore: obsChatStore,
            imageProvider: provider);

        var botMessage = new ChatMessageData
        {
            Platform = StreamingPlatform.Twitch,
            Channel = "chanA",
            ChannelId = "100",
            MessageId = "m-bot",
            Timestamp = DateTime.UtcNow,
            UserId = "bot-1",
            DisplayName = "Bot",
            Message = "глянь https://i.imgur.com/bot.png",
            MessageType = ChatMessageType.BotResponse,
            IsBot = true,
        };

        await bus.PublishAsync(new ChatMessageReceived(
            StreamingPlatform.Twitch,
            "chanA",
            botMessage.MessageId,
            botMessage.UserId,
            "bot",
            botMessage.DisplayName,
            botMessage.Message,
            UserStatus.None,
            false,
            botMessage,
            IsBot: true));

        await provider.WaitForPendingAsync();

        var item = tile.Items.OfType<ChatFeedMessageItemViewModel>().Single();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(item.ImageHandle.HasImage, Is.True);
            Assert.That(item.ImageMaxHeight, Is.EqualTo(180));
        }

        var view = new MultiplatformChatTileView { DataContext = tile };
        view.Measure(new(380, 420));
        view.Arrange(new(0, 0, 380, 420));
        view.UpdateLayout();

        obsChatStore.Save(new ObsChatSettings { ShowMessageImages = false });

        using (Assert.EnterMultipleScope())
        {
            Assert.That(item.ImageHandle, Is.SameAs(ChatMessageImageHandle.None));
            Assert.That(item.ImageHandle.HasImage, Is.False);
            Assert.That(item.ImageMaxHeight, Is.Zero);
        }

        tile.Dispose();

        obsChatStore.Save(new ObsChatSettings
        {
            ShowMessageImages = true,
            MessageImageRoles = MessageImageSenderRoles.Bot,
            MessageImageMaxHeightPixels = 200,
        });

        Assert.That(item.ImageHandle, Is.SameAs(ChatMessageImageHandle.None));
    }

    private static byte[] CreatePngBytes()
    {
        var source = BitmapSource.Create(4, 4, 96, 96, PixelFormats.Bgr24, null, new byte[4 * 4 * 3], 4 * 3);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(source));

        using var stream = new MemoryStream();
        encoder.Save(stream);
        return stream.ToArray();
    }

    private sealed class StaticChatHistorySettingsProvider(ChatSettings current) : IChatHistorySettingsProvider
    {
        public event Action<ChatSettings>? Changed
        {
            add { }
            remove { }
        }

        public ChatSettings GetCurrent() => current;
    }
}
