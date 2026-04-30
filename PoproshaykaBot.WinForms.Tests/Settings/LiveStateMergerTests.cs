using PoproshaykaBot.WinForms.Settings;

namespace PoproshaykaBot.WinForms.Tests.Settings;

[TestFixture]
public sealed class LiveStateMergerTests
{
    [Test]
    public void Apply_BroadcastProfiles_TakenFromLive()
    {
        var draft = new AppSettings();
        var live = new AppSettings();
        live.Twitch.BroadcastProfiles.Profiles.Add(new() { Name = "live-profile" });

        LiveStateMerger.Apply(draft, live);

        Assert.That(draft.Twitch.BroadcastProfiles.Profiles, Has.Count.EqualTo(1));
        Assert.That(draft.Twitch.BroadcastProfiles.Profiles[0].Name, Is.EqualTo("live-profile"));
    }

    [Test]
    public void Apply_PollProfiles_TakenFromLive()
    {
        var draft = new AppSettings();
        var live = new AppSettings();
        live.Twitch.Polls.Profiles = [new() { Name = "p1" }];

        LiveStateMerger.Apply(draft, live);

        Assert.That(draft.Twitch.Polls.Profiles, Has.Count.EqualTo(1));
    }

    [Test]
    public void Apply_RecentCategories_TakenFromLive()
    {
        var draft = new AppSettings();
        var live = new AppSettings();
        live.Twitch.Infrastructure.RecentCategories = [new() { Id = "1", Name = "Just Chatting" }];

        LiveStateMerger.Apply(draft, live);

        Assert.That(draft.Twitch.Infrastructure.RecentCategories, Has.Count.EqualTo(1));
    }

    [Test]
    public void Apply_BotAccount_TokensCopiedFromLive()
    {
        var draft = new AppSettings();
        var live = new AppSettings();
        live.Twitch.BotAccount.AccessToken = "live-bot-token";
        live.Twitch.BotAccount.RefreshToken = "live-bot-refresh";
        live.Twitch.BotAccount.Login = "bot";
        live.Twitch.BotAccount.UserId = "42";
        live.Twitch.BotAccount.StoredScopes = ["chat:read"];

        LiveStateMerger.Apply(draft, live);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(draft.Twitch.BotAccount.AccessToken, Is.EqualTo("live-bot-token"));
            Assert.That(draft.Twitch.BotAccount.RefreshToken, Is.EqualTo("live-bot-refresh"));
            Assert.That(draft.Twitch.BotAccount.Login, Is.EqualTo("bot"));
            Assert.That(draft.Twitch.BotAccount.UserId, Is.EqualTo("42"));
            Assert.That(draft.Twitch.BotAccount.StoredScopes, Is.EqualTo(["chat:read"]));
        }
    }

    [Test]
    public void Apply_BroadcasterAccount_TokensCopiedFromLive()
    {
        var draft = new AppSettings();
        var live = new AppSettings();
        live.Twitch.BroadcasterAccount.AccessToken = "live-cast-token";

        LiveStateMerger.Apply(draft, live);

        Assert.That(draft.Twitch.BroadcasterAccount.AccessToken, Is.EqualTo("live-cast-token"));
    }

    [Test]
    public void Apply_AccessTokenExpiresAt_TakenFromLive()
    {
        var draft = new AppSettings();
        var live = new AppSettings();
        var refreshedAt = DateTimeOffset.UtcNow.AddHours(4);
        live.Twitch.BotAccount.AccessTokenExpiresAt = refreshedAt;

        LiveStateMerger.Apply(draft, live);

        Assert.That(draft.Twitch.BotAccount.AccessTokenExpiresAt, Is.EqualTo(refreshedAt),
            "TTL access-токена обновляется live при refresh — потеря приведёт к лишним validate-запросам.");
    }

    [Test]
    public void Apply_DoesNotTouchObsChat()
    {
        var draft = new AppSettings();
        var live = new AppSettings();
        draft.Twitch.ObsChat.MaxMessages = 99;
        live.Twitch.ObsChat.MaxMessages = 7;

        LiveStateMerger.Apply(draft, live);

        Assert.That(draft.Twitch.ObsChat.MaxMessages, Is.EqualTo(99),
            "ObsChat редактируется только в форме, live-overlay не должен его перетирать.");
    }
}
