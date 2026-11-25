using PoproshaykaBot.Core.Platforms;
using PoproshaykaBot.Core.Platforms.Auth;
using PoproshaykaBot.Core.Settings;
using PoproshaykaBot.Core.Settings.Stores;
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
            Assert.That(context.SaveTwitchAccounts(accounts), Is.True);

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

            context.RestoreTwitchAssignments();
            Assert.That(catalog.GetActiveChannel(StreamingPlatform.Twitch)?.GetAccountAssignment(PlatformAuthRole.Bot)?.IsVerified, Is.False);
        }
        finally
        {
            folder.Delete(recursive: true);
        }
    }
}
