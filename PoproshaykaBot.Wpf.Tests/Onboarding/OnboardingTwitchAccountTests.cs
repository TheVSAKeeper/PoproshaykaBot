using PoproshaykaBot.Core.Platforms;
using PoproshaykaBot.Core.Platforms.Auth;
using Microsoft.Extensions.Logging.Abstractions;
using PoproshaykaBot.Core.Settings;
using PoproshaykaBot.Core.Settings.Stores;
using PoproshaykaBot.Core.Twitch.Auth;
using PoproshaykaBot.Core.Twitch.Chat;
using PoproshaykaBot.Wpf.ViewModels.Onboarding;
using System.IO;

namespace PoproshaykaBot.Wpf.Tests.Onboarding;

[TestFixture]
public sealed class OnboardingTwitchAccountTests
{
    [TestCase(null, false)]
    [TestCase("channel-bot", false)]
    [TestCase("channel-bot", true)]
    public void Login_AssignsActiveChannelAndKeepsOtherContexts(string? contextId, bool legacyWhitespace)
    {
        var folder = Directory.CreateTempSubdirectory("onboarding-twitch-");
        try
        {
            var accounts = new AccountsStore(filePath: Path.Combine(folder.FullName, "accounts.json"));
            accounts.UpsertContext(PlatformAuthRole.Bot, "other-bot", "Другой бот", new() { UserId = "2" });
            var channelPath = Path.Combine(folder.FullName, "channels.json");
            if (legacyWhitespace)
            {
                File.WriteAllText(channelPath, """
                    {"channels":[{"platform":"twitch","channelId":" 42 ","isConfigured":true,"botAccountId":" 1 ","botContextId":" channel-bot ","botVerifiedSubjectId":" "}]}
                    """);
            }
            var catalog = new ChannelCatalogStore(filePath: channelPath);
            var channel = new ChannelCatalogEntry
            {
                Platform = StreamingPlatform.Twitch,
                ChannelId = "42",
                IsConfigured = true,
                BotAccountId = "1",
                BotContextId = contextId,
            };
            if (!legacyWhitespace)
            {
                catalog.Save(new() { Channels = [channel] });
            }
            var manager = CreateSettingsManager(folder);
            var context = new OnboardingContext(new() { Twitch = new() { Channel = "" } }, new(), new())
            {
                TwitchCatalog = catalog,
                TwitchChannel = catalog.GetActiveChannel(StreamingPlatform.Twitch),
            };

            Assert.That(context.HasConfiguredTwitchChannel, Is.True);
            Assert.That(context.AcceptTwitchLogin(PlatformAuthRole.Bot, context.BotAccount,
                new("access", "refresh", [.. TwitchScopes.BotRequired], "wrong", "2", 3600)), Is.False);
            Assert.That(context.AcceptTwitchLogin(PlatformAuthRole.Bot, context.BotAccount,
                new("access", "refresh", [.. TwitchScopes.BotRequired], "bot", "1", 3600)), Is.True);
            Assert.That(context.SaveSetup(manager, accounts), Is.Null);

            using (Assert.EnterMultipleScope())
            {
                Assert.That(catalog.GetActiveChannel(StreamingPlatform.Twitch)?.GetAccountAssignment(PlatformAuthRole.Bot)?.IsVerified, Is.True);
                Assert.That(accounts.Load(PlatformAuthRole.Bot, contextId).UserId, Is.EqualTo("1"));
                Assert.That(accounts.Load(PlatformAuthRole.Bot, "other-bot").UserId, Is.EqualTo("2"));
                if (contextId is not null)
                {
                    Assert.That(accounts.LoadBot().UserId, Is.Empty);
                }
            }

            context.RestoreProvisionalWrites();
            Assert.That(catalog.GetActiveChannel(StreamingPlatform.Twitch)?.GetAccountAssignment(PlatformAuthRole.Bot)?.IsVerified, Is.False);
        }
        finally
        {
            folder.Delete(recursive: true);
        }
    }

