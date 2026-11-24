using KeepShell.Bootstrap;
using KeepShell.Services;
using KeepShell.Services.Platform;
using KeepShell.Testing;
using KeepShell.ViewModels;
using Microsoft.Extensions.Logging.Abstractions;
using PoproshaykaBot.Core.Chat;
using PoproshaykaBot.Core.Debugging;
using PoproshaykaBot.Core.Infrastructure.Events;
using PoproshaykaBot.Core.Infrastructure.Hosting;
using PoproshaykaBot.Core.Platforms;
using PoproshaykaBot.Core.Platforms.Auth;
using PoproshaykaBot.Core.Server;
using PoproshaykaBot.Core.Settings;
using PoproshaykaBot.Core.Settings.Stores;
using PoproshaykaBot.Core.Statistics;
using PoproshaykaBot.Core.Streaming;
using PoproshaykaBot.Core.Twitch.Auth;
using PoproshaykaBot.Core.YouTube.Auth;
using PoproshaykaBot.Wpf.Bootstrap;
using PoproshaykaBot.Wpf.Infrastructure;
using PoproshaykaBot.Wpf.Tests.Support;
using PoproshaykaBot.Wpf.ViewModels;
using PoproshaykaBot.Wpf.ViewModels.Settings;
using PoproshaykaBot.Wpf.ViewModels.Settings.Contributors;
using PoproshaykaBot.Wpf.ViewModels.Tiles;
using PoproshaykaBot.Wpf.Views;
using PoproshaykaBot.Wpf.Views.Settings;
using System.IO;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace PoproshaykaBot.Wpf.Tests.Settings;

[TestFixture]
[Apartment(ApartmentState.STA)]
[NonParallelizable]
public sealed class MultistreamLayoutTests
{
    private static readonly string[] Dictionaries =
    [
        "pack://application:,,,/KeepShell;component/Resources/Themes/Dark.xaml",
        "pack://application:,,,/KeepShell;component/Resources/Themes/Tokens.xaml",
        "pack://application:,,,/KeepShell;component/Resources/Converters.xaml",
        "pack://application:,,,/KeepShell;component/Resources/Styles/Controls.xaml",
        "pack://application:,,,/PoproshaykaBot.Wpf;component/Infrastructure/Converters/AppConverters.xaml",
        "pack://application:,,,/PoproshaykaBot.Wpf;component/Resources/TableStyles.xaml",
        "pack://application:,,,/PoproshaykaBot.Wpf;component/Resources/RatingStyles.xaml",
        "pack://application:,,,/PoproshaykaBot.Wpf;component/Resources/PlatformIcons.xaml",
    ];

    private string _directory = null!;

    [OneTimeSetUp]
    public void EnsureApplication()
    {
        TestResources.Ensure(Dictionaries);
        RuntimeHelpers.RunModuleConstructor(typeof(MultistreamPageView).Module.ModuleHandle);
        ViewLocator.InstallIntoApplication();
    }

