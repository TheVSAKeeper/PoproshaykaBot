using System.IO;
using KeepShell.Services;
using KeepShell.Services.Modal;
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
using PoproshaykaBot.Core.Users.Avatars;
using PoproshaykaBot.Core.YouTube;
using PoproshaykaBot.Core.YouTube.Auth;
using PoproshaykaBot.Wpf.ViewModels.Onboarding;

namespace PoproshaykaBot.Wpf.Tests.Onboarding;

[TestFixture]
public sealed class CompletionPageYouTubeBotOnlyTests
{
    [TestCase(false, false, false, false)]
    [TestCase(true, false, false, false)]
    [TestCase(true, true, false, false)]
    [TestCase(true, false, true, false)]
    [TestCase(false, false, false, true)]
    public async Task TwitchCompletionKeepsBroadcasterOptionalAndRequiresUsableBot(
        bool hasStaleBroadcaster, bool expiredBot, bool changeActiveChannel, bool warnWhilePending)
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"yt-onboard-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);

        try
        {
            await using var provider = BuildProvider();
            var settingsManager = new SettingsManager(NullLogger<SettingsManager>.Instance, Path.Combine(tempDir, "settings.json"));
            var accountsStore = new AccountsStore(filePath: Path.Combine(tempDir, "accounts.json"));
            var ytSettingsStore = new YouTubeSettingsStore(NullLogger<YouTubeSettingsStore>.Instance, Path.Combine(tempDir, "youtube.json"));
            var ytAccountsStore = new YouTubeAccountsStore(NullLogger<YouTubeAccountsStore>.Instance, Path.Combine(tempDir, "youtube-accounts.json"));
            var pending = warnWhilePending
                ? new TaskCompletionSource<ChannelValidationResult>(TaskCreationOptions.RunContinuationsAsynchronously)
                : null;
            var validator = new StubChannelValidator(pending?.Task);
            var dialogs = new NoOpDialogService();
            var catalog = new ChannelCatalogStore(filePath: Path.Combine(tempDir, "channels.json"));
            catalog.Save(new() { Channels = [new()
            {
                Platform = StreamingPlatform.Twitch, ChannelId = "42", IsConfigured = true,
                BotAccountId = "1", BotVerifiedSubjectId = "1",
                BroadcasterAccountId = warnWhilePending ? "42" : null,
                BroadcasterVerifiedSubjectId = warnWhilePending ? "42" : null,
            }] });

            using var page = new CompletionPageViewModel(
                settingsManager,
                accountsStore,
                provider.GetRequiredService<IEventBus>(),
                provider.GetRequiredService<KestrelHttpServer>(),
                validator,
                provider.GetRequiredService<BotConnectionManager>(),
                NullLogger<CompletionPageViewModel>.Instance,
                dialogs,
                ytSettingsStore,
                ytAccountsStore);

            var context = new OnboardingContext(
                new AppSettings
                {
                    Twitch = new TwitchSettings { ClientId = "tw-client", ClientSecret = "tw-secret", Channel = "streamer" },
                },
                new TwitchAccountSettings
                {
                    Login = "twbot",
                    UserId = "1",
                    AccessToken = "tw-bot-token",
                    RefreshToken = expiredBot ? string.Empty : "tw-bot-refresh",
                    AccessTokenExpiresAt = DateTimeOffset.UtcNow.AddMinutes(-15),
                    Scopes = [.. TwitchScopes.BotRequired],
                    StoredScopes = [.. TwitchScopes.BotRequired],
                },
                warnWhilePending
                    ? new TwitchAccountSettings { UserId = "42", Login = "renamed", RefreshToken = "streamer-refresh", StoredScopes = [.. TwitchScopes.BroadcasterRequired] }
                    : hasStaleBroadcaster
                    ? new TwitchAccountSettings { Login = "streamer", RefreshToken = "streamer-refresh", StoredScopes = [TwitchScopes.ChannelBot] }
                    : new TwitchAccountSettings(),
                new YouTubeSettings { Enabled = true, ClientId = "yt-client", Channel = "@foreign" },
                "yt-secret",
                new YouTubeAccountSettings
                {
                    ChannelId = "UC_BOT",
                    AccessToken = "yt-bot-token",
                    Scopes = [.. YouTubeScopes.BotRequired],
                },
                new YouTubeAccountSettings { Scopes = [.. YouTubeScopes.BroadcasterRequired] })
            {
                TwitchCatalog = catalog,
                TwitchChannel = catalog.GetActiveChannel(StreamingPlatform.Twitch),
            };

            Assert.That(settingsManager.SaveSettings(context.Settings), Is.True);
            Assert.That(accountsStore.SaveAll(context.BotAccount, context.BroadcasterAccount), Is.True);
            Assert.That(new OnboardingChecklist(settingsManager, accountsStore).IsComplete, Is.EqualTo(!expiredBot));

            var doneTcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            page.PropertyChanged += (_, args) =>
            {
                if (warnWhilePending ? page.CanAdvance : !page.IsValidationPending)
                {
                    doneTcs.TrySetResult();
                }
            };

            page.OnEnter(context);
            if (pending is not null)
            {
                using (Assert.EnterMultipleScope())
                {
                    Assert.That(page.IsValidationWarning, Is.True);
                    Assert.That(page.IsValidationPending, Is.True);
                    Assert.That(page.CanAdvance, Is.False);
                }
                pending.SetResult(ChannelValidationResult.Found);
            }
            if (page.IsValidationPending || (warnWhilePending && !page.CanAdvance))
            {
                await doneTcs.Task.WaitAsync(TimeSpan.FromSeconds(5));
            }

            Assert.Multiple(() =>
            {
                Assert.That(page.CanAdvance, Is.EqualTo(!expiredBot));
                Assert.That(page.IsValidationFailure, Is.EqualTo(expiredBot));
                if (!warnWhilePending)
                {
                    Assert.That(page.ValidationText, Does.Contain("Доступы аккаунта стримера: не подключён (режим «только бот»)"));
                }
            });

            if (changeActiveChannel)
            {
                catalog.Mutate(state => state.Channels[0].ChannelId = "99");
                context.Settings.Twitch.ClientId = "draft-client";
                Assert.That(await page.OnLeavingAsync(context), Is.False);
                using (Assert.EnterMultipleScope())
                {
                    Assert.That(dialogs.WarningMessage, Does.Contain("Канал или назначенный аккаунт Twitch изменились"));
                    Assert.That(settingsManager.Current.Twitch.ClientId, Is.EqualTo("tw-client"));
                    Assert.That(new SettingsManager(NullLogger<SettingsManager>.Instance,
                        Path.Combine(tempDir, "settings.json")).Current.Twitch.ClientId, Is.EqualTo("tw-client"));
                }
            }
        }
        finally
        {
            if (Directory.Exists(tempDir))
            {
                Directory.Delete(tempDir, recursive: true);
            }
        }
    }

    [Test]
    public async Task DeniedWriteKeepsOriginalCredentialsBeforeAndAfterCancel()
    {
        var dir = Directory.CreateTempSubdirectory("gated-onboarding-");
        try
        {
            await using var provider = BuildProvider();
            var gate = new SettingsWriteGate();
            var path = Path.Combine(dir.FullName, "settings.json");
            var manager = new SettingsManager(NullLogger<SettingsManager>.Instance, path, gate);
            manager.SaveSettings(new() { Twitch = new() { ClientId = "original", ClientSecret = "original-secret" } });
            await gate.RunExternalWriteAsync(() => Task.FromResult(0), _ => new[] { "settings.json" });
            var accounts = new AccountsStore(filePath: Path.Combine(dir.FullName, "accounts.json"));
            var bus = provider.GetRequiredService<IEventBus>();
            var controller = provider.GetRequiredService<BotConnectionManager>();
            using var connection = new BotConnectionPageViewModel(controller, bus, manager, accounts, NullLogger<BotConnectionPageViewModel>.Instance);
            var wizard = new OnboardingWizardViewModel([new DraftCredentialsPage(), connection], manager, accounts,
                provider.GetRequiredService<KestrelHttpServer>(), controller, bus, NullLogger<OnboardingWizardViewModel>.Instance);

            await wizard.NextCommand.ExecuteAsync(null);
            Assert.That(manager.Current.Twitch.ClientId, Is.EqualTo("original"));
            Assert.That(await wizard.RequestCloseAsync(), Is.True);

            using (Assert.EnterMultipleScope())
            {
                Assert.That(manager.Current.Twitch.ClientId, Is.EqualTo("original"));
                Assert.That(manager.Current.Twitch.ClientSecret, Is.EqualTo("original-secret"));
                Assert.That(new SettingsManager(NullLogger<SettingsManager>.Instance, path).Current.Twitch.ClientId, Is.EqualTo("original"));
            }
        }
        finally
        {
            dir.Delete(recursive: true);
        }
    }

    private sealed class DraftCredentialsPage : OnboardingPageViewModelBase
    {
        public override string PageTitle => "Черновик";

        public override void OnEnter(OnboardingContext context)
        {
            context.Settings.Twitch.ClientId = "draft";
            context.Settings.Twitch.ClientSecret = "draft-secret";
            CanAdvance = true;
        }
    }

    [TestCase(false)]
    [TestCase(true)]
    public async Task CancelAfterBlockedRollbackNotifiesAndAllowsOnlyRetryingClose(bool blockYouTube)
    {
        var dir = Directory.CreateTempSubdirectory("gated-onboarding-cancel-");
        try
        {
            await using var provider = BuildProvider();
            var gate = new SettingsWriteGate();
            var path = Path.Combine(dir.FullName, "settings.json");
            var manager = new SettingsManager(NullLogger<SettingsManager>.Instance, path, gate);
            manager.SaveSettings(new() { Twitch = new() { ClientId = "original" } });
            var accounts = new AccountsStore(filePath: Path.Combine(dir.FullName, "accounts.json"));
            var youtubePath = Path.Combine(dir.FullName, "youtube.json");
            var youtube = new YouTubeSettingsStore(filePath: youtubePath);
            youtube.Save(new() { ClientId = "original" });
            var page = new ProvisionalSetupPage(manager, accounts, youtube);
            var bus = provider.GetRequiredService<IEventBus>();
            var refreshed = new List<PlatformAuthorizationRefreshed>();
            using var subscription = bus.Subscribe<PlatformAuthorizationRefreshed>(message => refreshed.Add(message));
            var dialogs = new NoOpDialogService { ConfirmWarningResult = false };
            var wizard = new OnboardingWizardViewModel([page, new DraftCredentialsPage()], manager, accounts,
                provider.GetRequiredService<KestrelHttpServer>(), provider.GetRequiredService<BotConnectionManager>(), bus,
                NullLogger<OnboardingWizardViewModel>.Instance, youtubeSettingsStore: youtube, dialogService: dialogs);

            var allowWrite = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
            Task? externalWrite = null;
            FileStream? locked = null;
            try
            {
                if (blockYouTube)
                {
                    locked = File.Open(youtubePath, FileMode.Open, FileAccess.Read, FileShare.None);
                }
                else
                {
                    externalWrite = gate.RunExternalWriteAsync(() => allowWrite.Task, _ => []);
                }

                Assert.That(await wizard.RequestCloseAsync(), Is.False);
                using (Assert.EnterMultipleScope())
                {
                    Assert.That(manager.Current.Twitch.ClientId, Is.EqualTo(blockYouTube ? "original" : "draft"));
                    Assert.That(new SettingsManager(NullLogger<SettingsManager>.Instance, path).Current.Twitch.ClientId,
                        Is.EqualTo(blockYouTube ? "original" : "draft"));
                    Assert.That(accounts.LoadBot().AccessToken, Is.Empty);
                    Assert.That(refreshed.Any(message => message.Platform == StreamingPlatform.Twitch && message.Role == PlatformAuthRole.Bot), Is.True);
                    Assert.That(wizard.NextCommand.CanExecute(null), Is.False);
                    Assert.That(wizard.CancelCommand.CanExecute(null), Is.True);
                    Assert.That(page.Context.SaveSetup(manager, accounts, youtube), Does.Contain("отменяется"));
                }
            }
            finally
            {
                if (locked is not null)
                {
                    await locked.DisposeAsync();
                }
                if (externalWrite is not null)
                {
                    allowWrite.TrySetResult(0);
                    await externalWrite;
                }
            }

            Assert.That(await wizard.RequestCloseAsync(), Is.True);
            using (Assert.EnterMultipleScope())
            {
                Assert.That(manager.Current.Twitch.ClientId, Is.EqualTo("original"));
                Assert.That(youtube.Load().ClientId, Is.EqualTo("original"));
                Assert.That(new YouTubeSettingsStore(filePath: youtubePath).Load().ClientId, Is.EqualTo("original"));
            }
        }
        finally
        {
            dir.Delete(recursive: true);
        }
    }

    private sealed class ProvisionalSetupPage(SettingsManager settings, AccountsStore accounts, YouTubeSettingsStore youtube)
        : OnboardingPageViewModelBase
    {
        public override string PageTitle => "Черновик";
        public OnboardingContext Context { get; private set; } = null!;

        public override void OnEnter(OnboardingContext context)
        {
            Context = context;
            context.Settings.Twitch.ClientId = "draft";
            context.YouTubeSettings.ClientId = "draft";
            Assert.That(context.AcceptTwitchLogin(PlatformAuthRole.Bot, context.BotAccount,
                new("access", "refresh", [.. TwitchScopes.BotRequired], "bot", "1", 3600)), Is.True);
            Assert.That(context.SaveSetup(settings, accounts, youtube), Is.Null);
            CanAdvance = true;
        }
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

    private sealed class StubChannelValidator(Task<ChannelValidationResult>? pending = null) : IOnboardingChannelValidator
    {
        public Task<ChannelValidationResult> ValidateAsync(string login, string clientId, string accessToken, CancellationToken cancellationToken)
            => pending ?? Task.FromResult(ChannelValidationResult.Found);

        public Task<ChannelValidationResult> ValidateWithAppTokenAsync(string login, string clientId, string clientSecret, CancellationToken cancellationToken)
            => pending ?? Task.FromResult(ChannelValidationResult.Found);
    }

    private sealed class NoOpDialogService : IDialogService
    {
        public bool ConfirmWarningResult { get; init; } = true;
        public string? WarningMessage { get; private set; }
        public Task<bool> ShowAsync(IDialogViewModel viewModel) => Task.FromResult(false);
        public Task<bool> ReplaceAsync(IDialogViewModel viewModel) => Task.FromResult(false);
        public bool Confirm(string title, string message, bool defaultYes = false) => true;
        public bool ConfirmWarning(string title, string message, bool defaultYes = false) => ConfirmWarningResult;
        public void Info(string title, string message) { }
        public void Warning(string title, string message) => WarningMessage = message;
        public void Error(string title, string message) { }
    }
}