    [TestCase(false)]
    [TestCase(true)]
    public void SaveAndCancelKeepNewerTokensAndAssignments(bool reassign)
    {
        var folder = Directory.CreateTempSubdirectory("onboarding-concurrency-");
        try
        {
            var accounts = new AccountsStore(filePath: Path.Combine(folder.FullName, "accounts.json"));
            var catalog = new ChannelCatalogStore(filePath: Path.Combine(folder.FullName, "channels.json"));
            const string botContext = "first-bot";
            const string otherContext = "second-bot";
            const string streamerContext = "streamer";
            catalog.Save(new() { Channels = [new()
            {
                Platform = StreamingPlatform.Twitch,
                ChannelId = "42",
                IsConfigured = true,
                BotContextId = botContext,
                BotAccountId = "1",
                BroadcasterContextId = streamerContext,
                BroadcasterAccountId = "42",
            }] });
            accounts.UpsertContext(PlatformAuthRole.Broadcaster, streamerContext, streamerContext, new() { UserId = "42", RefreshToken = "streamer-original" });
            accounts.UpsertContext(PlatformAuthRole.Bot, otherContext, otherContext, new() { UserId = "2", RefreshToken = "other" });
            var context = new OnboardingContext(new(), new(), accounts.Load(PlatformAuthRole.Broadcaster, streamerContext))
            {
                TwitchCatalog = catalog,
                TwitchChannel = catalog.GetActiveChannel(StreamingPlatform.Twitch),
            };

            Assert.That(context.AcceptTwitchLogin(PlatformAuthRole.Bot, context.BotAccount,
                new("access", "refresh", [.. TwitchScopes.BotRequired], "first", "1", 3600)), Is.True);
            var manager = CreateSettingsManager(folder);
            Assert.That(context.SaveSetup(manager, accounts), Is.Null);
            Assert.That(context.CanUseTwitchRole(PlatformAuthRole.Bot, accounts.Load(PlatformAuthRole.Bot, botContext)), Is.True);
            Assert.That(context.CanUseTwitchRole(PlatformAuthRole.Bot, new() { UserId = "999" }), Is.False);
            accounts.Mutate(PlatformAuthRole.Bot, botContext, bot => bot.RefreshToken = "bot-rotated");
            accounts.Mutate(PlatformAuthRole.Broadcaster, streamerContext, streamer => streamer.RefreshToken = "streamer-rotated");
            if (reassign)
            {
                catalog.Mutate(state =>
                {
                    var entry = state.Channels[0];
                    entry.BotContextId = otherContext;
                    entry.BotAccountId = "2";
                    entry.BotVerifiedSubjectId = "2";
                });
            }

            context.Settings.Twitch.ClientId = "draft";
            var error = context.SaveSetup(manager, accounts);
            Assert.That(error is null, Is.EqualTo(!reassign));
            Assert.That(manager.Current.Twitch.ClientId == "draft", Is.EqualTo(!reassign));
            context.RestoreProvisionalWrites();

            using (Assert.EnterMultipleScope())
            {
                Assert.That(accounts.Load(PlatformAuthRole.Bot, botContext).RefreshToken, Is.EqualTo("bot-rotated"));
                Assert.That(accounts.Load(PlatformAuthRole.Broadcaster, streamerContext).RefreshToken, Is.EqualTo("streamer-rotated"));
                var binding = catalog.GetActiveChannel(StreamingPlatform.Twitch)?.GetAccountAssignment(PlatformAuthRole.Bot);
                Assert.That(binding?.ContextId, Is.EqualTo(reassign ? otherContext : botContext));
                Assert.That(binding?.IsVerified, Is.EqualTo(reassign));
            }
        }
        finally
        {
            folder.Delete(recursive: true);
        }
    }