    [SetUp]
    public void SetUp()
    {
        _directory = Path.Combine(Path.GetTempPath(), "poproshayka-ms-layout-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_directory);
    }

    [TearDown]
    public void TearDown()
    {
        ApplyScale(FontScaleManager.DefaultScale);
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }

    [TestCase(1660, 980, 1.0, Description = "1920x1080 wide")]
    [TestCase(1180, 800, 1.0, Description = "1440x900 wide")]
    [TestCase(1020, 700, 1.0, Description = "1280x800 wide")]
    [TestCase(940, 700, 1.0, Description = "1200x780 narrow-wide threshold")]
    [TestCase(840, 620, 1.0, Description = "1100x720 default window")]
    [TestCase(764, 540, 1.0, Description = "1024x640 compact")]
    [TestCase(1020, 700, 1.6, Description = "1280x800 scale 1.6")]
    [TestCase(1660, 980, 1.6, Description = "1920x1080 scale 1.6 wide")]
    public async Task Multistream_cards_and_channel_rows_do_not_clip_at_supported_sizes(double width, double height, double scale)
    {
        ApplyScale(scale);
        await using var harness = CreateHarness();
        var view = new MultistreamPageView { DataContext = harness.Page };
        var host = HostWithMainWindowFont(view);
        ArrangeWithResponsivePass(host, view, new(width, height));

        var masterPane = (ScrollViewer)view.FindName("MasterPane")!;
        var platformCards = (ListBox)view.FindName("PlatformCards")!;
        var detailScroll = (ScrollViewer)view.FindName("DetailScroll")!;
        var automationButton = Descendants<Button>(masterPane)
            .Single(b => System.Windows.Automation.AutomationProperties.GetAutomationId(b) == "MultistreamOpenAutomation");

        Assert.Multiple(() =>
        {
            foreach (var card in harness.Page.Cards)
            {
                var container = (ListBoxItem)platformCards.ItemContainerGenerator.ContainerFromItem(card);
                Assert.That(container, Is.Not.Null);
                var bounds = container.TransformToAncestor(masterPane).TransformBounds(new(container.RenderSize));
                Assert.That(bounds.Right, Is.LessThanOrEqualTo(masterPane.ViewportWidth + 0.5),
                    $"Карточка {card.Status.DisplayName} обрезана справа при {width}x{height} @ {scale}: Right={bounds.Right:F1}, ViewportWidth={masterPane.ViewportWidth:F1}");
                Assert.That(bounds.Bottom, Is.LessThanOrEqualTo(masterPane.ActualHeight + 0.5),
                    $"Карточка {card.Status.DisplayName} обрезана снизу в MasterPane при {width}x{height} @ {scale}: Bottom={bounds.Bottom:F1}, MasterHeight={masterPane.ActualHeight:F1}");

                var cardBorder = Descendants<Border>(container).First(b => b.Name == "CardBorder");
                var cardInnerRight = cardBorder.ActualWidth - cardBorder.Padding.Right - cardBorder.BorderThickness.Right;

                var titleText = Descendants<TextBlock>(cardBorder).First(t => t.Text == card.Status.DisplayName);
                var titleBounds = titleText.TransformToAncestor(cardBorder).TransformBounds(new(titleText.RenderSize));
                var naturalTitle = MeasureUnconstrainedWidth(titleText);
                Assert.That(titleText.ActualWidth, Is.GreaterThanOrEqualTo(naturalTitle - 1.0),
                    $"Название «{card.Status.DisplayName}» на карточке обрезано при {width}x{height} @ {scale}: Actual={titleText.ActualWidth:F1}, Natural={naturalTitle:F1}");

                foreach (var button in Descendants<Button>(cardBorder).Where(IsEffectivelyVisible))
                {
                    var btnBounds = button.TransformToAncestor(cardBorder).TransformBounds(new(button.RenderSize));
                    Assert.That(btnBounds.Right, Is.LessThanOrEqualTo(cardInnerRight + 0.5),
                        $"Кнопка «{button.Content}» на карточке {card.Status.DisplayName} обрезана справа при {width}x{height} @ {scale}: Right={btnBounds.Right:F1}, InnerRight={cardInnerRight:F1}");
                    Assert.That(titleBounds.Right, Is.LessThanOrEqualTo(btnBounds.Left + 0.5),
                        $"Название «{card.Status.DisplayName}» налезает на кнопку «{button.Content}» при {width}x{height} @ {scale}: TitleRight={titleBounds.Right:F1}, BtnLeft={btnBounds.Left:F1}");
                }

                if (!view.IsCompactLayout && card.Status.HasMultipleChannels)
                {
                    var countText = Descendants<TextBlock>(cardBorder).First(t => t.Text == card.Status.ChannelCountText);
                    var countBounds = countText.TransformToAncestor(cardBorder).TransformBounds(new(countText.RenderSize));
                    Assert.That(countBounds.Right, Is.LessThanOrEqualTo(cardInnerRight + 0.5),
                        $"Счётчик каналов на карточке {card.Status.DisplayName} обрезан справа при {width}x{height} @ {scale}: Right={countBounds.Right:F1}, InnerRight={cardInnerRight:F1}");
                }
            }

            var autoBounds = automationButton.TransformToAncestor(masterPane).TransformBounds(new(automationButton.RenderSize));
            Assert.That(autoBounds.Right, Is.LessThanOrEqualTo(masterPane.ViewportWidth + 0.5),
                $"Кнопка «Профили и автоматика» обрезана справа при {width}x{height} @ {scale}: Right={autoBounds.Right:F1}, ViewportWidth={masterPane.ViewportWidth:F1}");
            Assert.That(autoBounds.Bottom, Is.LessThanOrEqualTo(masterPane.ActualHeight + 0.5),
                $"Кнопка «Профили и автоматика» обрезана снизу при {width}x{height} @ {scale}: Bottom={autoBounds.Bottom:F1}, MasterHeight={masterPane.ActualHeight:F1}");

            foreach (var card in harness.Page.Cards)
            {
                harness.Page.SelectPlatformCommand.Execute(card);
                ArrangeWithResponsivePass(host, view, new(width, height));

                var guidedCard = (Border)view.FindName("GuidedOverviewCard")!;
                var guidedBounds = guidedCard.TransformToAncestor(detailScroll).TransformBounds(new(guidedCard.RenderSize));
                var guidedInnerRight = guidedCard.ActualWidth - guidedCard.Padding.Right - guidedCard.BorderThickness.Right;
                Assert.That(guidedBounds.Right, Is.LessThanOrEqualTo(detailScroll.ViewportWidth + 0.5),
                    $"Карточка готовности {card.Status.DisplayName} обрезана справа при {width}x{height} @ {scale}: Right={guidedBounds.Right:F1}, Viewport={detailScroll.ViewportWidth:F1}");
                foreach (var wizardButton in Descendants<Button>(guidedCard).Where(IsEffectivelyVisible))
                {
                    var btnBounds = wizardButton.TransformToAncestor(guidedCard).TransformBounds(new(wizardButton.RenderSize));
                    Assert.That(btnBounds.Right, Is.LessThanOrEqualTo(guidedInnerRight + 0.5),
                        $"Кнопка мастера на {card.Status.DisplayName} обрезана справа при {width}x{height} @ {scale}: Right={btnBounds.Right:F1}, InnerRight={guidedInnerRight:F1}");
                }

                var catalogView = Descendants<ChannelCatalogSettingsSectionView>(view).Single();
                var channelList = Descendants<ListBox>(catalogView).Single();
                var cardGrid = Descendants<System.Windows.Controls.Primitives.UniformGrid>(channelList).Single();
                Assert.That(cardGrid.Columns, Is.EqualTo(catalogView.IsTwoColumnCards ? 2 : 1));
                List<double> rowHeights = [];

                foreach (var channel in harness.Editor.VisibleChannels)
                {
                    var row = (ListBoxItem)channelList.ItemContainerGenerator.ContainerFromItem(channel);
                    Assert.That(row, Is.Not.Null);
                    rowHeights.Add(row.ActualHeight);

                    var rowBounds = row.TransformToAncestor(detailScroll).TransformBounds(new(row.RenderSize));
                    Assert.That(rowBounds.Right, Is.LessThanOrEqualTo(detailScroll.ViewportWidth + 0.5),
                        $"Строка канала {channel.DisplayName} обрезана справа при {width}x{height} @ {scale}: Right={rowBounds.Right:F1}, Viewport={detailScroll.ViewportWidth:F1}");

                    var rowBorder = Descendants<Border>(row).First(b => b.Name == "CardBorder");
                    var rowInnerRight = rowBorder.ActualWidth - rowBorder.Padding.Right - rowBorder.BorderThickness.Right;

                    var nameText = Descendants<TextBlock>(rowBorder).First(t => t.Text == channel.DisplayName);
                    var namePanel = (Panel)VisualTreeHelper.GetParent(nameText);
                    var nameBoundsInPanel = nameText.TransformToAncestor(namePanel).TransformBounds(new(nameText.RenderSize));
                    var nameBoundsInRow = nameText.TransformToAncestor(rowBorder).TransformBounds(new(nameText.RenderSize));
                    var naturalWidth = MeasureUnconstrainedWidth(nameText);
                    Assert.That(nameText.ActualWidth, Is.GreaterThanOrEqualTo(naturalWidth - 1.0),
                        $"Имя канала «{channel.DisplayName}» ({card.Status.DisplayName}) сжато при {width}x{height} @ {scale}: Actual={nameText.ActualWidth:F1}, Natural={naturalWidth:F1}");
                    Assert.That(nameBoundsInPanel.Right, Is.LessThanOrEqualTo(namePanel.ActualWidth + 0.5),
                        $"Имя канала «{channel.DisplayName}» ({card.Status.DisplayName}) вылезло за колонку при {width}x{height} @ {scale}: NameRight={nameBoundsInPanel.Right:F1}, PanelWidth={namePanel.ActualWidth:F1}");

                    foreach (var tb in Descendants<TextBlock>(rowBorder).Where(t => IsEffectivelyVisible(t) && t.ActualWidth > 0))
                    {
                        var tbBounds = tb.TransformToAncestor(rowBorder).TransformBounds(new(tb.RenderSize));
                        Assert.That(tbBounds.Right, Is.LessThanOrEqualTo(rowInnerRight + 0.5),
                            $"Текст «{tb.Text}» в строке «{channel.DisplayName}» обрезан справа при {width}x{height} @ {scale}: Right={tbBounds.Right:F1}, InnerRight={rowInnerRight:F1}");
                        if (!ReferenceEquals(tb, nameText) && !ReferenceEquals(VisualTreeHelper.GetParent(tb), namePanel)
                            && !ReferenceEquals(VisualTreeHelper.GetParent(VisualTreeHelper.GetParent(tb)), namePanel)
                            && VerticalRangesOverlap(nameBoundsInRow, tbBounds))
                        {
                            Assert.That(nameBoundsInRow.Right, Is.LessThanOrEqualTo(tbBounds.Left + 0.5),
                                $"Имя «{channel.DisplayName}» налезает на «{tb.Text}» при {width}x{height} @ {scale}: NameRight={nameBoundsInRow.Right:F1}, OtherLeft={tbBounds.Left:F1}");
                        }
                    }

                    foreach (var actionButton in Descendants<Button>(rowBorder).Where(IsEffectivelyVisible))
                    {
                        var btnBounds = actionButton.TransformToAncestor(rowBorder).TransformBounds(new(actionButton.RenderSize));
                        Assert.That(btnBounds.Right, Is.LessThanOrEqualTo(rowInnerRight + 0.5),
                            $"Кнопка в строке канала «{channel.DisplayName}» обрезана справа при {width}x{height} @ {scale}: Right={btnBounds.Right:F1}, InnerRight={rowInnerRight:F1}");
                    }
                }

                if (rowHeights.Count >= 2)
                {
                    Assert.That(rowHeights.Max() - rowHeights.Min(), Is.LessThanOrEqualTo(0.5),
                        $"Разная высота строк каналов на {card.Status.DisplayName} при {width}x{height} @ {scale}: [{string.Join(", ", rowHeights.Select(h => $"{h:F1}"))}]");
                }

                var addInput = Descendants<TextBox>(catalogView)
                    .Single(t => System.Windows.Automation.AutomationProperties.GetName(t) == "Логин, ID или ссылка на канал");
                Assert.That(addInput.ActualWidth, Is.GreaterThanOrEqualTo(140),
                    $"Поле добавления канала слишком узкое на {card.Status.DisplayName} при {width}x{height} @ {scale}: Width={addInput.ActualWidth:F1}");
            }
        });
    }

    [TestCase("Light", 764, 540, StreamingPlatform.Twitch)]
    [TestCase("Dark", 1390, 840, StreamingPlatform.YouTube)]
    public async Task Multistream_channel_cards_keep_live_offline_and_unavailable_states_distinct(
        string theme, int width, int height, StreamingPlatform platform)
    {
        var active = platform == StreamingPlatform.Twitch ? "90020006" : "UC_MAIN";
        var other = platform == StreamingPlatform.Twitch ? "77001122" : "UC_ARCH";
        var monitor = new CardMonitor(platform);
        monitor.Set(active, StreamStatus.Offline);
        monitor.Set(other, StreamStatus.Online, new() { Title = "Вечерний стрим", ViewerCount = 42 });
        await using var harness = CreateHarness(monitor);
        harness.Page.OpenPlatform(platform);
        harness.Page.Status.SetPageOnScreen(true);

        var view = new MultistreamPageView { DataContext = harness.Page };
        var host = new Border { Child = HostWithMainWindowFont(view) };
        host.Resources.MergedDictionaries.Add(new ResourceDictionary
        {
            Source = new($"pack://application:,,,/KeepShell;component/Resources/Themes/{theme}.xaml"),
        });
        host.SetResourceReference(Border.BackgroundProperty, "Bg.Base");
        var viewport = new Size(width, height);
        ArrangeWithResponsivePass(host, view, viewport);

        var catalog = Descendants<ChannelCatalogSettingsSectionView>(view).Single();
        var cards = Descendants<ListBox>(catalog).Single();
        var activeCard = harness.Editor.VisibleChannels.Single(c => c.ChannelId == active);
        var otherCard = harness.Editor.VisibleChannels.Single(c => c.ChannelId == other);
        var otherItem = (ListBoxItem)cards.ItemContainerGenerator.ContainerFromItem(otherCard);
        Assert.Multiple(() =>
        {
            Assert.That(activeCard.RuntimeStatus.BroadcastText, Is.EqualTo("Не в эфире"));
            Assert.That(otherCard.RuntimeStatus.BroadcastText, Is.EqualTo("В эфире · 42 зр."));
            Assert.That(otherCard.RuntimeStatus.BroadcastDetails, Is.EqualTo("Вечерний стрим"));
            Assert.That(otherCard.RuntimeStatus.ChatText, Is.EqualTo("Чат: не подключён"));
            Assert.That(Descendants<TextBlock>(otherItem).Any(t => t.Text == otherCard.RuntimeStatus.BroadcastText), Is.True);
        });
        TestResources.CaptureAccountLayout(host, $"channels-{theme}", width, height);

        cards.SelectedItem = otherCard;
        Assert.That(harness.Editor.SelectedChannel, Is.SameAs(otherCard));
        Assert.That(activeCard.IsActive, Is.True);
        Assert.That(harness.Page.HasUnsavedChanges, Is.False);

        monitor.Set(other, StreamStatus.Online, availability: ChannelMonitoringAvailability.Unavailable);
        var statusTimer = harness.Dispatcher.Timers.Single(timer => timer.Interval == TimeSpan.FromSeconds(1));
        statusTimer.Tick();
        ArrangeWithResponsivePass(host, view, viewport);

        Assert.Multiple(() =>
        {
            Assert.That(activeCard.RuntimeStatus.BroadcastText, Is.EqualTo("Не в эфире"));
            Assert.That(otherCard.RuntimeStatus.BroadcastText, Is.EqualTo("Статус эфира неизвестен"));
            Assert.That(otherCard.RuntimeStatus.BroadcastSeverity, Is.Not.EqualTo(StatusSeverity.Success));
            Assert.That(otherCard.RuntimeStatus.BroadcastDetails, Is.EqualTo("мониторинг недоступен"));
            Assert.That(Descendants<TextBlock>(otherItem).Any(t => t.Text == otherCard.RuntimeStatus.BroadcastText), Is.True);
            Assert.That(cards.SelectedItem, Is.SameAs(otherCard));
            Assert.That(harness.Page.HasUnsavedChanges, Is.False);
        });
        TestResources.CaptureAccountLayout(host, $"channels-{theme}-unavailable", width, height);

        monitor.Set(other, StreamStatus.Offline);
        statusTimer.Tick();
        Assert.That(otherCard.RuntimeStatus.BroadcastText, Is.EqualTo("Не в эфире"));
    }

    [TestCase("Light", 764, 540)]
    [TestCase("Dark", 1190, 820)]
    public async Task Multistream_accounts_show_compact_permission_summaries_until_expanded(string theme, int width, int height)
    {
        await using var harness = CreateHarness();
        harness.Page.SelectPlatformTabCommand.Execute(MultistreamPlatformTab.Accounts);
        var view = new MultistreamPageView { DataContext = harness.Page };
        var host = HostWithMainWindowFont(view);
        host.Resources.MergedDictionaries.Add(new ResourceDictionary
        {
            Source = new($"pack://application:,,,/KeepShell;component/Resources/Themes/{theme}.xaml"),
        });
        host.SetResourceReference(Control.BackgroundProperty, "Bg.Base");
        var viewport = new Size(width, height);
        ArrangeWithResponsivePass(host, view, viewport);
        var accounts = (ContentControl)view.FindName("PlatformAccountsHost")!;
        var grant = harness.Page.Accounts!.Accounts[0].Grants[0];
        var summary = Descendants<Button>(accounts).First(b => Equals(b.Content, grant.PermissionsLabel));
        Assert.That(IsEffectivelyVisible(summary), Is.True);
        Assert.That(grant.IsPermissionsExpanded, Is.False);
        TestResources.CaptureAccountLayout(host, $"accounts-{theme}-compact", width, height);

        grant.TogglePermissionsCommand.Execute(null);
        ArrangeWithResponsivePass(host, view, viewport);
        Assert.That(Descendants<TextBlock>(accounts).Any(t => IsEffectivelyVisible(t) && t.Text == grant.Permissions[0].Name), Is.True);
        TestResources.CaptureAccountLayout(host, $"accounts-{theme}-details", width, height);
    }

    [TestCase(1660, 980, Description = "wide")]
    [TestCase(840, 620, Description = "compact")]
    public async Task Multistream_switching_platforms_tabs_and_active_channel_does_not_jump_layout(double width, double height)
    {
        ApplyScale(1.0);
        await using var harness = CreateHarness();
        var view = new MultistreamPageView { DataContext = harness.Page };
        var host = HostWithMainWindowFont(view);
        Size viewport = new(width, height);
        ArrangeWithResponsivePass(host, view, viewport);

        var detailScroll = (ScrollViewer)view.FindName("DetailScroll")!;
        var channelEditor = (ContentControl)view.FindName("ChannelEditor")!;
        var platformSettingsHost = (ItemsControl)view.FindName("PlatformSettingsHost")!;
        var automationSection = (FrameworkElement)view.FindName("AutomationSection")!;
        var channelsTab = Descendants<RadioButton>(view).First(rb => System.Windows.Automation.AutomationProperties.GetAutomationId(rb) == "MultistreamTabChannels");
        var profilesTab = Descendants<RadioButton>(view).First(rb => System.Windows.Automation.AutomationProperties.GetAutomationId(rb) == "MultistreamAutoTabProfiles");

        var baseDetailWidth = channelEditor.ActualWidth;
        var baseTabsY = channelsTab.TransformToAncestor(view).Transform(default).Y;
        var baseTabsHeight = channelsTab.ActualHeight;

        var catalogView = Descendants<ChannelCatalogSettingsSectionView>(view).Single();
        var channelList = Descendants<ListBox>(catalogView).Single();
        var row0 = (ListBoxItem)channelList.ItemContainerGenerator.ContainerFromIndex(0);
        var row1 = (ListBoxItem)channelList.ItemContainerGenerator.ContainerFromIndex(1);
        var row0HeightBefore = row0.ActualHeight;
        var row1HeightBefore = row1.ActualHeight;
        Assert.That(row0HeightBefore, Is.EqualTo(row1HeightBefore).Within(0.5),
            $"Разная высота активной ({row1HeightBefore:F1}) и неактивной ({row0HeightBefore:F1}) строк канала при {width}x{height}");

        harness.Editor.VisibleChannels[0].MakeActiveCommand.Execute(null);
        ArrangeWithResponsivePass(host, view, viewport);
        Assert.Multiple(() =>
        {
            Assert.That(row0.ActualHeight, Is.EqualTo(row0HeightBefore).Within(0.5), "Высота строки 0 прыгнула после «Сделать активным»");
            Assert.That(row1.ActualHeight, Is.EqualTo(row1HeightBefore).Within(0.5), "Высота строки 1 прыгнула после «Сделать активным»");
            Assert.That(channelEditor.ActualWidth, Is.EqualTo(baseDetailWidth).Within(0.5), "Ширина рабочей области прыгнула при появлении нижней панели сохранения");
        });

        harness.Page.DiscardUnsavedChangesCommand.Execute(null);
        ArrangeWithResponsivePass(host, view, viewport);

        harness.Page.SelectPlatformCommand.Execute(harness.Page.Cards[1]);
        ArrangeWithResponsivePass(host, view, viewport);
        var ytTabsY = channelsTab.TransformToAncestor(view).Transform(default).Y;
        var ytDetailWidth = channelEditor.ActualWidth;

        harness.Page.SelectPlatformTabCommand.Execute(MultistreamPlatformTab.Accounts);
        ArrangeWithResponsivePass(host, view, viewport);
        var accountsHost = (ContentControl)view.FindName("PlatformAccountsHost")!;
        var ytAccountsWidth = accountsHost.ActualWidth;
        var ytAccountsTabsY = channelsTab.TransformToAncestor(view).Transform(default).Y;

        harness.Page.Accounts!.ToggleTechnicalModeCommand.Execute(null);
        ArrangeWithResponsivePass(host, view, viewport);
        var ytAccountsTechWidth = accountsHost.ActualWidth;
        harness.Page.Accounts.ToggleTechnicalModeCommand.Execute(null);

        harness.Page.SelectPlatformTabCommand.Execute(MultistreamPlatformTab.Parameters);
        ArrangeWithResponsivePass(host, view, viewport);
        var ytParamsWidth = platformSettingsHost.ActualWidth;
        var ytParamsTabsY = channelsTab.TransformToAncestor(view).Transform(default).Y;

        harness.Page.OpenAutomationCommand.Execute(null);
        ArrangeWithResponsivePass(host, view, viewport);
        var autoWidth = automationSection.ActualWidth;
        var autoTabsY = profilesTab.TransformToAncestor(view).Transform(default).Y;

        Assert.Multiple(() =>
        {
            Assert.That(ytTabsY, Is.EqualTo(baseTabsY).Within(0.5), $"Вкладки прыгнули по Y при смене площадки ({width}x{height}): Twitch={baseTabsY:F1}, YouTube={ytTabsY:F1}");
            Assert.That(ytAccountsTabsY, Is.EqualTo(baseTabsY).Within(0.5), $"Вкладки прыгнули по Y на «Аккаунты» ({width}x{height}): {ytAccountsTabsY:F1} vs {baseTabsY:F1}");
            Assert.That(ytParamsTabsY, Is.EqualTo(baseTabsY).Within(0.5), $"Вкладки прыгнули по Y на «Параметры и доступ» ({width}x{height}): {ytParamsTabsY:F1} vs {baseTabsY:F1}");
            Assert.That(autoTabsY, Is.GreaterThanOrEqualTo(0), $"Вкладки в «Профили и автоматика» вышли за верх экрана ({width}x{height}): {autoTabsY:F1}");
            Assert.That(profilesTab.ActualHeight, Is.EqualTo(baseTabsHeight).Within(0.5), $"Высота вкладок отличается в «Профили и автоматика» ({width}x{height}): {profilesTab.ActualHeight:F1} vs {baseTabsHeight:F1}");
            Assert.That(ytDetailWidth, Is.EqualTo(baseDetailWidth).Within(0.5), $"Ширина прыгнула при смене площадки ({width}x{height}): {ytDetailWidth:F1} vs {baseDetailWidth:F1}");
            Assert.That(ytAccountsWidth, Is.EqualTo(baseDetailWidth).Within(0.5), $"Ширина прыгнула на вкладке аккаунтов ({width}x{height}): {ytAccountsWidth:F1} vs {baseDetailWidth:F1}");
            Assert.That(ytAccountsTechWidth, Is.EqualTo(baseDetailWidth).Within(0.5), $"Ширина прыгнула в техническом виде аккаунтов ({width}x{height}): {ytAccountsTechWidth:F1} vs {baseDetailWidth:F1}");
            Assert.That(ytParamsWidth, Is.EqualTo(baseDetailWidth).Within(0.5), $"Ширина прыгнула на длинной вкладке параметров ({width}x{height}): {ytParamsWidth:F1} vs {baseDetailWidth:F1}");
            Assert.That(autoWidth, Is.EqualTo(baseDetailWidth).Within(0.5), $"Ширина прыгнула в «Профили и автоматика» ({width}x{height}): {autoWidth:F1} vs {baseDetailWidth:F1}");
            Assert.That(detailScroll.VerticalOffset, Is.EqualTo(0).Within(0.5), "Открытие «Профили и автоматика» не должно прокручивать заголовок за верх экрана");
        });
    }

    [Test]
    public async Task Multistream_reveal_section_for_channels_parameters_and_automation_keeps_header_visible()
    {
        ApplyScale(1.0);
        await using var harness = CreateHarness();
        var view = new MultistreamPageView { DataContext = harness.Page };
        var host = HostWithMainWindowFont(view);
        Size viewport = new(1180, 620);
        ArrangeWithResponsivePass(host, view, viewport);

        var detailScroll = (ScrollViewer)view.FindName("DetailScroll")!;

        harness.Page.OpenPlatform(StreamingPlatform.Twitch);
        ArrangeWithResponsivePass(host, view, viewport);
        view.ApplySectionRevealForTest(harness.Page);
        var channelsOffset = detailScroll.VerticalOffset;

        harness.Page.OpenSelectedPlatformParametersCommand.Execute(null);
        ArrangeWithResponsivePass(host, view, viewport);
        view.ApplySectionRevealForTest(harness.Page);
        var paramsOffset = detailScroll.VerticalOffset;

        harness.Page.OpenAutomationCommand.Execute(null);
        ArrangeWithResponsivePass(host, view, viewport);
        view.ApplySectionRevealForTest(harness.Page);
        var autoOffset = detailScroll.VerticalOffset;

        Assert.Multiple(() =>
        {
            Assert.That(channelsOffset, Is.EqualTo(0).Within(0.5), $"OpenPlatform(Twitch) прокрутил шапку за верх: VerticalOffset={channelsOffset:F1}");
            Assert.That(paramsOffset, Is.EqualTo(0).Within(0.5), $"OpenSelectedPlatformParameters прокрутил шапку за верх: VerticalOffset={paramsOffset:F1}");
            Assert.That(autoOffset, Is.EqualTo(0).Within(0.5), $"OpenAutomation прокрутил шапку за верх: VerticalOffset={autoOffset:F1}");
        });
    }

    [TestCase(1660, 980, 1.0, Description = "wide 1.0")]
    [TestCase(1660, 980, 1.6, Description = "wide 1.6")]
    public async Task Multistream_long_channel_name_and_connecting_label_do_not_clip_platform_card(double width, double height, double scale)
    {
        ApplyScale(scale);
        await using var harness = CreateHarness();
        harness.Page.Cards[1].Status.ChannelName = "@VeryLongYouTubeChannelHandleForTestingClipping";
        harness.Page.Cards[1].Status.ConnectionActionLabel = "Отменить подключение";

        var view = new MultistreamPageView { DataContext = harness.Page };
        var host = HostWithMainWindowFont(view);
        ArrangeWithResponsivePass(host, view, new(width, height));

        var platformCards = (ListBox)view.FindName("PlatformCards")!;
        var container = (ListBoxItem)platformCards.ItemContainerGenerator.ContainerFromItem(harness.Page.Cards[1]);
        var cardBorder = Descendants<Border>(container).First(b => b.Name == "CardBorder");
        var cardInnerRight = cardBorder.ActualWidth - cardBorder.Padding.Right - cardBorder.BorderThickness.Right;

        var titleText = Descendants<TextBlock>(cardBorder).First(t => t.Text == "YouTube");
        var titleBounds = titleText.TransformToAncestor(cardBorder).TransformBounds(new(titleText.RenderSize));
        var naturalTitle = MeasureUnconstrainedWidth(titleText);
        var countText = Descendants<TextBlock>(cardBorder).First(t => t.Text == harness.Page.Cards[1].Status.ChannelCountText);
        var countBounds = countText.TransformToAncestor(cardBorder).TransformBounds(new(countText.RenderSize));
        var connectBtn = Descendants<Button>(cardBorder).Single(IsEffectivelyVisible);
        var btnBounds = connectBtn.TransformToAncestor(cardBorder).TransformBounds(new(connectBtn.RenderSize));

        Assert.Multiple(() =>
        {
            Assert.That(titleText.ActualWidth, Is.GreaterThanOrEqualTo(naturalTitle - 1.0),
                $"Название «YouTube» сжато при «Отменить подключение» (@ {scale}): Actual={titleText.ActualWidth:F1}, Natural={naturalTitle:F1}");
            Assert.That(titleBounds.Right, Is.LessThanOrEqualTo(btnBounds.Left + 0.5),
                $"Название «YouTube» налезает на «Отменить подключение» (@ {scale}): TitleRight={titleBounds.Right:F1}, BtnLeft={btnBounds.Left:F1}");
            Assert.That(btnBounds.Right, Is.LessThanOrEqualTo(cardInnerRight + 0.5),
                $"Кнопка «Отменить подключение» обрезана (@ {scale}): Right={btnBounds.Right:F1}, InnerRight={cardInnerRight:F1}");
            Assert.That(countBounds.Right, Is.LessThanOrEqualTo(cardInnerRight + 0.5),
                $"Бейдж «2 канала» вылез за карточку при длинном имени канала (@ {scale}): Right={countBounds.Right:F1}, InnerRight={cardInnerRight:F1}");
        });
    }

    private LayoutHarness CreateHarness(params IPlatformChannelMonitor[] monitors)
    {
        var twitch = new StubPlatform(StreamingPlatform.Twitch, "Twitch");
        var youtube = new StubPlatform(StreamingPlatform.YouTube, "YouTube");
        var registry = new ChatPlatformRegistry([twitch, youtube]);
        var gate = new SettingsWriteGate();
        var bus = new InMemoryEventBus(NullLogger<InMemoryEventBus>.Instance);
        var catalog = new ChannelCatalogStore(filePath: Path.Combine(_directory, "channels.json"), gate: gate);
        var accounts = new AccountsStore(filePath: Path.Combine(_directory, "accounts.json"), gate: gate);
        var ytSettingsStore = new YouTubeSettingsStore(filePath: Path.Combine(_directory, "youtube.json"), gate: gate);
        var ytAccountsStore = new YouTubeAccountsStore(filePath: Path.Combine(_directory, "youtube-accounts.json"), gate: gate);
        var manager = new SettingsManager(NullLogger<SettingsManager>.Instance, Path.Combine(_directory, "settings.json"), gate);
        var server = new KestrelHttpServer(null!, manager, [], NullLogger<KestrelHttpServer>.Instance, NullLoggerFactory.Instance);
        var dispatcher = new ManualUiDispatcher();
        var dialogs = DispatchProxy.Create<IDialogService, InertProxy>();

        accounts.SaveAll(
            new TwitchAccountSettings { Login = "alex_ci_cd", UserId = "1001", AccessToken = "tok", RefreshToken = "ref", AccessTokenExpiresAt = DateTimeOffset.UtcNow.AddDays(30) },
            new TwitchAccountSettings { Login = "thevsakeeper", UserId = "2002", AccessToken = "tok", RefreshToken = "ref", AccessTokenExpiresAt = DateTimeOffset.UtcNow.AddDays(30) });
        ytSettingsStore.Save(new() { Enabled = true, ClientId = "yt-client" });
        ytAccountsStore.SaveAll(
            "yt-secret",
            new YouTubeAccountSettings { ChannelId = "UC_BOT", ChannelTitle = "alex_yt", AccessToken = "tok", RefreshToken = "ref", AccessTokenExpiresAt = DateTimeOffset.UtcNow.AddDays(30) },
            new YouTubeAccountSettings { ChannelId = "UC_MAIN", ChannelTitle = "VissGames", AccessToken = "tok", RefreshToken = "ref", AccessTokenExpiresAt = DateTimeOffset.UtcNow.AddDays(30) });
        catalog.Save(new()
        {
            Channels =
            [
                new() { Platform = StreamingPlatform.Twitch, ChannelId = "77001122", DisplayName = "thevsakeeper", IsConfigured = true, MonitoringEnabled = false, BotAccountId = "1001", BotContextId = "default", BotVerifiedSubjectId = "1001", BroadcasterAccountId = "2002", BroadcasterContextId = "default", BroadcasterVerifiedSubjectId = "2002" },
                new() { Platform = StreamingPlatform.Twitch, ChannelId = "90020006", DisplayName = "tsm_viss", IsConfigured = true, MonitoringEnabled = true, BotAccountId = "1001", BotContextId = "default", BotVerifiedSubjectId = "1001" },
                new() { Platform = StreamingPlatform.YouTube, ChannelId = "UC_MAIN", DisplayName = "@VissGames", IsConfigured = true, MonitoringEnabled = true, BotAccountId = "UC_BOT", BotContextId = "default", BotVerifiedSubjectId = "UC_BOT", BroadcasterAccountId = "UC_MAIN", BroadcasterContextId = "default", BroadcasterVerifiedSubjectId = "UC_MAIN" },
                new() { Platform = StreamingPlatform.YouTube, ChannelId = "UC_ARCH", DisplayName = "@VissGamesArchive", IsConfigured = true, MonitoringEnabled = true, BotAccountId = "UC_BOT", BotContextId = "default", BotVerifiedSubjectId = "UC_BOT" },
            ],
            Platforms =
            [
                new() { Platform = StreamingPlatform.Twitch, ActiveChannelId = "90020006", SwitchMode = ChannelSwitchMode.Manual },
                new() { Platform = StreamingPlatform.YouTube, ActiveChannelId = "UC_MAIN", SwitchMode = ChannelSwitchMode.PreselectedNext, PreselectedNextChannelId = "UC_ARCH" },
            ],
        });

        var oauth = new OAuthSettingsViewModel(
            DispatchProxy.Create<ITwitchOAuthService, InertProxy>(),
            accounts, dialogs, DispatchProxy.Create<IShellLauncher, InertProxy>(),
            DispatchProxy.Create<IEmbeddedTwitchAuthDialog, InertProxy>(),
            dispatcher, TimeProvider.System, NullLoggerFactory.Instance);
        var ytVm = new YouTubeSettingsSectionViewModel(
            DispatchProxy.Create<IYouTubeOAuthService, InertProxy>(),
            DispatchProxy.Create<IShellLauncher, InertProxy>(),
            dialogs, dispatcher, TimeProvider.System, NullLoggerFactory.Instance);
        var editor = new ChannelCatalogSettingsSectionViewModel(
            catalog,
            [new FakeAdapter(StreamingPlatform.Twitch, () => accounts.ListAccounts()), new FakeAdapter(StreamingPlatform.YouTube, () => ytAccountsStore.ListAccounts())],
            accounts, ytAccountsStore,
            shellLauncher: DispatchProxy.Create<IShellLauncher, InertProxy>(),
            twitchOAuth: DispatchProxy.Create<ITwitchOAuthService, InertProxy>(),
            youtubeOAuth: DispatchProxy.Create<IYouTubeOAuthService, InertProxy>(),
            settingsManager: manager,
            youtubeSettingsStore: ytSettingsStore,
            platformRegistry: registry);
        var groups = new ChannelGroupsSettingsSectionViewModel(catalog, new UserStatisticsRepository(NullLogger<UserStatisticsRepository>.Instance));
        var profilesSection = new BroadcastProfilesSettingsSectionViewModel(null!);

        var settings = new SettingsPageViewModel(
            [
                new BasicSettingsSectionContributor(new(), new(), new(), manager, new StubTargetChannelProvider()),
                new TwitchConnectionSettingsContributor(oauth, accounts, manager, NullLogger<TwitchConnectionSettingsContributor>.Instance),
                new YouTubeConnectionSettingsContributor(ytVm, ytSettingsStore, ytAccountsStore),
                new StreamSettingsSectionContributor(
                    new(),
                    new(registry),
                    new(new(filePath: Path.Combine(_directory, "polls.json")), TimeProvider.System),
                    new(manager, filePath: Path.Combine(_directory, "platform-lifecycle.json"), gate: gate)),
                new ChannelsSettingsSectionContributor(editor, groups, catalog),
            ],
            manager, gate, server, bus, NullLogger<SettingsPageViewModel>.Instance, dialogs, new MemorySettings(),
            broadcastProfiles: profilesSection, platformRegistry: registry);

        var connection = new BotConnectionManager(new AppHost([], NullLogger<AppHost>.Instance), [], null, bus, NullLogger<BotConnectionManager>.Instance);
        var status = new MultistreamTileViewModel(registry, catalog, connection, new PlatformAuthRegistry([]), monitors, bus, dispatcher, ytSettingsStore);
        var ytControls = new YouTubeMultistreamPlatformControls(settings);
        var platformAccounts = new PlatformAccountsSectionViewModel(
            catalog,
            accounts,
            ytAccountsStore,
            twitchOAuth: DispatchProxy.Create<ITwitchOAuthService, InertProxy>(),
            youtubeOAuth: DispatchProxy.Create<IYouTubeOAuthService, InertProxy>(),
            settingsManager: manager,
            youtubeSettingsStore: ytSettingsStore,
            shellLauncher: DispatchProxy.Create<IShellLauncher, InertProxy>(),
            dialogService: dialogs,
            eventBus: bus,
            platformRegistry: registry);
        var page = new MultistreamPageViewModel(settings, status, [ytControls], platformAccounts);
        page.OnEnter();

        return new(page, editor, settings, status, connection, dispatcher);
    }

    private static void ArrangeWithResponsivePass(FrameworkElement host, MultistreamPageView view, Size size)
    {
        for (var pass = 0; pass < 3; pass++)
        {
            host.Measure(size);
            host.Arrange(new(size));
            host.UpdateLayout();
            view.UpdateResponsiveLayout();
            foreach (var catalog in Descendants<ChannelCatalogSettingsSectionView>(view))
            {
                catalog.IsNarrowLayout = ChannelCatalogSettingsSectionView.ResolveNarrowLayout(catalog.IsNarrowLayout, catalog.ActualWidth, FontScaleManager.Current);
                catalog.IsTwoColumnCards = ChannelCatalogSettingsSectionView.ResolveTwoColumnCards(catalog.IsTwoColumnCards, catalog.ActualWidth, FontScaleManager.Current);
            }
        }
        host.Measure(size);
        host.Arrange(new(size));
        host.UpdateLayout();
    }

    private static double MeasureUnconstrainedWidth(TextBlock text)
    {
        var probe = new TextBlock
        {
            Text = text.Text,
            FontFamily = text.FontFamily,
            FontSize = text.FontSize,
            FontWeight = text.FontWeight,
            FontStyle = text.FontStyle,
        };
        probe.Measure(new(double.PositiveInfinity, double.PositiveInfinity));
        return probe.DesiredSize.Width;
    }

    private static void ApplyScale(double scale)
    {
        if (Application.Current is null)
        {
            return;
        }

        FontScaleManager.Apply(scale);
        var tokens = new ResourceDictionary { Source = new(Dictionaries[1]) };
        var resources = Application.Current.Resources;
        foreach (var key in tokens.Keys.OfType<string>().Where(static k => k.StartsWith("Font.Size.", StringComparison.Ordinal)))
        {
            resources[key] = Math.Abs(scale - FontScaleManager.DefaultScale) < 0.001
                ? (double)tokens[key]
                : (double)tokens[key] * scale;
        }
        AppScaledSizes.Register(Application.Current);
    }

    private static ContentControl HostWithMainWindowFont(FrameworkElement content)
    {
        var host = new ContentControl { Content = content };
        if (Application.Current?.TryFindResource("Font.Sans") is FontFamily fontFamily)
        {
            host.FontFamily = fontFamily;
        }
        if (Application.Current?.TryFindResource("Font.Size.M") is double fontSize)
        {
            host.FontSize = fontSize;
        }
        return host;
    }

    private static bool IsEffectivelyVisible(UIElement element)
    {
        for (DependencyObject? current = element; current is not null; current = VisualTreeHelper.GetParent(current))
        {
            if (current is UIElement ui && ui.Visibility != Visibility.Visible)
            {
                return false;
            }
        }

        return true;
    }

    private static bool VerticalRangesOverlap(Rect a, Rect b) =>
        a.Top < b.Bottom - 0.5 && b.Top < a.Bottom - 0.5;

    private static IEnumerable<T> Descendants<T>(DependencyObject root)
        where T : DependencyObject
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is T match)
            {
                yield return match;
            }
            foreach (var nested in Descendants<T>(child))
            {
                yield return nested;
            }
        }
    }

    private sealed class LayoutHarness(
        MultistreamPageViewModel page,
        ChannelCatalogSettingsSectionViewModel editor,
        SettingsPageViewModel settings,
        MultistreamTileViewModel status,
        BotConnectionManager connection,
        ManualUiDispatcher dispatcher) : IAsyncDisposable
    {
        public MultistreamPageViewModel Page { get; } = page;
        public ChannelCatalogSettingsSectionViewModel Editor { get; } = editor;
        public ManualUiDispatcher Dispatcher { get; } = dispatcher;

        public async ValueTask DisposeAsync()
        {
            status.Dispose();
            settings.Dispose();
            await connection.DisposeAsync();
        }
    }

    private sealed class CardMonitor(StreamingPlatform platform) : IPlatformChannelMonitor
    {
        private readonly Dictionary<ChannelKey, ChannelMonitoringSnapshot> _snapshots = [];

        public StreamingPlatform Platform => platform;
        public string Name => "Мониторинг";
        public int StartOrder => 0;
        public Task StartAsync(IProgress<string> progress, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task StopAsync(IProgress<string> progress, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task RefreshAsync(ChannelKey? channel = null, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public ChannelMonitoringSnapshot GetSnapshot(ChannelKey channel) => _snapshots[channel];
        public IReadOnlyList<ChannelMonitoringSnapshot> GetSnapshots() => [.. _snapshots.Values];

        public void Set(string id, StreamStatus status, StreamInfo? stream = null,
            ChannelMonitoringAvailability availability = ChannelMonitoringAvailability.Active)
        {
            var key = new ChannelKey(platform, id);
            var reason = availability == ChannelMonitoringAvailability.Unavailable
                ? MonitoringUnavailableReason.TransportDisconnected : MonitoringUnavailableReason.None;
            _snapshots[key] = new(key, id, availability, reason, null, status, stream, null, false);
        }
    }

    public class InertProxy : DispatchProxy
    {
        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args) =>
            targetMethod is not null && targetMethod.ReturnType != typeof(void) && targetMethod.ReturnType.IsValueType
                ? Activator.CreateInstance(targetMethod.ReturnType)
                : null;
    }

    private sealed class StubPlatform(StreamingPlatform id, string displayName) : IChatPlatform
    {
        public StreamingPlatform Id => id;
        public string DisplayName => displayName;
        public PlatformCapabilities Capabilities => PlatformCapabilities.MultiChannelMonitoring | PlatformCapabilities.BackgroundStreamMonitoring;
        public bool IsEnabled => true;
        public bool IsConnected => false;
        public IStreamStatus StreamStatus { get; } = new StubStreamStatus();
        public IPlatformMessenger Messenger => throw new NotSupportedException();
    }

    private sealed class StubStreamStatus : IStreamStatus
    {
        public StreamStatus CurrentStatus => StreamStatus.Offline;
        public StreamInfo? CurrentStream => null;
        public StreamStatusSnapshot GetSnapshot() => new(CurrentStatus, null, null);
        public Task RefreshLiveSnapshotAsync() => Task.CompletedTask;
    }

    private sealed class FakeAdapter(StreamingPlatform platform, Func<IReadOnlyList<PlatformAccount>> listAccounts) : IPlatformChannelAdapter
    {
        public StreamingPlatform Platform => platform;
        public PlatformCapabilities Capabilities => PlatformCapabilities.MultiChannelMonitoring | PlatformCapabilities.BackgroundStreamMonitoring;
        public IReadOnlyList<PlatformAccount> ListAccounts() => listAccounts();
        public Task<ResolvedChannelIdentity?> ResolveChannelIdentityAsync(string rawInput, string? preferredContextId, CancellationToken cancellationToken) => Task.FromResult<ResolvedChannelIdentity?>(null);
        public Task<ChannelAccessVerification> VerifyChannelAccessAsync(ChannelKey channel, PlatformAuthRole role, string contextId, CancellationToken cancellationToken) =>
            Task.FromResult(new ChannelAccessVerification(true, "id"));
        public Task<ChannelLiveProbeResult> ProbeChannelLiveAsync(ChannelCatalogEntry channel, CancellationToken cancellationToken) =>
            Task.FromResult(new ChannelLiveProbeResult(false, false, null));
    }

    private sealed class StubTargetChannelProvider : ITargetChannelProvider
    {
        public TargetChannelState Current { get; } = new("tsm_viss", "tsm_viss", false, true);
        public void BeginSession() { }
        public void EndSession() { }
    }
}
