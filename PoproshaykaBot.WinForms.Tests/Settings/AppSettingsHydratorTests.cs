using PoproshaykaBot.WinForms.Settings;
using PoproshaykaBot.WinForms.Twitch.Chat;

namespace PoproshaykaBot.WinForms.Tests.Settings;

[TestFixture]
public sealed class AppSettingsHydratorTests
{
    [Test]
    public void ApplyDefaults_EmptyBotAccountScopes_FillsFromBotRequired()
    {
        var settings = new AppSettings();
        settings.Twitch.BotAccount.Scopes = [];
        settings.Twitch.BroadcasterAccount.Scopes = [];

        AppSettingsHydrator.ApplyDefaults(settings);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(settings.Twitch.BotAccount.Scopes, Is.EquivalentTo(TwitchScopes.BotRequired));
            Assert.That(settings.Twitch.BroadcasterAccount.Scopes, Is.EquivalentTo(TwitchScopes.BroadcasterRequired));
        }
    }

    [Test]
    public void ApplyDefaults_NonEmptyScopes_DoNotGetOverwritten()
    {
        var customBot = new[] { "user:read:chat" };
        var customBroadcaster = new[] { "channel:bot", "channel:read:polls" };

        var settings = new AppSettings();
        settings.Twitch.BotAccount.Scopes = customBot;
        settings.Twitch.BroadcasterAccount.Scopes = customBroadcaster;

        AppSettingsHydrator.ApplyDefaults(settings);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(settings.Twitch.BotAccount.Scopes, Is.SameAs(customBot),
                "Если пользователь сократил/изменил scopes — гидратор не должен переписывать их дефолтом");

            Assert.That(settings.Twitch.BroadcasterAccount.Scopes, Is.SameAs(customBroadcaster));
        }
    }

    [Test]
    public void ApplyDefaults_OnlyBotEmpty_BroadcasterIsNotTouched()
    {
        var customBroadcaster = new[] { "channel:bot" };

        var settings = new AppSettings();
        settings.Twitch.BotAccount.Scopes = [];
        settings.Twitch.BroadcasterAccount.Scopes = customBroadcaster;

        AppSettingsHydrator.ApplyDefaults(settings);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(settings.Twitch.BotAccount.Scopes, Is.EquivalentTo(TwitchScopes.BotRequired));
            Assert.That(settings.Twitch.BroadcasterAccount.Scopes, Is.SameAs(customBroadcaster));
        }
    }

    [Test]
    public void ApplyDefaults_NullSettings_Throws()
    {
        Assert.That(() => AppSettingsHydrator.ApplyDefaults(null!), Throws.ArgumentNullException);
    }

    [Test]
    public void ApplyDefaults_DoesNotReturnSameArrayInstanceAsBotRequired()
    {
        var settings = new AppSettings();
        settings.Twitch.BotAccount.Scopes = [];

        AppSettingsHydrator.ApplyDefaults(settings);

        Assert.That(settings.Twitch.BotAccount.Scopes, Is.Not.SameAs(TwitchScopes.BotRequired),
            "Гидратор не должен раздавать ссылку на TwitchScopes.BotRequired — иначе мутация Scopes из UI повредит источник правды");
    }
}
