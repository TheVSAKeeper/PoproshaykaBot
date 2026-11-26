using System.IO;
using System.Net.Http;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using KeepShell.Bootstrap;
using KeepShell.Services;
using KeepShell.Services.Modal;
using KeepShell.Services.Platform;
using KeepShell.Testing;
using KeepShell.ViewModels;
using MahApps.Metro.IconPacks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using PoproshaykaBot.Core.Broadcast;
using PoproshaykaBot.Core.Chat;
using PoproshaykaBot.Core.Debugging;
using PoproshaykaBot.Core.Diagnostics;
using PoproshaykaBot.Core.Infrastructure.Di;
using PoproshaykaBot.Core.Infrastructure.Events;
using PoproshaykaBot.Core.Infrastructure.Events.Chat;
using PoproshaykaBot.Core.Infrastructure.Events.Lifecycle;
using PoproshaykaBot.Core.Infrastructure.Hosting;
using PoproshaykaBot.Core.Infrastructure.Logging;
using PoproshaykaBot.Core.Obs;
using PoproshaykaBot.Core.Platforms;
using PoproshaykaBot.Core.Platforms.Auth;
using PoproshaykaBot.Core.Polls;
using PoproshaykaBot.Core.Server;
using PoproshaykaBot.Core.Settings;
using PoproshaykaBot.Core.Settings.Onboarding;
using PoproshaykaBot.Core.Settings.Stores;
using PoproshaykaBot.Core.Statistics;
using PoproshaykaBot.Core.Streaming;
using PoproshaykaBot.Core.Twitch;
using PoproshaykaBot.Core.Twitch.Auth;
using PoproshaykaBot.Core.Twitch.Chat;
using PoproshaykaBot.Core.Twitch.GameArt;
using PoproshaykaBot.Core.Twitch.Onboarding;
using PoproshaykaBot.Core.Update;
using PoproshaykaBot.Core.Users;
using PoproshaykaBot.Core.Users.Avatars;
using PoproshaykaBot.Core.YouTube;
using PoproshaykaBot.Core.YouTube.Auth;
using PoproshaykaBot.Wpf.Infrastructure;
using PoproshaykaBot.Wpf.Tests.Support;
using PoproshaykaBot.Wpf.ViewModels.Onboarding;
using PoproshaykaBot.Wpf.Views.Onboarding;

namespace PoproshaykaBot.Wpf.Tests.Onboarding;

[TestFixture]
public sealed class OnboardingStage6Tests
{
    private string _tempDir = string.Empty;

    [OneTimeSetUp]
    public void RegisterPackUris() => PackScheme.Ensure();

