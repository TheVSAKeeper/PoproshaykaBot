using System.IO;
using System.Net.Http;
using System.Reflection;
using KeepShell.Services;
using KeepShell.Services.Modal;
using KeepShell.Services.Platform;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using PoproshaykaBot.Core.Broadcast;
using PoproshaykaBot.Core.Chat;
using PoproshaykaBot.Core.Debugging;
using PoproshaykaBot.Core.Diagnostics;
using PoproshaykaBot.Core.Infrastructure.Di;
using PoproshaykaBot.Core.Infrastructure.Events;
using PoproshaykaBot.Core.Infrastructure.Hosting;
using PoproshaykaBot.Core.Infrastructure.Logging;
using PoproshaykaBot.Core.Obs;
using PoproshaykaBot.Core.Platforms;
using PoproshaykaBot.Core.Platforms.Auth;
using PoproshaykaBot.Core.Polls;
using PoproshaykaBot.Core.Server;
using PoproshaykaBot.Core.Settings;
using PoproshaykaBot.Core.Settings.Stores;
using PoproshaykaBot.Core.Statistics;
using PoproshaykaBot.Core.Streaming;
using PoproshaykaBot.Core.Twitch;
using PoproshaykaBot.Core.Twitch.Auth;
using PoproshaykaBot.Core.Twitch.Chat;
using PoproshaykaBot.Core.Twitch.GameArt;
using PoproshaykaBot.Core.Twitch.Onboarding;
using PoproshaykaBot.Core.Update;
using PoproshaykaBot.Core.Users.Avatars;
using PoproshaykaBot.Core.YouTube;
using PoproshaykaBot.Core.YouTube.Auth;
using PoproshaykaBot.Wpf.Bootstrap;
using PoproshaykaBot.Wpf.Infrastructure;
using PoproshaykaBot.Wpf.Tests.Support;
using PoproshaykaBot.Wpf.ViewModels.Onboarding;
using PoproshaykaBot.Wpf.ViewModels.Settings;

namespace PoproshaykaBot.Wpf.Tests.Onboarding;

[TestFixture]
public sealed class Stage1DomainOnboardingTests
{
    private string _tempDir = string.Empty;