    [TestCase("accounts.json", false)]
    [TestCase("channels.json", false)]
    [TestCase("settings.json", false)]
    [TestCase("youtube-accounts.json", false)]
    [TestCase("youtube-accounts.json", true)]
    public async Task RejectedWriteIsCompensatedAndKeepsOriginalAccount(string revokedFile, bool blockFirstRollback)
    {
        var folder = Directory.CreateTempSubdirectory("onboarding-write-rejection-");
        try
        {
            var gate = new SettingsWriteGate();
            var accountPath = Path.Combine(folder.FullName, "accounts.json");
            var channelPath = Path.Combine(folder.FullName, "channels.json");
            var accounts = new AccountsStore(filePath: accountPath, gate: gate);
            var catalog = new ChannelCatalogStore(filePath: channelPath, gate: gate);
            var manager = CreateSettingsManager(folder, gate);
            manager.SaveSettings(new() { Twitch = new() { ClientId = "original" } });
            var ytSettingsPath = Path.Combine(folder.FullName, "youtube.json");
            var ytSettings = new YouTubeSettingsStore(filePath: ytSettingsPath, gate: gate);
            ytSettings.Save(new() { ClientId = "original" });
            var ytAccounts = new YouTubeAccountsStore(filePath: Path.Combine(folder.FullName, "youtube-accounts.json"), gate: gate);
            const string botContext = "named-bot";
            catalog.Save(new() { Channels = [new()
            {
                Platform = StreamingPlatform.Twitch,
                ChannelId = "42",
                IsConfigured = true,
                BotContextId = botContext,
                BotAccountId = "1",
            }] });
            accounts.UpsertContext(PlatformAuthRole.Bot, botContext, botContext, new() { UserId = "1", RefreshToken = "original" });
            var context = new OnboardingContext(new() { Twitch = new() { ClientId = "draft" } },
                accounts.Load(PlatformAuthRole.Bot, botContext), new(), ytSettings.Load())
            {
                TwitchCatalog = catalog,
                TwitchChannel = catalog.GetActiveChannel(StreamingPlatform.Twitch),
            };
            await gate.RunExternalWriteAsync(() => Task.FromResult(0), _ => new[] { revokedFile });
            context.YouTubeSettings.ClientId = "draft";
            context.YouTubeClientSecret = "draft-secret";

            Assert.That(context.AcceptTwitchLogin(PlatformAuthRole.Bot, context.BotAccount,
                new("access", "new", [.. TwitchScopes.BotRequired], "bot", "1", 3600)), Is.True);
            if (blockFirstRollback)
            {
                FileStream? channelLock = null;
                ytSettings.Changed += () => channelLock ??= File.Open(channelPath, FileMode.Open, FileAccess.Read, FileShare.None);
                try
                {
                    Assert.Throws<AggregateException>(() => context.SaveSetup(manager, accounts, ytSettings, ytAccounts));
                    Assert.That(context.HasProvisionalWrites, Is.True);
                    Assert.That(accounts.Load(PlatformAuthRole.Bot, botContext).RefreshToken, Is.EqualTo("original"));
                }
                finally
                {
                    if (channelLock is not null)
                    {
                        await channelLock.DisposeAsync();
                    }
                }

                context.RestoreProvisionalWrites();
            }
            else
            {
                var error = context.SaveSetup(manager, accounts, ytSettings, ytAccounts);
                Assert.That(error, Is.Not.Null);
                if (revokedFile == "youtube-accounts.json")
                {
                    Assert.That(error, Does.Contain("перезапустите приложение"));
                }
            }

            using (Assert.EnterMultipleScope())
            {
                Assert.That(context.HasProvisionalWrites, Is.False);
                Assert.That(manager.Current.Twitch.ClientId, Is.EqualTo("original"));
                Assert.That(CreateSettingsManager(folder).Current.Twitch.ClientId, Is.EqualTo("original"));
                Assert.That(ytSettings.Load().ClientId, Is.EqualTo("original"));
                Assert.That(new YouTubeSettingsStore(filePath: ytSettingsPath).Load().ClientId, Is.EqualTo("original"));
                Assert.That(ytAccounts.LoadClientSecret(), Is.Empty);
                Assert.That(accounts.Load(PlatformAuthRole.Bot, botContext).RefreshToken, Is.EqualTo("original"));
                Assert.That(new AccountsStore(filePath: accountPath).Load(PlatformAuthRole.Bot, botContext).RefreshToken, Is.EqualTo("original"));
                Assert.That(catalog.GetActiveChannel(StreamingPlatform.Twitch)?.GetAccountAssignment(PlatformAuthRole.Bot)?.IsVerified, Is.False);
                Assert.That(new ChannelCatalogStore(filePath: channelPath).GetActiveChannel(StreamingPlatform.Twitch)?.GetAccountAssignment(PlatformAuthRole.Bot)?.IsVerified, Is.False);
            }
        }
        finally
        {
            folder.Delete(recursive: true);
        }
    }