    [SetUp]
    public void SetUp()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"stage6-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
    }

    [TearDown]
    public void TearDown()
    {
        if (Directory.Exists(_tempDir))
        {
            Directory.Delete(_tempDir, recursive: true);
        }
    }

    [Test]
    public async Task BotConnectionPage_MixedStateShowsPartialHeaderPerPlatformRetryAndAllowsAdvance()
    {
        var settingsManager = new SettingsManager(NullLogger<SettingsManager>.Instance, Path.Combine(_tempDir, "settings.json"));
        var accountsStore = new AccountsStore(filePath: Path.Combine(_tempDir, "accounts.json"));
        var ytSettingsStore = new YouTubeSettingsStore(filePath: Path.Combine(_tempDir, "youtube.json"));
        var ytAccountsStore = new YouTubeAccountsStore(filePath: Path.Combine(_tempDir, "youtube-accounts.json"));
        var bus = new InMemoryEventBus(NullLogger<InMemoryEventBus>.Instance);
        var controller = new StubConnectionController();
        var registry = new StubRegistry(
            new StubPlatform(StreamingPlatform.Twitch, "Twitch", true, false, "twchan"),
            new StubPlatform(StreamingPlatform.YouTube, "YouTube", true, false, "@ytchan"));

        using var page = new BotConnectionPageViewModel(
            controller,
            bus,
            settingsManager,
            accountsStore,
            NullLogger<BotConnectionPageViewModel>.Instance,
            ytSettingsStore,
            ytAccountsStore,
            registry);

        var context = new OnboardingContext(
            new AppSettings { Twitch = new TwitchSettings { Enabled = true, ClientId = "cid", ClientSecret = "sec", Channel = "twchan" } },
            new(),
            new(),
            new YouTubeSettings { Enabled = true, ClientId = "yt-cid", Channel = "@ytchan" });

        page.OnEnter(context);
        Assert.That(page.CanAdvance, Is.False);

        await bus.PublishAsync(new PlatformConnectionChanged(StreamingPlatform.Twitch, BotLifecyclePhase.Connected));
        Assert.That(page.CanAdvance, Is.False, "Пока YouTube подключается, продолжение недоступно");

        await bus.PublishAsync(new PlatformConnectionChanged(StreamingPlatform.YouTube, BotLifecyclePhase.Failed, new HttpRequestException("offline")));

        var twitchRow = page.PlatformStatuses.Single(r => r.Platform == StreamingPlatform.Twitch);
        var youtubeRow = page.PlatformStatuses.Single(r => r.Platform == StreamingPlatform.YouTube);

        Assert.Multiple(() =>
        {
            Assert.That(page.CanAdvance, Is.True);
            Assert.That(page.StatusText, Is.EqualTo("Подключена 1 из 2 площадок"));
            Assert.That(page.HeaderSeverity, Is.EqualTo(StatusSeverity.Warning));
            Assert.That(page.ShowRetry, Is.False);
            Assert.That(page.DetailsText, Does.Not.Contain("Ошибка сети при подключении"));
            Assert.That(twitchRow.ShowRetry, Is.False);
            Assert.That(youtubeRow.ShowRetry, Is.True);
            Assert.That(youtubeRow.RetryButtonText, Is.EqualTo("Повторить для YouTube"));
            Assert.That(youtubeRow.DetailsText, Is.EqualTo("Ошибка сети при подключении"));
        });

        controller.StartedPlatforms.Clear();
        youtubeRow.RetryCommand.Execute(null);
        Assert.Multiple(() =>
        {
            Assert.That(controller.StartedPlatforms, Is.EqualTo(new[] { StreamingPlatform.YouTube }));
            Assert.That(youtubeRow.Phase, Is.EqualTo(BotLifecyclePhase.Connecting));
            Assert.That(page.CanAdvance, Is.False);
        });
    }

    [Test]
    public async Task BotConnectionPage_AllFailedAllowsAdvanceAfterAttemptCompletes()
    {
        var settingsManager = new SettingsManager(NullLogger<SettingsManager>.Instance, Path.Combine(_tempDir, "settings.json"));
        var accountsStore = new AccountsStore(filePath: Path.Combine(_tempDir, "accounts.json"));
        var ytSettingsStore = new YouTubeSettingsStore(filePath: Path.Combine(_tempDir, "youtube.json"));
        var ytAccountsStore = new YouTubeAccountsStore(filePath: Path.Combine(_tempDir, "youtube-accounts.json"));
        var bus = new InMemoryEventBus(NullLogger<InMemoryEventBus>.Instance);
        var controller = new StubConnectionController();
        var registry = new StubRegistry(
            new StubPlatform(StreamingPlatform.Twitch, "Twitch", true, false, "twchan"),
            new StubPlatform(StreamingPlatform.YouTube, "YouTube", true, false, "@ytchan"));

        using var page = new BotConnectionPageViewModel(
            controller,
            bus,
            settingsManager,
            accountsStore,
            NullLogger<BotConnectionPageViewModel>.Instance,
            ytSettingsStore,
            ytAccountsStore,
            registry);

        var context = new OnboardingContext(
            new AppSettings { Twitch = new TwitchSettings { Enabled = true, ClientId = "cid", ClientSecret = "sec", Channel = "twchan" } },
            new(),
            new(),
            new YouTubeSettings { Enabled = true, ClientId = "yt-cid", Channel = "@ytchan" });

        page.OnEnter(context);
        await bus.PublishAsync(new PlatformConnectionChanged(StreamingPlatform.Twitch, BotLifecyclePhase.Failed, new HttpRequestException("offline")));
        await bus.PublishAsync(new PlatformConnectionChanged(StreamingPlatform.YouTube, BotLifecyclePhase.Failed, new HttpRequestException("offline")));

        Assert.Multiple(() =>
        {
            Assert.That(page.CanAdvance, Is.True);
            Assert.That(page.ShowRetry, Is.False);
            Assert.That(page.PlatformStatuses.All(r => r.ShowRetry), Is.True);
            Assert.That(page.DetailsText, Does.Not.Contain("Ошибка сети при подключении"));
        });
    }

    [Test]
    public async Task CompletionPage_HonestConnectionCheckboxDisconnectsBotWhenUnchecked()
    {
        await using var provider = BuildProvider();
        var settingsManager = new SettingsManager(NullLogger<SettingsManager>.Instance, Path.Combine(_tempDir, "settings.json"));
        var accountsStore = new AccountsStore(filePath: Path.Combine(_tempDir, "accounts.json"));
        var bus = provider.GetRequiredService<IEventBus>();
        var controller = new StubConnectionController { CurrentPhase = BotLifecyclePhase.Connected };
        controller.SetPlatformPhase(StreamingPlatform.Twitch, BotLifecyclePhase.Connected);

        using var page = new CompletionPageViewModel(
            settingsManager,
            accountsStore,
            bus,
            provider.GetRequiredService<KestrelHttpServer>(),
            new StubChannelValidator(),
            controller,
            NullLogger<CompletionPageViewModel>.Instance,
            new StubDialogService());

        var context = CreateValidTwitchContext();
        accountsStore.SaveAll(context.BotAccount, context.BroadcasterAccount);
        page.OnEnter(context);
        await WaitForValidationAsync(page);

        Assert.That(page.ConnectionAfterFinishText, Is.EqualTo("Оставить бота подключённым после завершения"));

        page.AutoConnect = false;
        Assert.That(await page.OnLeavingAsync(context), Is.True);
        Assert.That(controller.StopCount, Is.EqualTo(1));

        controller.CurrentPhase = BotLifecyclePhase.Disconnected;
        controller.SetPlatformPhase(StreamingPlatform.Twitch, BotLifecyclePhase.Disconnected);
        page.OnEnter(context);
        await WaitForValidationAsync(page);

        Assert.That(page.ConnectionAfterFinishText, Is.EqualTo("Подключить бота после завершения"));
        page.AutoConnect = true;
        Assert.That(await page.OnLeavingAsync(context), Is.True);
        Assert.That(controller.StartAllCount, Is.EqualTo(1));
    }

    [Test]
    public async Task HealthCheckPage_TracksResultsPerPlatformUpdatesBotStatusAndCopiesOverlay()
    {
        var messenger = new StubChatMessenger();
        var bus = new InMemoryEventBus(NullLogger<InMemoryEventBus>.Instance);
        var clipboard = new RecordingClipboard();
        using var page = new HealthCheckPageViewModel(
            messenger,
            bus,
            NullLogger<HealthCheckPageViewModel>.Instance,
            new StubLauncher(),
            clipboard);

        var context = new OnboardingContext(
            new AppSettings { Twitch = new TwitchSettings { Enabled = true, HttpServerPort = 8080 } },
            new(),
            new(),
            new YouTubeSettings { Enabled = true, ClientId = "cid", Channel = "@yt" });

        page.OnEnter(context);

        page.CopyOverlayUrlCommand.Execute(null);
        Assert.Multiple(() =>
        {
            Assert.That(clipboard.LastText, Is.EqualTo("http://localhost:8080/chat"));
            Assert.That(page.OverlayCopyFeedback, Is.EqualTo("Ссылка скопирована"));
            Assert.That(page.OverlayCopyFeedbackSeverity, Is.EqualTo(StatusSeverity.Success));
        });

        page.ChatTestMessage = "привет twitch";
        page.SendChatTestCommand.Execute(null);
        Assert.Multiple(() =>
        {
            Assert.That(page.ChatTestSuccess, Is.True);
            Assert.That(page.ChatTestSeverity, Is.EqualTo(StatusSeverity.Success));
            Assert.That(page.ChatTestStatusText, Does.Not.Contain("✓"));
        });

        await PublishBotChatAsync(bus, StreamingPlatform.Twitch, UserStatus.None);
        Assert.Multiple(() =>
        {
            Assert.That(page.BotStatusKnown, Is.True);
            Assert.That(page.BotStatusText, Is.EqualTo("Бот в чате (Twitch): обычный пользователь"));
        });

        page.IsYouTubeSelected = true;
        Assert.Multiple(() =>
        {
            Assert.That(page.ChatTestStatusText, Is.EqualTo("Не выполнено"));
            Assert.That(page.ChatTestSuccess, Is.False);
            Assert.That(page.ChatTestSeverity, Is.EqualTo(StatusSeverity.None));
            Assert.That(page.BotStatusKnown, Is.False);
            Assert.That(page.BotStatusText, Is.EqualTo("Бот в чате (YouTube): ожидание сообщения…"));
        });

        messenger.Outcome = ChatEnqueueResult.ObserverMode;
        page.SendChatTestCommand.Execute(null);
        Assert.Multiple(() =>
        {
            Assert.That(page.ChatTestError, Is.True);
            Assert.That(page.ChatTestSeverity, Is.EqualTo(StatusSeverity.Error));
            Assert.That(page.ChatTestStatusText, Does.Not.Contain("✗"));
        });

        await PublishBotChatAsync(bus, StreamingPlatform.Twitch, UserStatus.Moderator);
        page.IsTwitchSelected = true;
        Assert.Multiple(() =>
        {
            Assert.That(page.ChatTestSuccess, Is.True);
            Assert.That(page.ChatTestSeverity, Is.EqualTo(StatusSeverity.Success));
            Assert.That(page.BotStatusKnown, Is.True);
            Assert.That(page.BotStatusText, Is.EqualTo("Бот в чате (Twitch): модератор"));
        });

        await bus.PublishAsync(new PlatformAuthorizationRefreshed(StreamingPlatform.Twitch, PlatformAuthRole.Bot));
        Assert.That(page.BotStatusKnown, Is.False);
    }

    [Test]
    [Apartment(ApartmentState.STA)]
    public void HealthCheckPageView_EnterInChatInputSendsNonEmptyTestAndHandlesEnterEvenWhenEmpty()
    {
        EnsureApplicationResources();

        var messenger = new StubChatMessenger();
        var bus = new InMemoryEventBus(NullLogger<InMemoryEventBus>.Instance);
        using var vm = new HealthCheckPageViewModel(
            messenger,
            bus,
            NullLogger<HealthCheckPageViewModel>.Instance,
            new StubLauncher());
        vm.OnEnter(new OnboardingContext(new AppSettings(), new(), new()));

        var view = new HealthCheckPageView { DataContext = vm };
        view.Measure(new Size(688, 420));
        view.Arrange(new Rect(0, 0, 688, 420));
        view.UpdateLayout();
        var textBox = FindVisualChild<TextBox>(view);
        Assert.That(textBox, Is.Not.Null);

        var source = new StubPresentationSource(view);
        var emptyEnter = new KeyEventArgs(Keyboard.PrimaryDevice, source, 0, Key.Enter)
        {
            RoutedEvent = Keyboard.PreviewKeyDownEvent,
        };
        textBox!.RaiseEvent(emptyEnter);

        Assert.Multiple(() =>
        {
            Assert.That(emptyEnter.Handled, Is.True);
            Assert.That(messenger.Sent, Is.Empty);
        });

        vm.ChatTestMessage = "тест через Enter";
        var filledEnter = new KeyEventArgs(Keyboard.PrimaryDevice, source, 0, Key.Enter)
        {
            RoutedEvent = Keyboard.PreviewKeyDownEvent,
        };
        textBox.RaiseEvent(filledEnter);

        Assert.Multiple(() =>
        {
            Assert.That(filledEnter.Handled, Is.True);
            Assert.That(messenger.Sent, Is.EqualTo(new[] { "тест через Enter" }));
        });
    }

    [Test]
    public async Task CompletionPage_DynamicTitleStructuredValidationItemsNoMissingGlyphsAndRevalidate()
    {
        await using var provider = BuildProvider();
        var settingsManager = new SettingsManager(NullLogger<SettingsManager>.Instance, Path.Combine(_tempDir, "settings.json"));
        var accountsStore = new AccountsStore(filePath: Path.Combine(_tempDir, "accounts.json"));
        var bus = provider.GetRequiredService<IEventBus>();
        var pending = new TaskCompletionSource<ChannelValidationResult>(TaskCreationOptions.RunContinuationsAsynchronously);

        using var completion = new CompletionPageViewModel(
            settingsManager,
            accountsStore,
            bus,
            provider.GetRequiredService<KestrelHttpServer>(),
            new StubChannelValidator(pending.Task),
            provider.GetRequiredService<BotConnectionManager>(),
            NullLogger<CompletionPageViewModel>.Instance,
            new StubDialogService());

        using var wizard = new OnboardingWizardViewModel(
            [completion],
            settingsManager,
            accountsStore,
            provider.GetRequiredService<KestrelHttpServer>(),
            provider.GetRequiredService<BotConnectionManager>(),
            bus,
            NullLogger<OnboardingWizardViewModel>.Instance);

        var context = new OnboardingContext(
            new AppSettings { Twitch = new TwitchSettings { Enabled = true, ClientId = "cid", ClientSecret = "sec", Channel = "mychan" } },
            CreateValidBotAccount(),
            new(),
            new YouTubeSettings { Enabled = true, ClientId = "yt-cid", Channel = "@myyt" });
        accountsStore.SaveAll(context.BotAccount, context.BroadcasterAccount);

        completion.OnEnter(context);
        Assert.Multiple(() =>
        {
            Assert.That(completion.PageTitle, Is.EqualTo("Проверка настроек"));
            Assert.That(wizard.HeaderTitle, Is.EqualTo("Проверка настроек"));
        });

        pending.SetResult(ChannelValidationResult.Found);
        await WaitForValidationAsync(completion);

        Assert.Multiple(() =>
        {
            Assert.That(completion.PageTitle, Is.EqualTo("Нужно исправить настройки"));
            Assert.That(wizard.HeaderTitle, Is.EqualTo("Нужно исправить настройки"));
            Assert.That(completion.CanAdvance, Is.False);
            Assert.That(completion.SummaryText, Does.Not.Contain("@–"));
            Assert.That(completion.SummaryText, Does.Contain("не подключён"));
            Assert.That(completion.ValidationText, Does.Not.Contain("✓").And.Not.Contain("✗").And.Not.Contain("⚠").And.Not.Contain("⏳"));

            var ytChannel = completion.ValidationItems.Single(i => i.Title == "Канал YouTube");
            Assert.That(ytChannel.Severity, Is.EqualTo(StatusSeverity.Success));
            Assert.That(ytChannel.IconKind, Is.EqualTo(PackIconLucideKind.Check));
            Assert.That(ytChannel.CanFix, Is.False);

            var ytBot = completion.ValidationItems.Single(i => i.Title == "Доступы YouTube бота");
            Assert.That(ytBot.Severity, Is.EqualTo(StatusSeverity.Error));
            Assert.That(ytBot.IconKind, Is.EqualTo(PackIconLucideKind.X));
            Assert.That(ytBot.TargetStep, Is.EqualTo(OnboardingStep.YouTube));
            Assert.That(ytBot.CanFix, Is.True);
        });

        context.YouTubeBotAccount = CreateValidYouTubeBotAccount();
        completion.RevalidateCommand.Execute(null);
        pending.TrySetResult(ChannelValidationResult.Found);
        await WaitForValidationAsync(completion);

        Assert.Multiple(() =>
        {
            Assert.That(completion.PageTitle, Is.EqualTo("Готово к запуску"));
            Assert.That(wizard.HeaderTitle, Is.EqualTo("Готово к запуску"));
            Assert.That(completion.CanAdvance, Is.True);
        });

        var emptyChannelContext = new OnboardingContext(
            new AppSettings { Twitch = new TwitchSettings { Enabled = true, ClientId = "cid", ClientSecret = "sec", Channel = string.Empty } },
            new(),
            new());
        accountsStore.SaveAll(new(), new());
        completion.OnEnter(emptyChannelContext);
        await WaitForValidationAsync(completion);
        Assert.Multiple(() =>
        {
            Assert.That(completion.SummaryText, Does.Not.Contain("@–"));
            Assert.That(completion.SummaryText, Does.Contain("не задан"));
            Assert.That(completion.SummaryText, Does.Contain("не подключён"));
            Assert.That(completion.ValidationItems.Single(i => i.Title == "Доступы аккаунта бота").TargetStep,
                Is.EqualTo(OnboardingStep.BotAuthorization));
        });
    }

    [Test]
    public async Task CompletionPage_FixButtonNavigatesToTargetStepIncludingBroadcasterAuthorization()
    {
        await using var provider = BuildProvider();
        var settingsManager = new SettingsManager(NullLogger<SettingsManager>.Instance, Path.Combine(_tempDir, "settings.json"));
        var accountsStore = new AccountsStore(filePath: Path.Combine(_tempDir, "accounts.json"));
        var bus = provider.GetRequiredService<IEventBus>();

        var platforms = new PlatformsPageViewModel();
        using var credentials = new CredentialsPageViewModel(
            new StubCredentialsValidator(),
            new StubChannelValidator(),
            NullLogger<CredentialsPageViewModel>.Instance,
            new ManualUiDispatcher());
        using var broadcaster = new BroadcasterAuthorizationPageViewModel(
            new StubTwitchOAuthService(),
            accountsStore,
            NullLogger<BroadcasterAuthorizationPageViewModel>.Instance,
            new StubLauncher(),
            new RecordingClipboard(),
            new StubEmbeddedTwitchAuthDialog(EmbeddedTwitchAuthResult.Canceled("cancel")),
            new ManualUiDispatcher());
        using var completion = new CompletionPageViewModel(
            settingsManager,
            accountsStore,
            bus,
            provider.GetRequiredService<KestrelHttpServer>(),
            new StubChannelValidator(),
            provider.GetRequiredService<BotConnectionManager>(),
            NullLogger<CompletionPageViewModel>.Instance,
            new StubDialogService());

        using var wizard = new OnboardingWizardViewModel(
            [completion, platforms, credentials, broadcaster],
            settingsManager,
            accountsStore,
            provider.GetRequiredService<KestrelHttpServer>(),
            provider.GetRequiredService<BotConnectionManager>(),
            bus,
            NullLogger<OnboardingWizardViewModel>.Instance);

        Assert.That(wizard.CurrentPage, Is.SameAs(completion));

        completion.FixIssueCommand.Execute(OnboardingStep.BroadcasterAuthorization);
        Assert.That(wizard.CurrentPage, Is.SameAs(broadcaster));

        completion.FixIssueCommand.Execute(OnboardingStep.Credentials);
        Assert.That(wizard.CurrentPage, Is.SameAs(credentials));
    }

    [Test]
    public async Task CompletionPage_ShowsChatDisplayRadiosOnlyWhenBothTwitchRolesAuthorized()
    {
        await using var provider = BuildProvider();
        var settingsManager = new SettingsManager(NullLogger<SettingsManager>.Instance, Path.Combine(_tempDir, "settings.json"));
        var accountsStore = new AccountsStore(filePath: Path.Combine(_tempDir, "accounts.json"));
        var bus = provider.GetRequiredService<IEventBus>();

        using var page = new CompletionPageViewModel(
            settingsManager,
            accountsStore,
            bus,
            provider.GetRequiredService<KestrelHttpServer>(),
            new StubChannelValidator(),
            provider.GetRequiredService<BotConnectionManager>(),
            NullLogger<CompletionPageViewModel>.Instance,
            new StubDialogService());

        var botOnlyContext = CreateValidTwitchContext();
        botOnlyContext.Settings.Twitch.ChatDisplayAccount = PlatformAuthRole.Broadcaster;
        accountsStore.SaveAll(botOnlyContext.BotAccount, botOnlyContext.BroadcasterAccount);

        page.OnEnter(botOnlyContext);
        await WaitForValidationAsync(page);

        Assert.Multiple(() =>
        {
            Assert.That(page.ShowTwitchChatDisplayOption, Is.True);
            Assert.That(page.CanSelectTwitchChatDisplayAccount, Is.False);
            Assert.That(page.ShowSingleTwitchChatDisplayNotice, Is.True);
            Assert.That(page.SingleTwitchChatDisplayText, Is.EqualTo("Чат работает от имени бота (@twbot)"));
            Assert.That(page.ChatDisplayAccount, Is.EqualTo(PlatformAuthRole.Bot));
            Assert.That(botOnlyContext.Settings.Twitch.ChatDisplayAccount, Is.EqualTo(PlatformAuthRole.Bot));
        });

        var bothRolesContext = new OnboardingContext(
            new AppSettings
            {
                Twitch = new TwitchSettings
                {
                    Enabled = true,
                    ClientId = "tw-cid",
                    ClientSecret = "tw-sec",
                    Channel = "streamer",
                    ChatDisplayAccount = PlatformAuthRole.Broadcaster,
                },
            },
            CreateValidBotAccount(),
            new TwitchAccountSettings
            {
                Login = "streamer",
                UserId = "42",
                AccessToken = "str-token",
                RefreshToken = "str-refresh",
                Scopes = [.. TwitchScopes.BroadcasterRequired],
                StoredScopes = [.. TwitchScopes.BroadcasterRequired],
            });
        accountsStore.SaveAll(bothRolesContext.BotAccount, bothRolesContext.BroadcasterAccount);

        page.OnEnter(bothRolesContext);
        await WaitForValidationAsync(page);

        Assert.Multiple(() =>
        {
            Assert.That(page.ShowTwitchChatDisplayOption, Is.True);
            Assert.That(page.CanSelectTwitchChatDisplayAccount, Is.True);
            Assert.That(page.ShowSingleTwitchChatDisplayNotice, Is.False);
            Assert.That(page.TwitchBotChatDisplayLabel, Is.EqualTo("Аккаунт бота (@twbot)"));
            Assert.That(page.TwitchBroadcasterChatDisplayLabel, Is.EqualTo("Аккаунт стримера (@streamer)"));
            Assert.That(page.ChatDisplayAccount, Is.EqualTo(PlatformAuthRole.Broadcaster));
        });
    }

    private static OnboardingContext CreateValidTwitchContext() => new(
        new AppSettings
        {
            Twitch = new TwitchSettings { Enabled = true, ClientId = "tw-cid", ClientSecret = "tw-sec", Channel = "mychan" },
        },
        CreateValidBotAccount(),
        new());

    private static TwitchAccountSettings CreateValidBotAccount() => new()
    {
        Login = "twbot",
        UserId = "1",
        AccessToken = "bot-token",
        RefreshToken = "bot-refresh",
        Scopes = [.. TwitchScopes.BotRequired],
        StoredScopes = [.. TwitchScopes.BotRequired],
    };

    private static YouTubeAccountSettings CreateValidYouTubeBotAccount() => new()
    {
        ChannelId = "UC_BOT",
        ChannelTitle = "BotYT",
        AccessToken = "yt-token",
        RefreshToken = "yt-refresh",
        Scopes = [.. YouTubeScopes.BotRequired],
        StoredScopes = [.. YouTubeScopes.BotRequired],
    };

    private static async Task WaitForValidationAsync(CompletionPageViewModel page)
    {
        if (!page.IsValidationPending)
        {
            return;
        }

        var tcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        page.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName == nameof(CompletionPageViewModel.IsValidationPending) && !page.IsValidationPending)
            {
                tcs.TrySetResult();
            }
        };

        if (!page.IsValidationPending)
        {
            return;
        }

        await tcs.Task.WaitAsync(TimeSpan.FromSeconds(5));
    }

    private static Task PublishBotChatAsync(IEventBus bus, StreamingPlatform platform, UserStatus status)
    {
        var entry = new ChatMessageData
        {
            Platform = platform,
            Channel = "chan",
            MessageId = Guid.NewGuid().ToString("N"),
            UserId = "bot-id",
            DisplayName = "bot",
            Message = "hello",
            IsBot = true,
        };
        return bus.PublishAsync(new ChatMessageReceived(platform, "chan", entry.MessageId, "bot-id", "bot", "bot", "hello", status, false, entry, IsBot: true));
    }

    private static void EnsureApplicationResources()
    {
        if (Application.Current is not null)
        {
            return;
        }

        var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        app.Resources.MergedDictionaries.Add(new ResourceDictionary
        {
            Source = new Uri("pack://application:,,,/KeepShell;component/Resources/Themes/Light.xaml", UriKind.Absolute),
        });
        app.Resources.MergedDictionaries.Add(new ResourceDictionary
        {
            Source = new Uri("pack://application:,,,/KeepShell;component/Resources/Themes/Tokens.xaml", UriKind.Absolute),
        });
        app.Resources.MergedDictionaries.Add(new ResourceDictionary
        {
            Source = new Uri("pack://application:,,,/KeepShell;component/Resources/Converters.xaml", UriKind.Absolute),
        });
        app.Resources.MergedDictionaries.Add(new ResourceDictionary
        {
            Source = new Uri("pack://application:,,,/KeepShell;component/Resources/Styles/Controls.xaml", UriKind.Absolute),
        });
        app.Resources.MergedDictionaries.Add(new ResourceDictionary
        {
            Source = new Uri("pack://application:,,,/PoproshaykaBot.Wpf;component/Infrastructure/Converters/AppConverters.xaml", UriKind.Absolute),
        });
    }

    private static T? FindVisualChild<T>(DependencyObject root) where T : DependencyObject
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is T typed)
            {
                return typed;
            }

            if (FindVisualChild<T>(child) is { } nested)
            {
                return nested;
            }
        }

        return null;
    }

    private static ServiceProvider BuildProvider()
    {
        var services = new ServiceCollection();
        services
            .AddCoreInfrastructure(new UiLogSink(), disposeSerilog: false)
            .AddStatistics()
            .AddSettingsStores()
            .AddDebugChannel(DebugChannelOverride.None)
            .AddTwitchClients()
            .AddGameBoxArt(cacheOnly: true)
            .AddUserAvatars(cacheOnly: true)
            .AddChatPipeline()
            .AddStreamMonitoring()
            .AddBroadcasting()
            .AddPolls()
            .AddHttpServer()
            .AddObsIntegration()
            .AddSelfUpdate()
            .AddDiagnostics();
        services.AddSingleton<BotConnectionManager>();
        services.AddSingleton<IBotConnectionController>(sp => sp.GetRequiredService<BotConnectionManager>());

        return services.BuildServiceProvider(new ServiceProviderOptions
        {
            ValidateOnBuild = true,
            ValidateScopes = true,
        });
    }

    private sealed class StubConnectionController : IBotConnectionController
    {
        private readonly Dictionary<StreamingPlatform, BotLifecyclePhase> _phases = [];

        public List<StreamingPlatform> StartedPlatforms { get; } = [];
        public int StartAllCount { get; private set; }
        public int StopCount { get; private set; }
        public bool IsBusy => false;
        public BotLifecyclePhase CurrentPhase { get; set; } = BotLifecyclePhase.Idle;

        public void SetPlatformPhase(StreamingPlatform platform, BotLifecyclePhase phase) => _phases[platform] = phase;
        public BotLifecyclePhase GetPhase(StreamingPlatform platform) => _phases.GetValueOrDefault(platform, BotLifecyclePhase.Idle);
        public bool IsPlatformBusy(StreamingPlatform platform) => false;
        public void StartConnection() => StartAllCount++;
        public void StartConnection(StreamingPlatform platform) => StartedPlatforms.Add(platform);
        public void CancelConnection() { }
        public void CancelConnection(StreamingPlatform platform) { }
        public Task WaitForConnectionAsync() => Task.CompletedTask;
        public Task WaitForConnectionAsync(StreamingPlatform platform) => Task.CompletedTask;
        public Task StopAsync(BotStopMode mode = BotStopMode.Graceful)
        {
            StopCount++;
            return Task.CompletedTask;
        }
        public Task StopAsync(StreamingPlatform platform, BotStopMode mode = BotStopMode.Graceful) => Task.CompletedTask;
    }

    private sealed class StubRegistry(params IChatPlatform[] platforms) : IChatPlatformRegistry
    {
        public IReadOnlyList<IChatPlatform> All { get; } = platforms;
        public bool TryGet(StreamingPlatform platform, [System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out IChatPlatform? chatPlatform)
        {
            chatPlatform = Find(platform);
            return chatPlatform is not null;
        }
        public IChatPlatform? Find(StreamingPlatform platform) => All.FirstOrDefault(p => p.Id == platform);
        public IChatPlatform GetRequired(StreamingPlatform platform) => Find(platform) ?? throw new KeyNotFoundException();
        public IReadOnlyList<IChatPlatform> GetEnabled() => All.Where(p => p.IsEnabled).ToArray();
        public IReadOnlyList<IChatPlatform> GetConnected() => All.Where(p => p.IsConnected).ToArray();
    }

    private sealed class StubPlatform(StreamingPlatform id, string displayName, bool isEnabled, bool isConnected, string channel) : IChatPlatform
    {
        public StreamingPlatform Id => id;
        public string DisplayName => displayName;
        public PlatformCapabilities Capabilities => PlatformCapabilities.None;
        public bool IsEnabled => isEnabled;
        public bool IsConnected => isConnected;
        public IStreamStatus StreamStatus { get; } = new StubStreamStatus();
        public IPlatformMessenger Messenger => null!;
        public string? ConfiguredChannel => channel;
        public string? BuildStreamUrl(StreamInfo? stream = null) => null;
    }

    private sealed class StubStreamStatus : IStreamStatus
    {
        public StreamStatus CurrentStatus => StreamStatus.Offline;
        public StreamInfo? CurrentStream => null;
        public StreamStatusSnapshot GetSnapshot() => new(StreamStatus.Offline, null, null);
        public Task RefreshLiveSnapshotAsync() => Task.CompletedTask;
    }

    private sealed class StubChatMessenger : IBroadcastMessenger
    {
        public ChatEnqueueResult Outcome { get; set; } = ChatEnqueueResult.Queued;
        public List<string> Sent { get; } = [];
        public ChatEnqueueResult Send(StreamingPlatform platform, string text, ChatSendCategory category = ChatSendCategory.None)
        {
            Sent.Add(text);
            return Outcome;
        }
        public BroadcastSendResult Send(string text, IReadOnlySet<StreamingPlatform> platforms, ChatSendCategory category = ChatSendCategory.None)
            => new(new Dictionary<StreamingPlatform, ChatEnqueueResult>());
        public BroadcastSendResult SendToConnected(string text, ChatSendCategory category = ChatSendCategory.None)
            => new(new Dictionary<StreamingPlatform, ChatEnqueueResult>());
        public BroadcastSendResult Send(Func<IChatPlatform, string?> messageFactory, IReadOnlySet<StreamingPlatform>? platforms = null, ChatSendCategory category = ChatSendCategory.None)
            => new(new Dictionary<StreamingPlatform, ChatEnqueueResult>());
    }

    private sealed class RecordingClipboard : IClipboardService
    {
        public string? LastText { get; private set; }
        public bool TrySetText(string? text)
        {
            LastText = text;
            return true;
        }
    }

    private sealed class StubLauncher : IShellLauncher
    {
        public bool Open(string pathOrUrl) => true;
        public bool Reveal(string path) => true;
        public bool Start(string executable, params string[] arguments) => true;
    }

    private sealed class StubChannelValidator(Task<ChannelValidationResult>? pending = null) : IOnboardingChannelValidator
    {
        public Task<ChannelValidationResult> ValidateAsync(string login, string clientId, string accessToken, CancellationToken cancellationToken)
            => pending ?? Task.FromResult(ChannelValidationResult.Found);
        public Task<ChannelValidationResult> ValidateWithAppTokenAsync(string login, string clientId, string clientSecret, CancellationToken cancellationToken)
            => pending ?? Task.FromResult(ChannelValidationResult.Found);
    }

    private sealed class StubCredentialsValidator : IClientCredentialsValidator
    {
        public Task<ClientCredentialsValidationResult> ValidateAsync(string clientId, string clientSecret, CancellationToken cancellationToken)
            => Task.FromResult(ClientCredentialsValidationResult.Valid);
    }

    private sealed class StubTwitchOAuthService : ITwitchOAuthService
    {
        public event Action<PlatformAuthRole, string>? StatusChanged { add { } remove { } }
        public Task<OAuthFlowResult> StartOAuthFlowToDraftAsync(PlatformAuthRole role, string clientId, string clientSecret, string[]? scopes, string? redirectUri, Action<string> onAuthUrlReady, bool checkBroadcasterChannel = true, CancellationToken ct = default)
            => Task.FromResult(new OAuthFlowResult("at", "rt", [], "user", "1", 3600));
        public Task<string> StartOAuthFlowAsync(PlatformAuthRole role, string clientId, string clientSecret, string[]? scopes, string? redirectUri, Action<string> onAuthUrlReady, bool checkBroadcasterChannel = true, CancellationToken ct = default)
            => throw new NotSupportedException();
        public void SetAuthResult(string code, string? state) { }
        public void SetAuthError(Exception exception) { }
        public Task<bool> IsTokenValidAsync(string token, CancellationToken ct = default) => Task.FromResult(true);
        public Task<TokenValidationInfo?> ValidateAsync(string token, CancellationToken ct = default) => Task.FromResult<TokenValidationInfo?>(null);
        public Task<string> RefreshTokenAsync(PlatformAuthRole role, string clientId, string clientSecret, string refreshToken, CancellationToken ct = default) => Task.FromResult("at");
        public Task<string?> GetAccessTokenAsync(PlatformAuthRole role, CancellationToken ct = default) => Task.FromResult<string?>(null);
        public void UpdateSettings(PlatformAuthRole role, string accessToken, string refreshToken, IEnumerable<string>? newScopes, string login, string userId, int expiresInSeconds = 0, bool publishAuthorizationRefreshed = false) { }
        public void ClearTokens(PlatformAuthRole role) { }
    }

    private sealed class StubEmbeddedTwitchAuthDialog(EmbeddedTwitchAuthResult result) : IEmbeddedTwitchAuthDialog
    {
        public Task<EmbeddedTwitchAuthResult> AuthorizeAsync(EmbeddedTwitchAuthRequest request, CancellationToken cancellationToken = default)
            => Task.FromResult(result);
    }

    private sealed class StubDialogService : IDialogService
    {
        public Task<bool> ShowAsync(IDialogViewModel viewModel) => Task.FromResult(false);
        public Task<bool> ReplaceAsync(IDialogViewModel viewModel) => Task.FromResult(false);
        public bool Confirm(string title, string message, bool defaultYes = false) => true;
        public bool ConfirmWarning(string title, string message, bool defaultYes = false) => true;
        public void Info(string title, string message) { }
        public void Warning(string title, string message) { }
        public void Error(string title, string message) { }
    }

    private sealed class StubPresentationSource(Visual rootVisual) : PresentationSource
    {
        public override Visual RootVisual { get; set; } = rootVisual;
        public override bool IsDisposed => false;
        protected override CompositionTarget GetCompositionTargetCore() => null!;
    }
}