    [SetUp]
    public void SetUp()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"stage1-onboard-{Guid.NewGuid():N}");
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
    public async Task BroadcasterAuthorizationPage_FreshProfileWithEmptyChannel_CannotSkipAndAutoDetectsOnLogin()
    {
        var accountsStore = new AccountsStore(filePath: Path.Combine(_tempDir, "accounts.json"));
        var flow = new OAuthFlowResult(
            "str-access",
            "str-refresh",
            [.. TwitchScopes.BroadcasterRequired],
            "detected_streamer",
            "777",
            3600);
        using var page = new BroadcasterAuthorizationPageViewModel(
            DispatchProxy.Create<ITwitchOAuthService, InertProxy>(),
            accountsStore,
            NullLogger<BroadcasterAuthorizationPageViewModel>.Instance,
            new StubLauncher(),
            new StubClipboardService(),
            new StubEmbeddedTwitchAuth(flow),
            new ManualUiDispatcher());

        var freshSettings = new AppSettings
        {
            Twitch = new TwitchSettings
            {
                ClientId = "tw-id",
                ClientSecret = "tw-secret",
            },
        };
        var context = new OnboardingContext(freshSettings, new(), new());

        page.OnEnter(context);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(freshSettings.Twitch.Channel, Is.Empty);
            Assert.That(context.HasConfiguredTwitchChannel, Is.False);
            Assert.That(page.CanAdvance, Is.False);
        }

        await page.AuthorizeInAppCommand.ExecuteAsync(null);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(page.CanAdvance, Is.True);
            Assert.That(context.Settings.Twitch.Channel, Is.EqualTo("detected_streamer"));
        }
    }

    [Test]
    public void YouTubeOnboardingPage_RejectsReadOnlyOrExpiredBotAndAcceptsUsableForceSsl()
    {
        using var editor = CreateYouTubeEditor();
        using var page = new YouTubeOnboardingPageViewModel(editor);
        var context = new OnboardingContext(
            new AppSettings { Twitch = new TwitchSettings { Enabled = false } },
            new(),
            new(),
            new YouTubeSettings { Enabled = true, ClientId = "yt-client", Channel = "UC1234567890123456789012" });

        page.OnEnter(context);

        editor.Bot.Load(new YouTubeAccountSettings
        {
            ChannelId = "UC_BOT",
            AccessToken = "at",
            RefreshToken = "rt",
            Scopes = [YouTubeScopes.ReadOnly],
            StoredScopes = [YouTubeScopes.ReadOnly],
        });
        using (Assert.EnterMultipleScope())
        {
            Assert.That(page.CanAdvance, Is.False);
            Assert.That(page.ValidationHint, Does.Contain("youtube.force-ssl"));
        }

        editor.Bot.Load(new YouTubeAccountSettings
        {
            ChannelId = "UC_BOT",
            AccessToken = "at",
            RefreshToken = string.Empty,
            AccessTokenExpiresAt = DateTimeOffset.UtcNow.AddMinutes(-5),
            Scopes = [YouTubeScopes.ForceSsl],
            StoredScopes = [YouTubeScopes.ForceSsl],
        });
        using (Assert.EnterMultipleScope())
        {
            Assert.That(page.CanAdvance, Is.False);
            Assert.That(page.ValidationHint, Does.Contain("истёк"));
        }

        editor.Bot.Load(new YouTubeAccountSettings
        {
            ChannelId = "UC_BOT",
            AccessToken = "at",
            RefreshToken = "rt",
            Scopes = [YouTubeScopes.ForceSsl],
            StoredScopes = [YouTubeScopes.ForceSsl],
        });
        Assert.That(page.CanAdvance, Is.True);
    }

    [Test]
    public void PlatformsPage_ShowsNeedsSetupWhenYouTubeBotHasOnlyReadOnly()
    {
        var page = new PlatformsPageViewModel();
        var context = new OnboardingContext(
            new AppSettings { Twitch = new TwitchSettings { Enabled = false } },
            new(),
            new(),
            new YouTubeSettings { Enabled = true, ClientId = "yt-id", Channel = "UC1234567890123456789012" },
            youTubeBotAccount: new YouTubeAccountSettings
            {
                AccessToken = "at",
                RefreshToken = "rt",
                StoredScopes = [YouTubeScopes.ReadOnly],
            });

        page.OnEnter(context);

        Assert.That(page.YouTubeStatusText, Is.EqualTo("Требует донастройки"));
    }

    [Test]
    public async Task Wizard_ReconfigureYouTubeWithNamedContexts_LoadsSavesAndRollsBackAssignedContexts()
    {
        await using var provider = BuildProvider();
        var settingsManager = new SettingsManager(NullLogger<SettingsManager>.Instance, Path.Combine(_tempDir, "settings.json"));
        settingsManager.SaveSettings(new AppSettings { Twitch = new TwitchSettings { Enabled = false } });
        var accountsStore = new AccountsStore(filePath: Path.Combine(_tempDir, "accounts.json"));
        var ytSettingsStore = new YouTubeSettingsStore(filePath: Path.Combine(_tempDir, "youtube.json"));
        ytSettingsStore.Save(new YouTubeSettings
        {
            Enabled = true,
            ClientId = "yt-client",
            Channel = "UC1234567890123456789012",
        });

        var ytAccountsStore = new YouTubeAccountsStore(filePath: Path.Combine(_tempDir, "youtube-accounts.json"));
        ytAccountsStore.SaveAll("orig-secret",
            new() { ChannelId = "UC_DEF_BOT", RefreshToken = "def-bot-rt", StoredScopes = [YouTubeScopes.ForceSsl] },
            new() { ChannelId = "UC_DEF_STR", RefreshToken = "def-str-rt", StoredScopes = [YouTubeScopes.ForceSsl] });
        ytAccountsStore.UpsertContext(PlatformAuthRole.Bot, "yt-bot-2", "Бот 2",
            new() { ChannelId = "UC_NAMED_BOT", ChannelTitle = "NamedBot", RefreshToken = "named-bot-rt-orig", StoredScopes = [YouTubeScopes.ForceSsl] });
        ytAccountsStore.UpsertContext(PlatformAuthRole.Broadcaster, "yt-str-2", "Стример 2",
            new() { ChannelId = "UC1234567890123456789012", ChannelTitle = "NamedStreamer", RefreshToken = "named-str-rt-orig", StoredScopes = [YouTubeScopes.ReadOnly] });

        var catalog = new ChannelCatalogStore(filePath: Path.Combine(_tempDir, "channels.json"));
        catalog.Save(new()
        {
            Channels =
            [
                new ChannelCatalogEntry
                {
                    Platform = StreamingPlatform.YouTube,
                    ChannelId = "UC1234567890123456789012",
                    DisplayName = "NamedStreamer",
                    Handle = "@named",
                    IsConfigured = true,
                    BotContextId = "yt-bot-2",
                    BotAccountId = "UC_NAMED_BOT",
                    BroadcasterContextId = "yt-str-2",
                    BroadcasterAccountId = "UC1234567890123456789012",
                },
            ],
        });

        var bus = provider.GetRequiredService<IEventBus>();
        var refreshedEvents = new List<PlatformAuthorizationRefreshed>();
        using var sub = bus.Subscribe<PlatformAuthorizationRefreshed>(refreshedEvents.Add);

        using var ytEditor = CreateYouTubeEditor();
        using var ytPage = new YouTubeOnboardingPageViewModel(ytEditor);
        var mutatingPage = new YouTubeContextMutatingPage(settingsManager, accountsStore, ytSettingsStore, ytAccountsStore);

        using var wizard = new OnboardingWizardViewModel(
            [ytPage, mutatingPage],
            settingsManager,
            accountsStore,
            provider.GetRequiredService<KestrelHttpServer>(),
            provider.GetRequiredService<BotConnectionManager>(),
            bus,
            NullLogger<OnboardingWizardViewModel>.Instance,
            ytSettingsStore,
            ytAccountsStore,
            catalog,
            new StubDialogService());

        using (Assert.EnterMultipleScope())
        {
            Assert.That(ytEditor.Bot.ChannelId, Is.EqualTo("UC_NAMED_BOT"));
            Assert.That(ytEditor.Broadcaster.ChannelId, Is.EqualTo("UC1234567890123456789012"));
            Assert.That(ytEditor.Bot.RefreshTokenValue, Is.EqualTo("named-bot-rt-orig"));
        }

        await wizard.NextCommand.ExecuteAsync(null);
        Assert.That(wizard.CurrentPage, Is.SameAs(mutatingPage));
        Assert.That(mutatingPage.SaveError, Is.Null);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(ytAccountsStore.Load(PlatformAuthRole.Bot, "yt-bot-2").RefreshToken, Is.EqualTo("named-bot-rt-new"));
            Assert.That(ytAccountsStore.LoadBot().RefreshToken, Is.EqualTo("def-bot-rt"));
            Assert.That(ytAccountsStore.LoadBroadcaster().RefreshToken, Is.EqualTo("def-str-rt"));
        }

        Assert.That(await wizard.RequestCloseAsync(), Is.True);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(ytAccountsStore.Load(PlatformAuthRole.Bot, "yt-bot-2").RefreshToken, Is.EqualTo("named-bot-rt-orig"));
            Assert.That(ytAccountsStore.Load(PlatformAuthRole.Broadcaster, "yt-str-2").RefreshToken, Is.EqualTo("named-str-rt-orig"));
            Assert.That(ytAccountsStore.LoadBot().RefreshToken, Is.EqualTo("def-bot-rt"));
            Assert.That(refreshedEvents.Any(e =>
                e.Platform == StreamingPlatform.YouTube && e.Role == PlatformAuthRole.Bot && e.ContextId == "yt-bot-2"), Is.True);
            Assert.That(refreshedEvents.Any(e =>
                e.Platform == StreamingPlatform.YouTube && e.Role == PlatformAuthRole.Broadcaster && e.ContextId == "yt-str-2"), Is.True);
        }
    }

    [Test]
    public async Task CompletionPage_YouTubeReadOnlyBroadcaster_WarnsAndAllowsCompletion()
    {
        await using var provider = BuildProvider();
        var settingsManager = new SettingsManager(NullLogger<SettingsManager>.Instance, Path.Combine(_tempDir, "settings.json"));
        var accountsStore = new AccountsStore(filePath: Path.Combine(_tempDir, "accounts.json"));
        var ytSettingsStore = new YouTubeSettingsStore(filePath: Path.Combine(_tempDir, "youtube.json"));
        var ytAccountsStore = new YouTubeAccountsStore(filePath: Path.Combine(_tempDir, "youtube-accounts.json"));
        var bus = provider.GetRequiredService<IEventBus>();

        using var page = new CompletionPageViewModel(
            settingsManager,
            accountsStore,
            bus,
            provider.GetRequiredService<KestrelHttpServer>(),
            new StubChannelValidator(),
            provider.GetRequiredService<BotConnectionManager>(),
            NullLogger<CompletionPageViewModel>.Instance,
            new StubDialogService(),
            ytSettingsStore,
            ytAccountsStore);

        var context = new OnboardingContext(
            new AppSettings { Twitch = new TwitchSettings { Enabled = false } },
            new(),
            new(),
            new YouTubeSettings { Enabled = true, ClientId = "yt-client", Channel = "UC1234567890123456789012" },
            "yt-secret",
            new YouTubeAccountSettings
            {
                ChannelId = "UC_BOT_0000000000000001",
                ChannelTitle = "BotYT",
                AccessToken = "yt-bot-token",
                RefreshToken = "yt-bot-refresh",
                Scopes = [.. YouTubeScopes.BotRequired],
                StoredScopes = [.. YouTubeScopes.BotRequired],
            },
            new YouTubeAccountSettings
            {
                ChannelId = "UC1234567890123456789012",
                ChannelTitle = "StreamerYT",
                AccessToken = "yt-str-token",
                RefreshToken = "yt-str-refresh",
                Scopes = [YouTubeScopes.ReadOnly],
                StoredScopes = [YouTubeScopes.ReadOnly],
            });

        var doneTcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        page.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName == nameof(CompletionPageViewModel.IsValidationPending) && !page.IsValidationPending)
            {
                doneTcs.TrySetResult();
            }
        };

        page.AutoConnect = false;
        page.OnEnter(context);
        if (page.IsValidationPending)
        {
            await doneTcs.Task.WaitAsync(TimeSpan.FromSeconds(5));
        }

        using (Assert.EnterMultipleScope())
        {
            Assert.That(page.IsValidationFailure, Is.False);
            Assert.That(page.IsValidationWarning, Is.True);
            Assert.That(page.CanAdvance, Is.True);
            Assert.That(page.ValidationText, Does.Contain("youtube.readonly"));
            Assert.That(page.ValidationText, Does.Contain("youtube.force-ssl"));
        }

        Assert.That(await page.OnLeavingAsync(context), Is.True);
    }

    [Test]
    public async Task YouTubeOnboardingPage_ResolvesHandleAndPersistsActiveCatalogChannel()
    {
        var settingsManager = new SettingsManager(NullLogger<SettingsManager>.Instance, Path.Combine(_tempDir, "settings.json"));
        var accountsStore = new AccountsStore(filePath: Path.Combine(_tempDir, "accounts.json"));
        var ytSettingsStore = new YouTubeSettingsStore(filePath: Path.Combine(_tempDir, "youtube.json"));
        var ytAccountsStore = new YouTubeAccountsStore(filePath: Path.Combine(_tempDir, "youtube-accounts.json"));
        var catalog = new ChannelCatalogStore(filePath: Path.Combine(_tempDir, "channels.json"));

        using var editor = CreateYouTubeEditor();
        var identityClient = new StubIdentityClient((_, _) =>
            Task.FromResult<YouTubeChannelIdentity?>(new YouTubeChannelIdentity("UC_RESOLVED_000000000001", "Fixture Channel")));
        using var page = new YouTubeOnboardingPageViewModel(editor, identityClient);

        var context = new OnboardingContext(
            new AppSettings { Twitch = new TwitchSettings { Enabled = false } },
            new(),
            new(),
            ytSettingsStore.Load(),
            ytAccountsStore.LoadClientSecret(),
            ytAccountsStore.LoadBot(),
            ytAccountsStore.LoadBroadcaster())
        {
            TwitchCatalog = catalog,
        };
        context.YouTubeSettings.Enabled = true;
        context.YouTubeSettings.ClientId = "yt-client";
        context.YouTubeBotAccount = new YouTubeAccountSettings
        {
            ChannelId = "UC_BOT_0000000000000001",
            ChannelTitle = "BotYT",
            AccessToken = "live-bot-token",
            RefreshToken = "bot-refresh",
            AccessTokenExpiresAt = DateTimeOffset.UtcNow.AddHours(1),
            Scopes = [.. YouTubeScopes.BotRequired],
            StoredScopes = [.. YouTubeScopes.BotRequired],
        };

        page.OnEnter(context);
        editor.Channel = "@fixture";

        Assert.That(await page.OnLeavingAsync(context), Is.True);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(context.YouTubeResolvedChannel, Is.Not.Null);
            Assert.That(context.YouTubeResolvedChannel!.ChannelId, Is.EqualTo("UC_RESOLVED_000000000001"));
            Assert.That(context.YouTubeResolvedChannel.DisplayName, Is.EqualTo("Fixture Channel"));
            Assert.That(context.SaveSetup(settingsManager, accountsStore, ytSettingsStore, ytAccountsStore), Is.Null);
        }

        var active = catalog.GetActiveChannel(StreamingPlatform.YouTube);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(active, Is.Not.Null);
            Assert.That(active!.ChannelId, Is.EqualTo("UC_RESOLVED_000000000001"));
            Assert.That(active.Handle, Is.EqualTo("@fixture"));
            Assert.That(active.DisplayName, Is.EqualTo("Fixture Channel"));
        }
    }

    [TestCase("not-found")]
    [TestCase("network")]
    [TestCase("no-live-token")]
    public async Task YouTubeOnboardingPage_HandleResolutionFailure_StaysOnStepWithHint(string mode)
    {
        using var editor = CreateYouTubeEditor();
        var identityClient = new StubIdentityClient((_, _) => mode switch
        {
            "not-found" => Task.FromResult<YouTubeChannelIdentity?>(null),
            "network" => Task.FromException<YouTubeChannelIdentity?>(new HttpRequestException("offline")),
            _ => Task.FromResult<YouTubeChannelIdentity?>(null),
        });
        using var page = new YouTubeOnboardingPageViewModel(editor, identityClient);

        var context = new OnboardingContext(
            new AppSettings { Twitch = new TwitchSettings { Enabled = false } },
            new(),
            new(),
            new YouTubeSettings { Enabled = true, ClientId = "yt-client" },
            youTubeBotAccount: new YouTubeAccountSettings
            {
                ChannelId = "UC_BOT_0000000000000001",
                AccessToken = mode == "no-live-token" ? string.Empty : "live-bot-token",
                RefreshToken = "bot-refresh",
                AccessTokenExpiresAt = mode == "no-live-token" ? null : DateTimeOffset.UtcNow.AddHours(1),
                Scopes = [.. YouTubeScopes.BotRequired],
                StoredScopes = [.. YouTubeScopes.BotRequired],
            });

        page.OnEnter(context);
        editor.Channel = "@fixture";

        Assert.That(await page.OnLeavingAsync(context), Is.False);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(page.HasValidationHint, Is.True);
            Assert.That(page.ValidationHint, Does.Contain("UC"));
            Assert.That(context.YouTubeResolvedChannel, Is.Null);
        }

        editor.Channel = "https://youtube.com/channel/UC1234567890123456789012";
        Assert.That(page.HasValidationHint, Is.False);
        Assert.That(await page.OnLeavingAsync(context), Is.True);
    }

    [TestCase(true)]
    [TestCase(false)]
    public async Task YouTubeOnboardingPage_HandleResolutionTimeout_DoesNotThrowAndShowsNetworkHint(bool useAdapterFallback)
    {
        using var editor = CreateYouTubeEditor();
        CancellationToken observedToken = default;
        IYouTubeChannelIdentityClient? identityClient = useAdapterFallback
            ? null
            : new StubIdentityClient((_, _, ct) =>
            {
                observedToken = ct;
                return Task.FromException<YouTubeChannelIdentity?>(new TaskCanceledException("timeout"));
            });
        IPlatformChannelAdapter[]? adapters = useAdapterFallback
            ? [new TimeoutYouTubeAdapter(ct => observedToken = ct)]
            : null;

        using var page = new YouTubeOnboardingPageViewModel(editor, identityClient, adapters);
        var context = new OnboardingContext(
            new AppSettings { Twitch = new TwitchSettings { Enabled = false } },
            new(),
            new(),
            new YouTubeSettings { Enabled = true, ClientId = "yt-client" },
            youTubeBotAccount: new YouTubeAccountSettings
            {
                ChannelId = "UC_BOT_0000000000000001",
                AccessToken = "live-bot-token",
                RefreshToken = "bot-refresh",
                AccessTokenExpiresAt = DateTimeOffset.UtcNow.AddHours(1),
                Scopes = [.. YouTubeScopes.BotRequired],
                StoredScopes = [.. YouTubeScopes.BotRequired],
            });

        page.OnEnter(context);
        editor.Channel = "@fixture";

        var left = await page.OnLeavingAsync(context);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(left, Is.False);
            Assert.That(observedToken.CanBeCanceled, Is.True);
            Assert.That(page.HasValidationHint, Is.True);
            Assert.That(page.ValidationHint, Does.Contain("сеть"));
            Assert.That(page.ValidationHint, Does.Contain("UC-ID"));
            Assert.That(context.YouTubeResolvedChannel, Is.Null);
        }
    }

    [Test]
    public void OnboardingContext_RestoreProvisionalWrites_RollsBackSecretAndBotWhenBroadcasterUpdatedExternally()
    {
        var settingsManager = new SettingsManager(NullLogger<SettingsManager>.Instance, Path.Combine(_tempDir, "settings.json"));
        var accountsStore = new AccountsStore(filePath: Path.Combine(_tempDir, "accounts.json"));
        var ytSettingsStore = new YouTubeSettingsStore(filePath: Path.Combine(_tempDir, "youtube.json"));
        var ytAccountsStore = new YouTubeAccountsStore(filePath: Path.Combine(_tempDir, "youtube-accounts.json"));

        var origBot = new YouTubeAccountSettings
        {
            ChannelId = "UC_BOT",
            RefreshToken = "orig-bot-rt",
            StoredScopes = [YouTubeScopes.ForceSsl],
        };
        var origBroadcaster = new YouTubeAccountSettings
        {
            ChannelId = "UC_STR",
            RefreshToken = "orig-str-rt",
            StoredScopes = [YouTubeScopes.ForceSsl],
        };
        ytAccountsStore.SaveAll("orig-secret", origBot, origBroadcaster);

        var context = new OnboardingContext(
            new AppSettings { Twitch = new TwitchSettings { Enabled = false } },
            new(),
            new(),
            ytSettingsStore.Load(),
            ytAccountsStore.LoadClientSecret(),
            ytAccountsStore.LoadBot(),
            ytAccountsStore.LoadBroadcaster());

        context.YouTubeClientSecret = "wizard-secret";
        context.YouTubeBotAccount.RefreshToken = "wizard-bot-rt";
        context.YouTubeBroadcasterAccount.RefreshToken = "wizard-str-rt";

        Assert.That(context.SaveSetup(settingsManager, accountsStore, ytSettingsStore, ytAccountsStore), Is.Null);

        ytAccountsStore.Mutate(PlatformAuthRole.Broadcaster, account => account.RefreshToken = "external-str-rt");

        context.RestoreProvisionalWrites();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(ytAccountsStore.LoadClientSecret(), Is.EqualTo("orig-secret"));
            Assert.That(ytAccountsStore.LoadBot().RefreshToken, Is.EqualTo("orig-bot-rt"));
            Assert.That(ytAccountsStore.LoadBroadcaster().RefreshToken, Is.EqualTo("external-str-rt"));
        }
    }

    private static YouTubeSettingsSectionViewModel CreateYouTubeEditor() =>
        new(
            new StubYouTubeOAuthService(),
            new StubLauncher(),
            new StubDialogService(),
            new ManualUiDispatcher(),
            TimeProvider.System,
            NullLoggerFactory.Instance);

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

    private sealed class YouTubeContextMutatingPage(
        SettingsManager settingsManager,
        AccountsStore accountsStore,
        YouTubeSettingsStore ytSettingsStore,
        YouTubeAccountsStore ytAccountsStore) : OnboardingPageViewModelBase
    {
        public override string PageTitle => "Проверка контекстов";

        public string? SaveError { get; private set; }

        public override void OnEnter(OnboardingContext context)
        {
            context.YouTubeBotAccount.RefreshToken = "named-bot-rt-new";
            context.YouTubeBroadcasterAccount.RefreshToken = "named-str-rt-new";
            SaveError = context.SaveSetup(settingsManager, accountsStore, ytSettingsStore, ytAccountsStore);
            CanAdvance = true;
        }
    }

    private sealed class StubIdentityClient : IYouTubeChannelIdentityClient
    {
        private readonly Func<string, string, CancellationToken, Task<YouTubeChannelIdentity?>> _resolveByHandle;

        public StubIdentityClient(Func<string, string, Task<YouTubeChannelIdentity?>> resolveByHandle)
        {
            _resolveByHandle = (handle, token, _) => resolveByHandle(handle, token);
        }

        public StubIdentityClient(Func<string, string, CancellationToken, Task<YouTubeChannelIdentity?>> resolveByHandle)
        {
            _resolveByHandle = resolveByHandle;
        }

        public Task<YouTubeChannelIdentity> GetOwnChannelAsync(string accessToken, CancellationToken ct = default)
            => Task.FromResult(new YouTubeChannelIdentity("UC_OWN", "Own"));

        public Task<YouTubeChannelIdentity?> ResolveChannelByHandleAsync(string handle, string accessToken, CancellationToken ct = default)
            => _resolveByHandle(handle, accessToken, ct);
    }

    private sealed class TimeoutYouTubeAdapter(Action<CancellationToken> onResolve) : IPlatformChannelAdapter
    {
        public StreamingPlatform Platform => StreamingPlatform.YouTube;

        public PlatformCapabilities Capabilities => PlatformCapabilities.None;

        public Task<ResolvedChannelIdentity?> ResolveChannelIdentityAsync(
            string rawInput,
            string? preferredContextId,
            CancellationToken cancellationToken)
        {
            onResolve(cancellationToken);
            return Task.FromException<ResolvedChannelIdentity?>(new OperationCanceledException(cancellationToken));
        }

        public Task<ChannelAccessVerification> VerifyChannelAccessAsync(
            ChannelKey channel,
            PlatformAuthRole role,
            string contextId,
            CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<ChannelLiveProbeResult> ProbeChannelLiveAsync(
            ChannelCatalogEntry channel,
            CancellationToken cancellationToken) => throw new NotSupportedException();
    }

    private sealed class StubEmbeddedTwitchAuth(OAuthFlowResult flow) : IEmbeddedTwitchAuthDialog
    {
        public Task<EmbeddedTwitchAuthResult> AuthorizeAsync(EmbeddedTwitchAuthRequest request, CancellationToken cancellationToken = default)
            => Task.FromResult(EmbeddedTwitchAuthResult.Completed(flow));
    }

    private sealed class StubClipboardService : IClipboardService
    {
        public bool TrySetText(string? text) => true;
    }

    private sealed class StubLauncher : IShellLauncher
    {
        public bool Open(string pathOrUrl) => true;
        public bool Reveal(string path) => true;
        public bool Start(string executable, params string[] arguments) => true;
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

    private sealed class StubChannelValidator : IOnboardingChannelValidator
    {
        public Task<ChannelValidationResult> ValidateAsync(string login, string clientId, string accessToken, CancellationToken cancellationToken)
            => Task.FromResult(ChannelValidationResult.Found);
        public Task<ChannelValidationResult> ValidateWithAppTokenAsync(string login, string clientId, string clientSecret, CancellationToken cancellationToken)
            => Task.FromResult(ChannelValidationResult.Found);
    }

    private sealed class StubYouTubeOAuthService : IYouTubeOAuthService
    {
        public event Action<PlatformAuthRole, string>? StatusChanged { add { } remove { } }
        public Task<YouTubeOAuthFlowResult> StartOAuthFlowAsync(PlatformAuthRole role, string clientId, string clientSecret, string[]? scopes, Action<string> onAuthUrlReady, CancellationToken ct = default)
            => Task.FromResult(new YouTubeOAuthFlowResult("a", "r", [], "c", "t", 3600));
        public Task<YouTubeOAuthFlowResult> StartOAuthFlowToDraftAsync(PlatformAuthRole role, string clientId, string clientSecret, string[]? scopes, Action<string> onAuthUrlReady, CancellationToken ct = default)
            => Task.FromResult(new YouTubeOAuthFlowResult("a", "r", [], "c", "t", 3600));
        public Task<string> RefreshTokenAsync(PlatformAuthRole role, string clientId, string clientSecret, string refreshToken, CancellationToken ct = default)
            => Task.FromResult(string.Empty);
        public Task<string?> GetAccessTokenAsync(PlatformAuthRole role, CancellationToken ct = default)
            => Task.FromResult<string?>(null);
        public bool TryClearAccessToken(PlatformAuthRole role, string expectedToken) => false;
        public void ClearAccount(PlatformAuthRole role, bool publishAuthorizationRefreshed = false) { }
    }

    public class InertProxy : DispatchProxy
    {
        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args) => throw new NotSupportedException(targetMethod?.Name);
    }
}