    private static SettingsManager CreateSettingsManager(DirectoryInfo directory, SettingsWriteGate? gate = null) =>
        new(NullLogger<SettingsManager>.Instance, Path.Combine(directory.FullName, "settings.json"), gate);

    [Test]
    public async Task BlockedNewestRollbackPreservesOlderWritesForTheNextAttempt()
    {
        var folder = Directory.CreateTempSubdirectory("onboarding-repeat-rollback-");
        try
        {
            var manager = CreateSettingsManager(folder);
            var accounts = new AccountsStore(filePath: Path.Combine(folder.FullName, "accounts.json"));
            var youtubePath = Path.Combine(folder.FullName, "youtube.json");
            var youtube = new YouTubeSettingsStore(filePath: youtubePath);
            youtube.Save(new() { ClientId = "original" });
            var context = new OnboardingContext(new(), new(), new(), youtube.Load());
            context.YouTubeSettings.ClientId = "first-draft";
            Assert.That(context.SaveSetup(manager, accounts, youtube), Is.Null);
            context.YouTubeSettings.ClientId = "second-draft";
            Assert.That(context.SaveSetup(manager, accounts, youtube), Is.Null);

            await using (var blocked = File.Open(youtubePath, FileMode.Open, FileAccess.Read, FileShare.None))
            {
                Assert.Throws<AggregateException>(context.RestoreProvisionalWrites);
                Assert.That(context.HasProvisionalWrites, Is.True);
                Assert.That(youtube.Load().ClientId, Is.EqualTo("second-draft"));
            }

            context.RestoreProvisionalWrites();

            using (Assert.EnterMultipleScope())
            {
                Assert.That(context.HasProvisionalWrites, Is.False);
                Assert.That(context.HasIncompleteRollback, Is.False);
                Assert.That(youtube.Load().ClientId, Is.EqualTo("original"));
                Assert.That(new YouTubeSettingsStore(filePath: youtubePath).Load().ClientId, Is.EqualTo("original"));
            }
        }
        finally
        {
            folder.Delete(recursive: true);
        }
    }

    [TestCase("42", "42", "renamed", false, true)]
    [TestCase("42", "999", "streamer", true, false)]
    [TestCase(null, "42", "STREAMER", false, true)]
    [TestCase(null, "999", "foreign", false, false)]
    [TestCase(null, "999", "foreign", true, true)]
    public void BroadcasterUsesKnownChannelIdOrExplicitDraftLogin(
        string? channelId, string userId, string login, bool autoDetect, bool expected)
    {
        var context = new OnboardingContext(new() { Twitch = new() { Channel = "streamer" } }, new(), new())
        {
            TwitchChannel = channelId is null ? null : new() { ChannelId = channelId },
            AutoDetectChannel = autoDetect,
        };

        Assert.That(context.AcceptTwitchLogin(PlatformAuthRole.Broadcaster, context.BroadcasterAccount,
            new OAuthFlowResult("access", "refresh", [.. TwitchScopes.BroadcasterRequired], login, userId, 3600)), Is.EqualTo(expected));
    }
}
