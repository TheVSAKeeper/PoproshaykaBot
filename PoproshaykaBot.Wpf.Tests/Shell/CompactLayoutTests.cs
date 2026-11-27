using KeepShell.Bootstrap;
using KeepShell.Services;
using KeepShell.Testing;
using Microsoft.Extensions.Logging.Abstractions;
using PoproshaykaBot.Core.Broadcast.Profiles;
using PoproshaykaBot.Core.Chat;
using PoproshaykaBot.Core.Chat.Commands;
using PoproshaykaBot.Core.Dashboard;
using PoproshaykaBot.Core.Infrastructure;
using PoproshaykaBot.Core.Infrastructure.Events;
using PoproshaykaBot.Core.Infrastructure.Hosting;
using PoproshaykaBot.Core.Platforms;
using PoproshaykaBot.Core.Server;
using PoproshaykaBot.Core.Settings;
using PoproshaykaBot.Core.Settings.Stores;
using PoproshaykaBot.Core.Settings.Ui;
using PoproshaykaBot.Core.Statistics;
using PoproshaykaBot.Core.Update;
using PoproshaykaBot.Core.Users;
using PoproshaykaBot.Wpf.Bootstrap;
using PoproshaykaBot.Wpf.Infrastructure;
using PoproshaykaBot.Wpf.Tests.Support;
using PoproshaykaBot.Wpf.ViewModels;
using PoproshaykaBot.Wpf.ViewModels.Controls;
using PoproshaykaBot.Wpf.ViewModels.Dialogs;
using PoproshaykaBot.Wpf.ViewModels.Settings;
using PoproshaykaBot.Wpf.Views;
using PoproshaykaBot.Wpf.Views.Dialogs;
using PoproshaykaBot.Wpf.Views.Settings;
using System.IO;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;

namespace PoproshaykaBot.Wpf.Tests.Shell;

[TestFixture]
[Apartment(ApartmentState.STA)]
[NonParallelizable]
public class CompactLayoutTests
{
    private static readonly string[] Dictionaries =
    [
        "pack://application:,,,/KeepShell;component/Resources/Themes/Light.xaml",
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
    }

