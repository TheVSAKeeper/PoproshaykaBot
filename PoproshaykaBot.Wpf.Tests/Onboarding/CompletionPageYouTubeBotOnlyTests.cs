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
    [TestCase(false)]
    [TestCase(true)]
    public async Task BotOnly_Without_Broadcaster_Allows_Completing_Onboarding(bool twitchBroadcasterAuthorized)
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
            var validator = new StubChannelValidator();

            using var page = new CompletionPageViewModel(
                settingsManager,
                accountsStore,
                provider.GetRequiredService<IEventBus>(),
                provider.GetRequiredService<KestrelHttpServer>(),
                validator,
                provider.GetRequiredService<BotConnectionManager>(),
                NullLogger<CompletionPageViewModel>.Instance,
                new NoOpDialogService(),
                ytSettingsStore,
                ytAccountsStore);

            var context = new OnboardingContext(
                new AppSettings
                {
                    Twitch = new TwitchSettings { ClientId = "tw-client", ClientSecret = "tw-secret", Channel = "streamer" },
                },
                new TwitchAccountSettings { Login = "twbot", AccessToken = "tw-bot-token", RefreshToken = "tw-bot-refresh", Scopes = [.. TwitchScopes.BotRequired], StoredScopes = [.. TwitchScopes.BotRequired] },
                twitchBroadcasterAuthorized
                    ? new TwitchAccountSettings { Login = "streamer", AccessToken = "tw-streamer-token", Scopes = [.. TwitchScopes.BroadcasterRequired] }
                    : new TwitchAccountSettings(),
                new YouTubeSettings { Enabled = true, ClientId = "yt-client", Channel = "@foreign" },
                "yt-secret",
                new YouTubeAccountSettings
                {
                    ChannelId = "UC_BOT",
                    AccessToken = "yt-bot-token",
                    Scopes = [.. YouTubeScopes.BotRequired],
                },
                new YouTubeAccountSettings { Scopes = [.. YouTubeScopes.BroadcasterRequired] });

            Assert.That(settingsManager.SaveSettings(context.Settings), Is.True);
            Assert.That(accountsStore.SaveAll(context.BotAccount, context.BroadcasterAccount), Is.True);
            Assert.That(new OnboardingChecklist(settingsManager, accountsStore).IsComplete, Is.True);

            var doneTcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            page.PropertyChanged += (_, args) =>
            {
                if (args.PropertyName == nameof(CompletionPageViewModel.IsValidationPending) && !page.IsValidationPending)
                {
                    doneTcs.TrySetResult();
                }
            };

            page.OnEnter(context);
            if (page.IsValidationPending)
            {
                await doneTcs.Task.WaitAsync(TimeSpan.FromSeconds(5));
            }

            Assert.Multiple(() =>
            {
                Assert.That(page.CanAdvance, Is.True);
                Assert.That(page.IsValidationFailure, Is.False);
                Assert.That(page.ValidationText, Does.Contain("режим «только бот»"));
                if (!twitchBroadcasterAuthorized)
                {
                    Assert.That(page.ValidationText, Does.Contain("Доступы аккаунта стримера: не подключён (режим «только бот»)"));
                }
            });
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
    public async Task CancelRestoresTheInMemoryCredentialsWhenSettingsWriteWasDenied()
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
            Assert.That(manager.Current.Twitch.ClientId, Is.EqualTo("draft"));
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

    private sealed class StubChannelValidator : IOnboardingChannelValidator
    {
        public Task<ChannelValidationResult> ValidateAsync(string login, string clientId, string accessToken, CancellationToken cancellationToken)
            => Task.FromResult(ChannelValidationResult.Found);

        public Task<ChannelValidationResult> ValidateWithAppTokenAsync(string login, string clientId, string clientSecret, CancellationToken cancellationToken)
            => Task.FromResult(ChannelValidationResult.Found);
    }

    private sealed class NoOpDialogService : IDialogService
    {
        public Task<bool> ShowAsync(IDialogViewModel viewModel) => Task.FromResult(false);
        public Task<bool> ReplaceAsync(IDialogViewModel viewModel) => Task.FromResult(false);
        public bool Confirm(string title, string message, bool defaultYes = false) => true;
        public bool ConfirmWarning(string title, string message, bool defaultYes = false) => true;
        public void Info(string title, string message) { }
        public void Warning(string title, string message) { }
        public void Error(string title, string message) { }
    }
}
