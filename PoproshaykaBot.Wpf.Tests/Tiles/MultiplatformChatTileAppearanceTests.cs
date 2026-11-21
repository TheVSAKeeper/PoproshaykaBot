using KeepShell.Bootstrap;
using KeepShell.Services;
using KeepShell.Testing;
using MahApps.Metro.IconPacks;
using Microsoft.Extensions.Logging.Abstractions;
using PoproshaykaBot.Core.Chat;
using PoproshaykaBot.Core.Chat.History;
using PoproshaykaBot.Core.Infrastructure.Events;
using PoproshaykaBot.Core.Platforms;
using PoproshaykaBot.Core.Settings;
using PoproshaykaBot.Core.Settings.Chat;
using PoproshaykaBot.Core.Settings.Stores;
using PoproshaykaBot.Core.Users;
using PoproshaykaBot.Wpf.Bootstrap;
using PoproshaykaBot.Wpf.ViewModels.Tiles;
using PoproshaykaBot.Wpf.Views.Tiles;
using System.IO;
using System.Windows;
using System.Windows.Controls;

namespace PoproshaykaBot.Wpf.Tests.Tiles;

[TestFixture]
[Apartment(ApartmentState.STA)]
[NonParallelizable]
public sealed class MultiplatformChatTileAppearanceTests
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
        _directory = Directory.CreateTempSubdirectory("chat-feed-appearance");
    }

    [TearDown]
    public void TearDown()
    {
        _directory.Delete(recursive: true);
    }

    [Test]
    public void Значения_по_умолчанию_и_кнопка_настроек_в_шапке_инициализируются_корректно()
    {
        using var fixture = CreateFixture();
        using var tile = fixture.CreateTile();

        var appearanceAction = tile.HeaderActions.Single(action =>
            ReferenceEquals(action.Command, tile.ToggleAppearancePanelCommand));

        using (Assert.EnterMultipleScope())
        {
            Assert.That(tile.FeedFontSize, Is.EqualTo(MultiplatformChatTileViewModel.DefaultFeedFontSize));
            Assert.That(tile.IsCompactMode, Is.False);
            Assert.That(tile.ShowTimestamps, Is.True);
            Assert.That(tile.ShowPlatformIcons, Is.True);
            Assert.That(tile.ShowRoleBadges, Is.True);
            Assert.That(tile.IsAppearancePanelOpen, Is.False);

            Assert.That(tile.FeedMetaFontSize, Is.EqualTo(11.0));
            Assert.That(tile.FeedLineHeight, Is.EqualTo(19.0));
            Assert.That(tile.FeedItemPadding, Is.EqualTo(new Thickness(2, 3, 2, 3)));
            Assert.That(tile.FeedPlatformIconSize, Is.EqualTo(14.0));
            Assert.That(tile.FeedFontSizeText, Is.EqualTo("13 px"));
            Assert.That(tile.CanDecreaseFontSize, Is.True);
            Assert.That(tile.CanIncreaseFontSize, Is.True);

            Assert.That(appearanceAction.Icon, Is.EqualTo(PackIconLucideKind.SlidersHorizontal));
            Assert.That(appearanceAction.ToolTip, Is.EqualTo("Настройки отображения ленты"));
        }
    }

    [Test]
    public void Границы_шрифта_и_расчётные_метрики_в_обычном_и_компактном_режимах()
    {
        using var fixture = CreateFixture();
        using var tile = fixture.CreateTile();

        tile.FeedFontSize = 5.0;
        using (Assert.EnterMultipleScope())
        {
            Assert.That(tile.FeedFontSize, Is.EqualTo(MultiplatformChatTileViewModel.MinFeedFontSize));
            Assert.That(tile.FeedMetaFontSize, Is.EqualTo(10.0));
            Assert.That(tile.FeedLineHeight, Is.EqualTo(16.0));
            Assert.That(tile.FeedPlatformIconSize, Is.EqualTo(12.0));
            Assert.That(tile.FeedFontSizeText, Is.EqualTo("11 px"));
            Assert.That(tile.CanDecreaseFontSize, Is.False);
            Assert.That(tile.DecreaseFontSizeCommand.CanExecute(null), Is.False);
            Assert.That(tile.CanIncreaseFontSize, Is.True);
            Assert.That(tile.IncreaseFontSizeCommand.CanExecute(null), Is.True);
        }

        tile.IncreaseFontSizeCommand.Execute(null);
        Assert.That(tile.FeedFontSize, Is.EqualTo(12.0));
        Assert.That(tile.CanDecreaseFontSize, Is.True);

        tile.FeedFontSize = 99.0;
        using (Assert.EnterMultipleScope())
        {
            Assert.That(tile.FeedFontSize, Is.EqualTo(MultiplatformChatTileViewModel.MaxFeedFontSize));
            Assert.That(tile.FeedMetaFontSize, Is.EqualTo(22.0));
            Assert.That(tile.FeedLineHeight, Is.EqualTo(36.0));
            Assert.That(tile.FeedPlatformIconSize, Is.EqualTo(25.0));
            Assert.That(tile.FeedFontSizeText, Is.EqualTo("24 px"));
            Assert.That(tile.CanIncreaseFontSize, Is.False);
            Assert.That(tile.IncreaseFontSizeCommand.CanExecute(null), Is.False);
            Assert.That(tile.CanDecreaseFontSize, Is.True);
            Assert.That(tile.DecreaseFontSizeCommand.CanExecute(null), Is.True);
        }

        tile.IsCompactMode = true;
        using (Assert.EnterMultipleScope())
        {
            Assert.That(tile.FeedLineHeight, Is.EqualTo(31.0));
            Assert.That(tile.FeedItemPadding, Is.EqualTo(new Thickness(2, 1, 2, 1)));
        }

        tile.DecreaseFontSizeCommand.Execute(null);
        Assert.That(tile.FeedFontSize, Is.EqualTo(23.0));
        Assert.That(tile.CanIncreaseFontSize, Is.True);
    }

    [Test]
    public void Настройки_сохраняются_и_восстанавливаются_через_ISettingsStore_с_откатом_на_невалидных_данных()
    {
        var store = new MemorySettings();
        using var fixture = CreateFixture();

        using (var first = fixture.CreateTile(store))
        {
            using (Assert.EnterMultipleScope())
            {
                Assert.That(store.GetStringValue(SettingsKeys.ChatFeedFontSize), Is.Null);
                Assert.That(store.GetStringValue(SettingsKeys.ChatFeedCompactMode), Is.Null);
                Assert.That(store.GetStringValue(SettingsKeys.ChatFeedShowTimestamps), Is.Null);
                Assert.That(store.GetStringValue(SettingsKeys.ChatFeedShowPlatformIcons), Is.Null);
                Assert.That(store.GetStringValue(SettingsKeys.ChatFeedShowRoleBadges), Is.Null);
            }

            first.FeedFontSize = 18.0;
            first.IsCompactMode = true;
            first.ShowTimestamps = false;
            first.ShowPlatformIcons = false;
            first.ShowRoleBadges = false;
        }

        using (var restored = fixture.CreateTile(store))
        {
            using (Assert.EnterMultipleScope())
            {
                Assert.That(restored.FeedFontSize, Is.EqualTo(18.0));
                Assert.That(restored.IsCompactMode, Is.True);
                Assert.That(restored.ShowTimestamps, Is.False);
                Assert.That(restored.ShowPlatformIcons, Is.False);
                Assert.That(restored.ShowRoleBadges, Is.False);
            }
        }

        var invalidStore = new MemorySettings();
        invalidStore.SetValue(SettingsKeys.ChatFeedFontSize, "not-a-number");
        invalidStore.SetValue(SettingsKeys.ChatFeedCompactMode, "maybe");
        invalidStore.SetValue(SettingsKeys.ChatFeedShowTimestamps, "invalid");
        invalidStore.SetValue(SettingsKeys.ChatFeedShowPlatformIcons, string.Empty);
        invalidStore.SetValue(SettingsKeys.ChatFeedShowRoleBadges, "2");

        using (var fallback = fixture.CreateTile(invalidStore))
        {
            using (Assert.EnterMultipleScope())
            {
                Assert.That(fallback.FeedFontSize, Is.EqualTo(MultiplatformChatTileViewModel.DefaultFeedFontSize));
                Assert.That(fallback.IsCompactMode, Is.False);
                Assert.That(fallback.ShowTimestamps, Is.True);
                Assert.That(fallback.ShowPlatformIcons, Is.True);
                Assert.That(fallback.ShowRoleBadges, Is.True);
            }
        }

        var outOfRangeStore = new MemorySettings();
        outOfRangeStore.SetValue(SettingsKeys.ChatFeedFontSize, "50");
        using var clamped = fixture.CreateTile(outOfRangeStore);
        Assert.That(clamped.FeedFontSize, Is.EqualTo(MultiplatformChatTileViewModel.MaxFeedFontSize));
    }

    [Test]
    public void Сброс_возвращает_значения_по_умолчанию_и_сохраняет_их_в_хранилище()
    {
        var store = new MemorySettings();
        using var fixture = CreateFixture();
        using var tile = fixture.CreateTile(store);

        tile.ToggleAppearancePanelCommand.Execute(null);
        Assert.That(tile.IsAppearancePanelOpen, Is.True);

        tile.FeedFontSize = 20.0;
        tile.IsCompactMode = true;
        tile.ShowTimestamps = false;
        tile.ShowPlatformIcons = false;
        tile.ShowRoleBadges = false;

        tile.ResetAppearanceCommand.Execute(null);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(tile.FeedFontSize, Is.EqualTo(MultiplatformChatTileViewModel.DefaultFeedFontSize));
            Assert.That(tile.IsCompactMode, Is.False);
            Assert.That(tile.ShowTimestamps, Is.True);
            Assert.That(tile.ShowPlatformIcons, Is.True);
            Assert.That(tile.ShowRoleBadges, Is.True);
            Assert.That(store.GetStringValue(SettingsKeys.ChatFeedFontSize), Is.EqualTo("13"));
            Assert.That(store.GetStringValue(SettingsKeys.ChatFeedCompactMode), Is.EqualTo("false"));
            Assert.That(store.GetStringValue(SettingsKeys.ChatFeedShowTimestamps), Is.EqualTo("true"));
            Assert.That(store.GetStringValue(SettingsKeys.ChatFeedShowPlatformIcons), Is.EqualTo("true"));
            Assert.That(store.GetStringValue(SettingsKeys.ChatFeedShowRoleBadges), Is.EqualTo("true"));
        }

        using var reloaded = fixture.CreateTile(store);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(reloaded.FeedFontSize, Is.EqualTo(MultiplatformChatTileViewModel.DefaultFeedFontSize));
            Assert.That(reloaded.IsCompactMode, Is.False);
            Assert.That(reloaded.ShowTimestamps, Is.True);
            Assert.That(reloaded.ShowPlatformIcons, Is.True);
            Assert.That(reloaded.ShowRoleBadges, Is.True);
        }
    }

    [Test]
    public void Разметка_плитки_и_панели_отображения_создаётся_и_раскладывается_без_исключений()
    {
        using var fixture = CreateFixture(seedMessages: true);
        using var tile = fixture.CreateTile(new MemorySettings());

        var view = new MultiplatformChatTileView
        {
            DataContext = tile,
        };

        Layout(view);
        var panel = (Border)view.FindName("AppearancePanel");
        Assert.That(panel.Visibility, Is.EqualTo(Visibility.Collapsed));

        tile.ToggleAppearancePanelCommand.Execute(null);
        tile.FeedFontSize = 17.0;
        tile.IsCompactMode = true;
        tile.ShowTimestamps = false;
        tile.ShowPlatformIcons = false;
        tile.ShowRoleBadges = false;
        Layout(view);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(panel.Visibility, Is.EqualTo(Visibility.Visible));
            Assert.That(tile.Items.Count, Is.GreaterThanOrEqualTo(2));
        }

        tile.ResetAppearanceCommand.Execute(null);
        Layout(view);

        Assert.That(tile.FeedFontSize, Is.EqualTo(MultiplatformChatTileViewModel.DefaultFeedFontSize));
    }

    private static void Layout(UserControl view)
    {
        view.Measure(new(380, 420));
        view.Arrange(new(0, 0, 380, 420));
        view.UpdateLayout();
    }

    private TileFixture CreateFixture(bool seedMessages = false)
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

        var historySettings = new StaticChatHistorySettingsProvider(new ChatSettings { ShowHistoryInApp = true });
        var history = new ChatHistoryManager(settings, bus, historySettings, channelCatalogStore: catalog);

        if (seedMessages)
        {
            var t0 = new DateTime(2026, 11, 21, 16, 0, 0, DateTimeKind.Utc);
            history.AddMessage(new ChatMessageData
            {
                Platform = StreamingPlatform.Twitch,
                Channel = "chanA",
                ChannelId = "100",
                MessageId = "m-hist",
                Timestamp = t0,
                UserId = "u1",
                DisplayName = "OldUser",
                Message = "Историческое сообщение",
                IsHistorical = true,
                Status = UserStatus.Moderator,
            });
            history.AddMessage(new ChatMessageData
            {
                Platform = StreamingPlatform.Twitch,
                Channel = "chanA",
                ChannelId = "100",
                MessageId = "m-live",
                Timestamp = t0.AddSeconds(5),
                UserId = "u2",
                DisplayName = "LiveUser",
                Message = "Новое сообщение в ленте",
                Status = UserStatus.Vip,
            });
        }

        return new TileFixture(history, bus, historySettings);
    }

    private sealed class TileFixture(
        ChatHistoryManager history,
        InMemoryEventBus bus,
        IChatHistorySettingsProvider historySettings) : IDisposable
    {
        public MultiplatformChatTileViewModel CreateTile(ISettingsStore? uiSettings = null) =>
            new(history, bus, historySettings, uiSettings: uiSettings);

        public void Dispose() => history.Dispose();
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