    [SetUp]
    public void SetUp()
    {
        _directory = Path.Combine(Path.GetTempPath(), "poproshayka-layout-" + Guid.NewGuid().ToString("N"));
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

    [TestCase(1.0)]
    [TestCase(1.6)]
    public void Диалоги_при_любом_масштабе_не_режут_подписи_и_кнопки(double scale)
    {
        ApplyScale(scale);

        using var game = new GameAutocompleteViewModel(new StubGameResolver(), new ManualUiDispatcher());
        using var profileVm = new BroadcastProfileEditDialogViewModel(
            game,
            null!,
            null!,
            NullLogger<BroadcastProfileEditDialogViewModel>.Instance,
            new ChatPlatformRegistry([]));
        var profileView = new BroadcastProfileEditDialogView { DataContext = profileVm };
        Arrange(profileView, new(600, 620));

        var profileLabels = Descendants<TextBlock>(profileView)
            .Where(static t => t.Text is "Заголовок:" or "Серия №:" or "Игра:" or "Теги:" or "Язык:" or "Площадки:")
            .ToArray();

        Assert.That(profileLabels, Has.Length.EqualTo(6));
        foreach (var label in profileLabels)
        {
            var requiredWidth = label.DesiredSize.Width - label.Margin.Left - label.Margin.Right;
            Assert.That(label.ActualWidth, Is.GreaterThanOrEqualTo(requiredWidth - 0.5),
                $"Подпись «{label.Text}» обрезана при scale={scale}");
        }

        var termVm = new PointTermDialogViewModel();
        var termView = new PointTermDialogView { DataContext = termVm };
        Arrange(termView, new(480, 420));

        var termLabels = Descendants<TextBlock>(termView)
            .Where(static t => t.Text is "Единственное:" or "От 2 до 4:" or "Множественное:")
            .ToArray();

        Assert.That(termLabels, Has.Length.EqualTo(3));
        foreach (var label in termLabels)
        {
            var requiredWidth = label.DesiredSize.Width - label.Margin.Left - label.Margin.Right;
            Assert.That(label.ActualWidth, Is.GreaterThanOrEqualTo(requiredWidth - 0.5),
                $"Подпись «{label.Text}» обрезана при scale={scale}");
        }
    }

    [Test]
    public void Модальные_диалоги_не_прыгают_по_ширине_и_ограничивают_высоту_при_изменении_содержимого()
    {
        static Size MeasureConverged(FrameworkElement view, Size ceiling)
        {
            for (var pass = 0; pass < 3; pass++)
            {
                view.InvalidateMeasure();
                view.Measure(ceiling);
                view.Arrange(new(view.DesiredSize));
                view.UpdateLayout();
            }

            return view.DesiredSize;
        }

        var modalCeiling = new Size(1100, 760);
        var compactCeiling = new Size(976, 572);

        // PointTermDialogView: длинные формы не должны растягивать ширину
        var termVm = new PointTermDialogViewModel();
        var termView = new PointTermDialogView { DataContext = termVm };
        var termBefore = MeasureConverged(termView, modalCeiling);
        termVm.Singular = "очень длинное название валюты канала";
        termVm.Few = "очень длинных названия валюты канала";
        termVm.Many = "очень длинных названий валюты канала";
        var termAfter = MeasureConverged(termView, modalCeiling);

        // PollProfileEditDialogView: включение опций и текст ошибки не прыгают по ширине и укладываются в компактный потолок
        var pollVm = new PollProfileEditDialogViewModel(null!);
        var pollView = new PollProfileEditDialogView { DataContext = pollVm };
        var pollScroll = ((Grid)pollView.Content).Children.OfType<ScrollViewer>().First();
        var pollBefore = MeasureConverged(pollView, modalCeiling);
        var pollExtentBefore = pollScroll.ExtentHeight;

        pollVm.ChannelPointsEnabled = true;
        pollVm.SelectedTriggerItem = pollVm.TriggerItems[2];
        pollVm.ErrorMessage = PollProfileEditDialogViewModel.NotWrittenNotice;
        pollVm.HasError = true;
        var pollAfter = MeasureConverged(pollView, modalCeiling);
        var pollExtentAfter = pollScroll.ExtentHeight;
        var pollCompact = MeasureConverged(pollView, compactCeiling);

        // ChatBlockersDialogView: длинная строка CSS-селектора не должна растягивать диалог
        var blockersVm = new ChatBlockersDialogViewModel(string.Empty);
        var blockersView = new ChatBlockersDialogView { DataContext = blockersVm };
        var blockersBefore = MeasureConverged(blockersView, modalCeiling);
        blockersVm.Selectors = "div.very-long-selector-class-name > section.banner-container > article.promo-card[data-test='long-banner-value']";
        var blockersAfter = MeasureConverged(blockersView, modalCeiling);

        // ColorPickerDialogView: длинный ввод в HexText не должен менять ширину
        var colorVm = new ColorPickerDialogViewModel(System.Drawing.Color.CornflowerBlue, "Цвет текста");
        var colorView = new ColorPickerDialogView { DataContext = colorVm };
        var colorBefore = MeasureConverged(colorView, modalCeiling);
        colorVm.HexText = "#FF6495ED-extra-long-input-text-value";
        var colorAfter = MeasureConverged(colorView, modalCeiling);

        Assert.Multiple(() =>
        {
            Assert.That(termAfter.Width, Is.EqualTo(termBefore.Width).Within(0.5), "PointTermDialogView прыгнул по ширине");
            Assert.That(pollExtentAfter - pollExtentBefore, Is.GreaterThan(80), "Скрытые поля PollProfileEditDialogView должны участвовать в расчёте ScrollViewer");
            Assert.That(pollAfter.Width, Is.EqualTo(pollBefore.Width).Within(0.5), "PollProfileEditDialogView прыгнул по ширине");
            Assert.That(pollAfter.Height, Is.LessThan(modalCeiling.Height - 20), "PollProfileEditDialogView не должен раздуваться до потолка ModalHost");
            Assert.That(pollCompact.Height, Is.LessThanOrEqualTo(compactCeiling.Height + 0.5), "PollProfileEditDialogView вышел за высоту 1024x640");
            Assert.That(blockersAfter.Width, Is.EqualTo(blockersBefore.Width).Within(0.5), "ChatBlockersDialogView прыгнул по ширине");
            Assert.That(colorAfter.Width, Is.EqualTo(colorBefore.Width).Within(0.5), "ColorPickerDialogView прыгнул по ширине");
        });
    }

    [Test]
    public void Баннер_онбординга_переносит_текст_и_не_подрезает_кнопку()
    {
        ApplyScale(1.6);

        var data = new
        {
            IsVisible = true,
            Text = "Для работы бота подключите канал Twitch, настройте веб-сервер и авторизуйте аккаунты.",
            ConfigureCommand = (System.Windows.Input.ICommand?)null,
        };
        var banner = new OnboardingBannerView { DataContext = data };
        Arrange(banner, new(720, 220));

        var text = Descendants<TextBlock>(banner).First(t => t.Text == data.Text);
        var singleLine = new TextBlock { Text = "А", FontSize = text.FontSize, FontFamily = text.FontFamily };
        singleLine.Measure(new(double.PositiveInfinity, double.PositiveInfinity));
        var button = Descendants<Button>(banner).First(b => Equals(b.Content, "Настроить"));

        Assert.Multiple(() =>
        {
            Assert.That(text.ActualHeight, Is.GreaterThan(singleLine.DesiredSize.Height * 1.5));
            Assert.That(button.ActualWidth, Is.GreaterThanOrEqualTo(button.DesiredSize.Width - button.Margin.Left - button.Margin.Right - 0.5));
        });
    }

    [Test]
    public void Пользователи_при_1024x640_оставляют_не_менее_четырёх_строк_и_не_режут_инспектор()
    {
        ApplyScale(1.0);
        var pageVm = CreateUsersPage(20);
        Assert.That(pageVm.TrySelectAt(0), Is.True);
        var view = new UserStatisticsPageView { DataContext = pageVm };
        Arrange(view, new(920, 430));

        var inspectorScroll = (ScrollViewer)view.FindName("OuterInspectorScroll")!;
        var dailyLabel = Descendants<TextBlock>(inspectorScroll).First(static t => t.Text == "сообщений в день");
        var bottomEdge = dailyLabel.TranslatePoint(new(0, dailyLabel.ActualHeight), inspectorScroll).Y;

        Assert.That(view.IsInspectorScrollCombined, Is.True);
        Assert.That(bottomEdge, Is.LessThanOrEqualTo(inspectorScroll.ViewportHeight + 0.5),
            "При 1024x640 @ 1.0 подпись «сообщений в день» не должна обрезаться краем скролла инспектора");

        ApplyScale(1.6);
        Arrange(view, new(920, 350));

        var ratingList = (ListBox)view.FindName("RatingList")!;
        Assert.Multiple(() =>
        {
            Assert.That(pageVm.ShowExpandedSummary, Is.False);
            Assert.That(ratingList.ActualHeight, Is.GreaterThanOrEqualTo(150),
                "При 1024x640 @ 1.6 с баннером списку пользователей должно оставаться минимум ~4 строки");
        });
    }

    [Test]
    public void История_стримов_переходит_в_две_колонки_при_1216_и_держит_строки_при_1024x640_1_6()
    {
        var pageVm = CreateStreamsPage(20);
        var view = new StreamHistoryPageView { DataContext = pageVm };

        ApplyScale(1.0);
        Arrange(view, new(1216, 680));

        var pageGrid = (Grid)view.FindName("PageGrid")!;
        Assert.That(pageGrid.ColumnDefinitions.Count, Is.EqualTo(3),
            "При 1280x800 со свёрнутым рейлом (1216 DIP) страница должна идти в две колонки");

        var baseFontM = (double)Application.Current.Resources["Font.Size.M"];
        ApplyScale(1.6);
        var scaledFontM = (double)Application.Current.Resources["Font.Size.M"];
        Assert.That(scaledFontM, Is.EqualTo(baseFontM * 1.6).Within(0.01), "Масштаб шрифта 1.6 должен масштабировать Font.Size.* ресурсы");

        Arrange(view, new(920, 400));

        var sessionsList = (ListBox)view.FindName("SessionsList")!;
        var hiddenToggle = Descendants<System.Windows.Controls.Primitives.ToggleButton>(view)
            .First(b => b.Visibility == Visibility.Visible && Descendants<TextBlock>(b).Any(t => t.Text.StartsWith("Убранные:", StringComparison.Ordinal)));

        Assert.Multiple(() =>
        {
            Assert.That(pageVm.IsSummaryOpen, Is.False);
            Assert.That(pageVm.HasTrend, Is.False);
            Assert.That(sessionsList.ActualHeight, Is.GreaterThanOrEqualTo(120));
            Assert.That(hiddenToggle.ActualWidth, Is.GreaterThanOrEqualTo(hiddenToggle.DesiredSize.Width - hiddenToggle.Margin.Left - hiddenToggle.Margin.Right - 0.5));
        });

        foreach (var height in new[] { 430.0, 350.0 })
        {
            pageVm.SelectedRow = null;
            Arrange(view, new(744, height));
            var rowHeightNoSelection = GetFirstRowHeight(sessionsList);
            Assert.That(sessionsList.ActualHeight, Is.GreaterThanOrEqualTo((rowHeightNoSelection * 4) - 0.5),
                $"Без выбора при 744x{height} @ 1.6 список должен вмещать минимум 4 строки");

            Assert.That(pageVm.TrySelectAt(0), Is.True);
            Arrange(view, new(744, height));
            var rowHeightWithSelection = GetFirstRowHeight(sessionsList);
            Assert.That(sessionsList.ActualHeight, Is.GreaterThanOrEqualTo((rowHeightWithSelection * 4) - 0.5),
                $"С выбранной строкой при 744x{height} @ 1.6 список должен вмещать минимум 4 строки");
        }
    }

    [Test]
    public void TitleBarView_системные_кнопки_свернуть_и_закрыть_имеют_AutomationProperties_Name()
    {
        var view = new TitleBarView();
        Arrange(view, new(1024, 40));

        var buttons = Descendants<Button>(view).ToList();
        var minimize = buttons.First(b => Equals(b.ToolTip, "Свернуть"));
        var close = buttons.First(b => Equals(b.ToolTip, "Закрыть"));
        var maxRestoreStyle = Application.Current.TryFindResource("Chrome.Button.MaxRestore");
        var maxRestore = buttons.First(b => ReferenceEquals(b.Style, maxRestoreStyle));

        Assert.Multiple(() =>
        {
            Assert.That(AutomationProperties.GetName(minimize), Is.EqualTo("Свернуть"));
            Assert.That(AutomationProperties.GetName(close), Is.EqualTo("Закрыть"));
            Assert.That(maxRestore.ReadLocalValue(AutomationProperties.NameProperty), Is.EqualTo(DependencyProperty.UnsetValue));
            Assert.That(maxRestore.ReadLocalValue(FrameworkElement.ToolTipProperty), Is.EqualTo(DependencyProperty.UnsetValue));
        });
    }

    [Test]
    public void DashboardLayoutSectionView_эталон_и_подпись_не_режутся_при_1024x640_1_6()
    {
        var store = new DashboardLayoutStore(null, Path.Combine(_directory, "dashboard-layout.json"));
        var vm = new DashboardLayoutSectionViewModel([], new MemorySettings(), store);
        var view = new DashboardLayoutSectionView { DataContext = vm };
        var host = HostWithMainWindowFont(view);

        ApplyScale(1.6);
        SyncMainWindowFont(host);
        Arrange(host, new(768, 560));

        var combo = Descendants<ComboBox>(view).First();
        var caption = Descendants<TextBlock>(view).First(t => t.Text == vm.ReferenceCaption);
        var bounds = caption.TransformToAncestor(view).TransformBounds(new(caption.RenderSize));

        Assert.Multiple(() =>
        {
            Assert.That(view.FontSize, Is.GreaterThan(20));
            Assert.That(AutomationProperties.GetName(combo), Is.EqualTo("Эталон размера окна"));
            Assert.That(caption.TextWrapping, Is.EqualTo(TextWrapping.Wrap));
            Assert.That(bounds.Right, Is.LessThanOrEqualTo(view.ActualWidth + 0.5));
        });
    }

    [Test]
    public void CommandsPageView_не_режет_последний_вызов_при_1024x640_и_держит_CanContentScroll_False()
    {
        var settingsManager = new SettingsManager(NullLogger<SettingsManager>.Instance, Path.Combine(_directory, "cmd-settings.json"));
        var cmdStore = new CommandSettingsStore(null, Path.Combine(_directory, "commands.json"));
        var usage = new CommandUsageRepository(TimeProvider.System);
        var commands = Enumerable.Range(1, 16)
            .Select(i => (IChatCommand)new StubChatCommand($"команда{i:D2}"))
            .ToArray();
        var processor = new ChatCommandProcessor(
            commands,
            cmdStore,
            usage,
            NullLogger<ChatCommandProcessor>.Instance,
            "!");

        var pageVm = new CommandsPageViewModel(
            processor,
            cmdStore,
            settingsManager,
            usage,
            new StubPrompt(),
            new AccountsStore(null, Path.Combine(_directory, "cmd-accounts.json")),
            NullLogger<CommandsPageViewModel>.Instance);

        var view = new CommandsPageView { DataContext = pageVm };
        var host = HostWithMainWindowFont(view);
        var tableScroll = (ScrollViewer)view.FindName("TableScroll")!;
        var commandList = (ListBox)view.FindName("CommandList")!;

        ApplyScale(1.0);
        SyncMainWindowFont(host);
        Arrange(host, new(768, 540));

        var lastCallHeader = Descendants<TextBlock>(view).First(static t => t.Text == "Последний вызов");
        var totalHeader = Descendants<TextBlock>(view).First(static t => t.Text == "Всего");
        var accessHeader = Descendants<TextBlock>(view).First(static t => t.Text == "Права");
        var headerRight = lastCallHeader.TransformToAncestor(tableScroll).Transform(new(lastCallHeader.ActualWidth, 0)).X;
        var firstItem = (ListBoxItem)commandList.ItemContainerGenerator.ContainerFromIndex(0);
        var firstTotalCell = Descendants<TextBlock>(firstItem).First(static t => t.Text == "0");
        var firstAccessCell = Descendants<TextBlock>(firstItem).First(static t => t.Text == "Все");
        var totalHeaderX = totalHeader.TransformToAncestor(tableScroll).Transform(new(totalHeader.ActualWidth, 0)).X;
        var totalCellX = firstTotalCell.TransformToAncestor(tableScroll).Transform(new(firstTotalCell.ActualWidth, 0)).X;
        var accessHeaderX = accessHeader.TransformToAncestor(tableScroll).Transform(default).X;
        var accessCellX = firstAccessCell.TransformToAncestor(tableScroll).Transform(default).X;

        Assert.Multiple(() =>
        {
            Assert.That(ScrollViewer.GetCanContentScroll(commandList), Is.False);
            Assert.That(tableScroll.ExtentWidth, Is.LessThanOrEqualTo(tableScroll.ViewportWidth + 0.5));
            Assert.That(headerRight, Is.LessThanOrEqualTo(tableScroll.ViewportWidth + 0.5));
            Assert.That(accessHeaderX, Is.EqualTo(accessCellX).Within(0.5));
            Assert.That(totalHeaderX, Is.EqualTo(totalCellX).Within(0.5));
        });

        ApplyScale(1.6);
        SyncMainWindowFont(host);
        Arrange(host, new(744, 540));

        var searchPlaceholder = Descendants<TextBlock>(view).First(static t => t.Text == "Найти команду");
        var searchBox = Descendants<TextBox>(view).First(static tb => AutomationProperties.GetName(tb) == "Поиск команды по имени или описанию");
        var naturalPlaceholderWidth = searchPlaceholder.DesiredSize.Width - searchPlaceholder.Margin.Left - searchPlaceholder.Margin.Right;

        Assert.Multiple(() =>
        {
            Assert.That(view.FontSize, Is.GreaterThan(20));
            Assert.That(searchBox.ActualWidth, Is.GreaterThanOrEqualTo(220));
            Assert.That(searchPlaceholder.ActualWidth, Is.GreaterThanOrEqualTo(naturalPlaceholderWidth - 0.5));
        });
    }

    [TestCase(940, 1.0)]
    [TestCase(940, 1.6)]
    [TestCase(970, 1.6)]
    public void Шапка_с_переключателем_пресета_не_обрезает_кнопки_на_узком_окне(double width, double scale)
    {
        ApplyScale(scale);

        var view = new TitleBarView
        {
            DataContext = new
            {
                BuildLabel = "v1.0.0",
                BuildTooltip = "v1.0.0",
                IsOverviewSelected = true,
                ConnectButtonText = "Подключить бота",
                ConnectButtonTooltip = "Управление ботом",
                IsConnectBlockedBySetup = false,
                Overview = new
                {
                    IsEditing = false,
                    CanEdit = true,
                    CanUndo = false,
                    ActivePresetShortLabel = "Компакт",
                    IsCompactPreset = true,
                    IsWidePreset = false,
                },
            },
        };
        var host = HostWithMainWindowFont(view);
        Arrange(host, new(width, 37));

        var presetButton = (Button)view.FindName("PresetSwitcherButton")!;
        var titleBlock = Descendants<TextBlock>(view).First(static t => t.Text == "PoproshaykaBot");
        var presetLeft = presetButton.TranslatePoint(default, view).X;
        var titleRight = titleBlock.TranslatePoint(new(titleBlock.ActualWidth, 0), view).X;

        Assert.Multiple(() =>
        {
            Assert.That(presetButton.Visibility, Is.EqualTo(Visibility.Visible));
            Assert.That(presetButton.ActualWidth, Is.GreaterThan(0));
            Assert.That(presetButton.ActualWidth, Is.GreaterThanOrEqualTo(presetButton.DesiredSize.Width - presetButton.Margin.Left - presetButton.Margin.Right - 0.5));
            Assert.That(presetLeft, Is.GreaterThanOrEqualTo(titleRight - 0.5),
                $"Переключатель пресета ({presetLeft:F1}) не должен наезжать на название приложения ({titleRight:F1}) при width={width}, scale={scale}");
        });
    }

    [Test]
    public async Task Escape_отменяет_вооружённое_отключение_бота_без_фокуса_на_кнопке_и_не_глотается_в_обычном_состоянии()
    {
        var dir = Directory.CreateTempSubdirectory("poproshayka-main-esc-");
        try
        {
            var bus = new InMemoryEventBus(NullLogger<InMemoryEventBus>.Instance);
            var gate = new SettingsWriteGate();
            var settingsManager = new SettingsManager(NullLogger<SettingsManager>.Instance, Path.Combine(dir.FullName, "settings.json"), gate);
            var server = new KestrelHttpServer(null!, settingsManager, [], NullLogger<KestrelHttpServer>.Instance, NullLoggerFactory.Instance);
            var uiSettings = new MemorySettings();
            var dispatcher = new ManualUiDispatcher();
            var connection = new BotConnectionManager(new PoproshaykaBot.Core.Infrastructure.Hosting.AppHost([], NullLogger<PoproshaykaBot.Core.Infrastructure.Hosting.AppHost>.Instance), [], null, bus, NullLogger<BotConnectionManager>.Instance);
            var accounts = new AccountsStore(filePath: Path.Combine(dir.FullName, "accounts.json"));
            var settingsPage = new SettingsPageViewModel([], settingsManager, gate, server, bus, NullLogger<SettingsPageViewModel>.Instance, System.Reflection.DispatchProxy.Create<IDialogService, SettingsNavigationSaveTests.InertProxy>(), uiSettings);
            var layoutStore = new DashboardLayoutStore(filePath: Path.Combine(dir.FullName, "layout.json"));
            using var overview = new DashboardViewModel([], new DashboardLayoutCoordinator(layoutStore), TimeProvider.System);
            var history = new BroadcastHistoryStore(null, Path.Combine(dir.FullName, "broadcasts.json"), Path.Combine(dir.FullName, "sessions.json"));
            var boxArt = new PoproshaykaBot.Wpf.Infrastructure.GameBoxArtProvider(new FakeBoxArtCache(), NullLogger<PoproshaykaBot.Wpf.Infrastructure.GameBoxArtProvider>.Instance);
            using var historyPage = new StreamHistoryPageViewModel(history, new UserStatisticsRepository(NullLogger<UserStatisticsRepository>.Instance), uiSettings, boxArt, bus);
            var windows = System.Reflection.DispatchProxy.Create<IMainWindowController, SettingsNavigationSaveTests.InertProxy>();
            var dialogs = System.Reflection.DispatchProxy.Create<IDialogService, SettingsNavigationSaveTests.InertProxy>();
            var shellLauncher = System.Reflection.DispatchProxy.Create<KeepShell.Services.Platform.IShellLauncher, SettingsNavigationSaveTests.InertProxy>();
            var update = new UpdateBannerViewModel(
                System.Reflection.DispatchProxy.Create<KeepShell.Updates.IUpdateCoordinator, SettingsNavigationSaveTests.InertProxy>(),
                System.Reflection.DispatchProxy.Create<IUpdateEnvironment, SettingsNavigationSaveTests.InertProxy>(),
                dialogs,
                NullLogger<UpdateBannerViewModel>.Instance,
                dispatcher,
                shellLauncher,
                windows);
            using var onboarding = new OnboardingBannerViewModel(new PoproshaykaBot.Core.Settings.Onboarding.OnboardingChecklist(settingsManager, accounts), bus);
            var debug = new DebugBannerViewModel(new PoproshaykaBot.Core.Debugging.TargetChannelProvider(settingsManager, new DebugChannelStore(filePath: Path.Combine(dir.FullName, "debug.json")), PoproshaykaBot.Core.Debugging.DebugChannelOverride.None));
            var preferences = new ShellPreferences(uiSettings);
            using var shell = new ShellViewModel(
                new KeepShell.ViewModels.ModalHostViewModel(dispatcher),
                overview,
                settingsPage,
                null!,
                historyPage,
                null!,
                null!,
                null!,
                null!,
                preferences,
                connection,
                bus,
                NullLogger<ShellViewModel>.Instance,
                dialogs,
                update,
                onboarding,
                debug,
                null!,
                null!,
                uiSettings,
                dispatcher,
                new FakeUnsavedChangesPrompt(),
                windows);

            var window = new MainWindow(shell, uiSettings, layoutStore);
            var source = new System.Windows.Interop.HwndSource(new System.Windows.Interop.HwndSourceParameters("test") { Width = 100, Height = 100 });
            try
            {
                await bus.PublishAsync(new PoproshaykaBot.Core.Infrastructure.Events.Lifecycle.BotLifecyclePhaseChanged(PoproshaykaBot.Core.Infrastructure.Events.Lifecycle.BotLifecyclePhase.Connected));

                var unarmedArgs = new System.Windows.Input.KeyEventArgs(System.Windows.Input.Keyboard.PrimaryDevice, source, 0, System.Windows.Input.Key.Escape)
                {
                    RoutedEvent = UIElement.PreviewKeyDownEvent,
                };
                window.RaiseEvent(unarmedArgs);
                Assert.That(unarmedArgs.Handled, Is.False);

                await shell.ToggleConnectCommand.ExecuteAsync(null);
                Assert.That(shell.IsDisconnectConfirmationArmed, Is.True);

                var armedArgs = new System.Windows.Input.KeyEventArgs(System.Windows.Input.Keyboard.PrimaryDevice, source, 0, System.Windows.Input.Key.Escape)
                {
                    RoutedEvent = UIElement.PreviewKeyDownEvent,
                };
                window.RaiseEvent(armedArgs);

                Assert.Multiple(() =>
                {
                    Assert.That(armedArgs.Handled, Is.True);
                    Assert.That(shell.IsDisconnectConfirmationArmed, Is.False);
                });
            }
            finally
            {
                source.Dispose();
                await connection.DisposeAsync();
                settingsPage.Dispose();
                update.Dispose();
            }
        }
        finally
        {
            Directory.Delete(dir.FullName, recursive: true);
        }
    }

    [Test]
    public void Escape_в_карточках_OBS_не_глотается_без_активного_подтверждения_и_отменяет_при_подтверждении()
    {
        var dispatcher = new ManualUiDispatcher();
        var obsClient = new InertObsClient();
        var settingsManager = new SettingsManager(NullLogger<SettingsManager>.Instance, Path.Combine(_directory, "obs-esc-settings.json"));
        using var integration = new PoproshaykaBot.Core.Obs.ObsIntegrationService(obsClient, settingsManager, NullLogger<PoproshaykaBot.Core.Obs.ObsIntegrationService>.Instance);

        using var cardVm = new PoproshaykaBot.Wpf.ViewModels.Tiles.ObsOutputCardViewModel(
            PoproshaykaBot.Wpf.ViewModels.Tiles.ObsOutputCardKind.Stream,
            integration,
            NullLogger.Instance,
            dispatcher);
        cardVm.ApplySnapshot(active: true, paused: false, "00:01:00.000", null);
        var cardView = new PoproshaykaBot.Wpf.Views.Tiles.ObsOutputCardView { DataContext = cardVm };

        var secondaryVm = new PoproshaykaBot.Wpf.ViewModels.Tiles.ObsSecondaryOutputViewModel(
            new PoproshaykaBot.Core.Obs.ObsSecondaryOutputSnapshot(
                "YouTube", "yt", "rtmp_output", PoproshaykaBot.Core.Obs.ObsMultistreamVendor.AitumMultistream,
                IsActive: true, IsReconnecting: false, "00:01:00.000", 100, 1024, 0, 0, 1000, CanControl: true, IsVertical: false),
            _ => Task.CompletedTask,
            dispatcher);
        var secondaryView = new PoproshaykaBot.Wpf.Views.Tiles.ObsSecondaryOutputView { DataContext = secondaryVm };

        using var source = new System.Windows.Interop.HwndSource(new System.Windows.Interop.HwndSourceParameters("obs-test") { Width = 100, Height = 100 });
        try
        {
            var cardUnarmed = new System.Windows.Input.KeyEventArgs(System.Windows.Input.Keyboard.PrimaryDevice, source, 0, System.Windows.Input.Key.Escape)
            {
                RoutedEvent = UIElement.PreviewKeyDownEvent,
            };
            cardView.RaiseEvent(cardUnarmed);
            Assert.That(cardUnarmed.Handled, Is.False);

            cardVm.InvokePrimaryCommand.Execute(null);
            Assert.That(cardVm.IsStopConfirmationArmed, Is.True);
            var cardArmed = new System.Windows.Input.KeyEventArgs(System.Windows.Input.Keyboard.PrimaryDevice, source, 0, System.Windows.Input.Key.Escape)
            {
                RoutedEvent = UIElement.PreviewKeyDownEvent,
            };
            cardView.RaiseEvent(cardArmed);
            Assert.That(cardArmed.Handled, Is.True);
            Assert.That(cardVm.IsStopConfirmationArmed, Is.False);

            var secondaryUnarmed = new System.Windows.Input.KeyEventArgs(System.Windows.Input.Keyboard.PrimaryDevice, source, 0, System.Windows.Input.Key.Escape)
            {
                RoutedEvent = UIElement.PreviewKeyDownEvent,
            };
            secondaryView.RaiseEvent(secondaryUnarmed);
            Assert.That(secondaryUnarmed.Handled, Is.False);

            secondaryVm.ToggleCommand.Execute(null);
            Assert.That(secondaryVm.IsStopConfirmationArmed, Is.True);
            var secondaryArmed = new System.Windows.Input.KeyEventArgs(System.Windows.Input.Keyboard.PrimaryDevice, source, 0, System.Windows.Input.Key.Escape)
            {
                RoutedEvent = UIElement.PreviewKeyDownEvent,
            };
            secondaryView.RaiseEvent(secondaryArmed);
            Assert.That(secondaryArmed.Handled, Is.True);
            Assert.That(secondaryVm.IsStopConfirmationArmed, Is.False);
        }
        finally
        {
            secondaryVm.Dispose();
        }
    }

    private sealed class InertObsClient : PoproshaykaBot.Core.Obs.IObsWebSocketClient
    {
        public event EventHandler<PoproshaykaBot.Core.Obs.ObsWebSocketEventArgs>? EventReceived { add { } remove { } }
        public bool IsConnected => false;
        public DateTimeOffset? LastExchangeAt => null;
        public Task<PoproshaykaBot.Core.Obs.ObsConnectionSnapshot> ConnectAsync(PoproshaykaBot.Core.Obs.ObsConnectionOptions options, CancellationToken cancellationToken) => Task.FromResult(new PoproshaykaBot.Core.Obs.ObsConnectionSnapshot(false, null, null, null));
        public Task DisconnectAsync(CancellationToken cancellationToken) => Task.CompletedTask;
        public Task<System.Text.Json.JsonElement?> SendRequestAsync(string requestType, object? requestData, CancellationToken cancellationToken) => Task.FromResult<System.Text.Json.JsonElement?>(null);
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private UserStatisticsPageViewModel CreateUsersPage(int count)
    {
        var settingsManager = new SettingsManager(NullLogger<SettingsManager>.Instance, Path.Combine(_directory, "settings.json"));
        var statistics = new UserStatisticsRepository(NullLogger<UserStatisticsRepository>.Instance);
        var eventBus = new InMemoryEventBus(NullLogger<InMemoryEventBus>.Instance);

        var users = Enumerable.Range(1, count)
            .Select(i => new UserStatistics
            {
                Platform = StreamingPlatform.Twitch,
                UserId = i.ToString(System.Globalization.CultureInfo.InvariantCulture),
                Name = $"VeryLongViewerNameForCompactLayoutTest_{i}",
                MessageCount = (ulong)(100 - i),
                BonusPoints = (ulong)((count - i + 1) * 100),
                PenaltyPoints = 5,
            })
            .ToList();
        statistics.SeedPlatform(StreamingPlatform.Twitch, users);

        return new(
            statistics,
            null!,
            new UserRankService(settingsManager),
            new UserPointsManagementService(statistics, settingsManager, eventBus, NullLogger<UserPointsManagementService>.Instance),
            new FakeChannelProvider(),
            settingsManager,
            null!,
            eventBus,
            new MemorySettings(),
            FakeAvatarCache.CreateProvider());
    }

    private StreamHistoryPageViewModel CreateStreamsPage(int count)
    {
        var store = new BroadcastHistoryStore(
            null,
            Path.Combine(_directory, "broadcasts.json"),
            Path.Combine(_directory, "sessions.json"));

        var start = new DateTimeOffset(new DateTime(2026, 9, 1, 18, 0, 0, DateTimeKind.Local));
        for (var i = 0; i < count; i++)
        {
            store.TryAppend(BroadcastRecord.FromTwitchSession(new StreamSessionRecord
            {
                Channel = "channel",
                StartedAt = start.AddDays(i),
                EndedAt = start.AddDays(i).AddHours(3),
                Title = $"Стрим {i}",
                Game = i % 2 == 0 ? "Just Chatting" : "Software and Game Development",
                MessageCount = 100 + i,
                ChatterCount = 10,
                PeakViewers = 50 + i,
                AverageViewers = 25,
                IsHidden = i == 0,
            }));
        }

        var users = new UserStatisticsRepository(NullLogger<UserStatisticsRepository>.Instance);
        var boxArt = new GameBoxArtProvider(new FakeBoxArtCache(), NullLogger<GameBoxArtProvider>.Instance);
        return new(store, users, new MemorySettings(), boxArt, new InMemoryEventBus(NullLogger<InMemoryEventBus>.Instance));
    }

    private static double GetFirstRowHeight(ListBox listBox)
    {
        return listBox.ItemContainerGenerator.ContainerFromIndex(0) is FrameworkElement item && item.ActualHeight > 0
            ? item.ActualHeight
            : 34;
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
            if (Math.Abs(scale - FontScaleManager.DefaultScale) < 0.001)
            {
                resources[key] = (double)tokens[key];
            }
            else
            {
                resources[key] = (double)tokens[key] * scale;
            }
        }

        AppScaledSizes.Register(Application.Current);
    }

    private static ContentControl HostWithMainWindowFont(FrameworkElement content)
    {
        var host = new ContentControl { Content = content };
        SyncMainWindowFont(host);
        return host;
    }

    private static void SyncMainWindowFont(Control host)
    {
        if (Application.Current?.TryFindResource("Font.Sans") is FontFamily fontFamily)
        {
            host.FontFamily = fontFamily;
        }

        if (Application.Current?.TryFindResource("Font.Size.M") is double fontSize)
        {
            host.FontSize = fontSize;
        }
    }

    private static void Arrange(FrameworkElement element, Size size)
    {
        element.Measure(size);
        element.Arrange(new(size));
        element.UpdateLayout();
    }

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

    private sealed class FakeChannelProvider : IChannelProvider
    {
        public string? Channel => "test";
    }

    private sealed class StubGameResolver : IGameCategoryResolver
    {
        public Task<IReadOnlyList<GameSuggestion>> SearchAsync(string query, CancellationToken cancellationToken)
            => Task.FromResult<IReadOnlyList<GameSuggestion>>([]);

        public Task<GameCategoryResolution> ResolveAsync(string query, CancellationToken cancellationToken)
            => Task.FromResult(new GameCategoryResolution(null, []));

        public Task RememberAsync(GameSuggestion suggestion) => Task.CompletedTask;
    }

    private sealed class StubChatCommand(string canonical) : IChatCommand
    {
        public string Canonical => canonical;
        public IReadOnlyCollection<string> Aliases => [];
        public string Description => canonical;
        public PlatformCapabilities RequiredCapabilities => PlatformCapabilities.None;
        public bool CanExecute(CommandContext context) => true;
        public Task<OutgoingMessage?> ExecuteAsync(CommandContext context, CancellationToken cancellationToken)
            => Task.FromResult<OutgoingMessage?>(null);
    }

    private sealed class StubPrompt : IUnsavedChangesPrompt
    {
        public UnsavedChangesDecision Ask(string subject, string action) => UnsavedChangesDecision.Discard;
    }
}
