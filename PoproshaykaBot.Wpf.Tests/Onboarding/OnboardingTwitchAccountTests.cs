using PoproshaykaBot.Core.Platforms;
using PoproshaykaBot.Core.Platforms.Auth;
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
    [TestCase(null)]
    [TestCase("channel-bot")]
    public void Login_AssignsActiveChannelAndKeepsOtherContexts(string? contextId)
    {
        var folder = Directory.CreateTempSubdirectory("onboarding-twitch-");
        try
        {
            var accounts = new AccountsStore(filePath: Path.Combine(folder.FullName, "accounts.json"));
            accounts.UpsertContext(PlatformAuthRole.Bot, "other-bot", "Другой бот", new() { UserId = "2" });
            var catalog = new ChannelCatalogStore(filePath: Path.Combine(folder.FullName, "channels.json"));
            var channel = new ChannelCatalogEntry
            {
                Platform = StreamingPlatform.Twitch,
                ChannelId = "42",
                IsConfigured = true,
                BotAccountId = "1",
                BotContextId = contextId,
            };
            catalog.Save(new() { Channels = [channel] });
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
            Assert.That(context.SaveTwitchSetup(accounts, () => true), Is.Null);

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

            Assert.That(context.RestoreTwitchChanges(accounts), Is.True);
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
            Assert.That(context.SaveTwitchSetup(accounts, () => true), Is.Null);
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

            var settingsSaved = false;
            var error = context.SaveTwitchSetup(accounts, () => settingsSaved = true);
            Assert.That(error is null, Is.EqualTo(!reassign));
            Assert.That(settingsSaved, Is.EqualTo(!reassign));
            Assert.That(context.RestoreTwitchChanges(accounts), Is.True);

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

    [TestCase("accounts.json")]
    [TestCase("channels.json")]
    [TestCase("settings.json")]
    public async Task RejectedWriteIsCompensatedAndKeepsOriginalAccount(string revokedFile)
    {
        var folder = Directory.CreateTempSubdirectory("onboarding-write-rejection-");
        try
        {
            var gate = new SettingsWriteGate();
            var accountPath = Path.Combine(folder.FullName, "accounts.json");
            var channelPath = Path.Combine(folder.FullName, "channels.json");
            var accounts = new AccountsStore(filePath: accountPath, gate: gate);
            var catalog = new ChannelCatalogStore(filePath: channelPath, gate: gate);
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
            var context = new OnboardingContext(new(), accounts.Load(PlatformAuthRole.Bot, botContext), new())
            {
                TwitchCatalog = catalog,
                TwitchChannel = catalog.GetActiveChannel(StreamingPlatform.Twitch),
            };
            await gate.RunExternalWriteAsync(() => Task.FromResult(0), _ => new[] { revokedFile });

            Assert.That(context.AcceptTwitchLogin(PlatformAuthRole.Bot, context.BotAccount,
                new("access", "new", [.. TwitchScopes.BotRequired], "bot", "1", 3600)), Is.True);
            var settingsAttempted = false;
            var error = context.SaveTwitchSetup(accounts, () =>
            {
                settingsAttempted = true;
                return false;
            });

            using (Assert.EnterMultipleScope())
            {
                Assert.That(error, Is.Not.Null);
                Assert.That(settingsAttempted, Is.EqualTo(revokedFile == "settings.json"));
                Assert.That(context.TwitchAccountsApplied, Is.False);
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
