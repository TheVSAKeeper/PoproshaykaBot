using Microsoft.Extensions.DependencyInjection;
using PoproshaykaBot.Core.Broadcast.Profiles;
using PoproshaykaBot.Core.Debugging;
using PoproshaykaBot.Core.Infrastructure.Diagnostics;
using PoproshaykaBot.Core.Infrastructure.Events;
using PoproshaykaBot.Core.Polls;
using PoproshaykaBot.Core.Settings;
using PoproshaykaBot.Core.Settings.Debugging;
using PoproshaykaBot.Core.Settings.Obs;
using PoproshaykaBot.Core.Settings.Stores;
using PoproshaykaBot.Core.Settings.Update;
using PoproshaykaBot.Core.Twitch;
using PoproshaykaBot.Core.Twitch.Auth;
using PoproshaykaBot.Core.YouTube;
using PoproshaykaBot.Core.YouTube.Auth;

namespace PoproshaykaBot.Core.Tests.Settings;

[TestFixture]
public sealed class SettingsDescriberTests
{
    private static readonly string[] _sentinels = ["SECRET-1", "SECRET-2", "SECRET-3", "SECRET-4"];

    [TestCaseSource(nameof(GetDescriptionCases))]
    public void Describe_KeepsSecretsOut(DescriptionCase testCase)
    {
        Assert.That(testCase.Description, Does.Not.Contain("SECRET"));

        foreach (var sentinel in _sentinels)
        {
            Assert.That(testCase.Description, Does.Not.Contain(sentinel));
        }
    }

    [TestCaseSource(nameof(GetDescriptionCases))]
    public void Describe_CarriesKeyValues(DescriptionCase testCase)
    {
        foreach (var expected in testCase.Expected)
        {
            Assert.That(testCase.Description, Does.Contain(expected));
        }
    }

    private static IEnumerable<DescriptionCase> GetDescriptionCases()
    {
        yield return new("AppSettings",
            SettingsDescriber.Describe(CreateAppSettings()),
            ["testchannel", "clientId есть", "clientSecret есть", "порт 9099", "лимит 111", "приветствие вкл", "прощание выкл", "каждые 42 мин", "автоподключение на онлайн вкл", "пользователей 2", "рангов 3"]);

        yield return new("ChatSettings",
            ChatSettingsStore.Describe(new()
            {
                ShowHistoryInApp = true,
                ShowHistoryInObs = false,
                HistoryMaxItems = 250,
            }),
            ["история в окне бота вкл", "история в OBS выкл", "лимит кэша 250"]);

        yield return new("TwitchAccountSettings",
            AccountsStore.Describe(CreateAccount()),
            ["логин botlogin", "userId 4242", "access-токен есть", "refresh-токен есть", "скоупов 2"]);

        yield return new("ObsIntegrationSettings",
            ObsIntegrationStore.Describe(CreateObsIntegrationSettings()),
            ["интеграция вкл", "obs-host:4466", "пароль есть", "сцена MainScene", "источников дашборда 2"]);

        yield return new("ObsChatSettings",
            ObsChatStore.Describe(new ObsChatSettings { MaxMessages = 77, ShowTimestamp = false, MessageLifetimeSeconds = 88 }),
            ["сообщений на экране 77", "время выкл", "через 88 с"]);

        yield return new("UpdateSettings",
            UpdateStore.Describe(new UpdateSettings
            {
                AutoCheckEnabled = false,
                CheckIntervalHours = 13,
                RepositoryOverride = "owner/fork",
                SkippedVersion = "9.9.9",
                AllowFrameworkDependentUpdate = true,
            }),
            ["автопроверка выкл", "каждые 13 ч", "owner/fork", "9.9.9", "framework-dependent вкл"]);

        yield return new("DebugChannelSettings",
            DebugChannelStore.Describe(new DebugChannelSettings { IsEnabled = true, Channel = "mrbeast", AllowSending = true }),
            ["отладочный канал вкл", "mrbeast", "отправка сообщений вкл"]);

        yield return new("BroadcastProfilesSettings",
            BroadcastProfilesStore.Describe(new BroadcastProfilesSettings { Profiles = [new(), new(), new()] }),
            ["профилей 3"]);

        yield return new("PollsSettings",
            PollsStore.Describe(new PollsSettings
            {
                ChatTemplates = new() { StartEnabled = true, ProgressEnabled = false, ProgressAnnounceIntervalSeconds = 45 },
                HistoryMaxItems = 33,
            }),
            ["объявление старта вкл", "прогресс выкл каждые 45 с", "история до 33"]);

        yield return new("YouTubeSettings",
            YouTubeSettingsStore.Describe(new YouTubeSettings
            {
                Enabled = true,
                ProjectId = "yt-proj-1",
                ClientId = "yt-client-id",
                Channel = "@mychannel",
                DailyQuotaBudget = 8000,
            }),
            ["включено вкл", "projectId yt-proj-1", "clientId есть", "@mychannel", "бюджет квоты 8000"]);

        yield return new("YouTubeAccounts",
            YouTubeAccountsStore.Describe(
                "SECRET-1",
                new()
                {
                    ChannelTitle = "BotChan",
                    ChannelId = "UC111",
                    AccessToken = "SECRET-2",
                    RefreshToken = "SECRET-3",
                    Scopes = [YouTubeScopes.ForceSsl],
                    StoredScopes = [YouTubeScopes.ForceSsl],
                    AccessTokenExpiresAt = new DateTimeOffset(2026, 9, 7, 10, 0, 0, TimeSpan.Zero),
                },
                new() { ChannelTitle = "BroadcasterChan", ChannelId = "UC222" }),
            ["clientSecret есть", "BotChan", "UC111", "access-токен есть", "refresh-токен есть", "BroadcasterChan", "UC222"]);
    }

    private static AppSettings CreateAppSettings()
    {
        var settings = new AppSettings();
        settings.Twitch.Channel = "testchannel";
        settings.Twitch.ClientId = "client-id-value";
        settings.Twitch.ClientSecret = "SECRET-1";
        settings.Twitch.HttpServerPort = 9099;
        settings.Twitch.RedirectUri = "http://localhost:9099";
        settings.Twitch.MessagesAllowedInPeriod = 111;
        settings.Twitch.ThrottlingPeriodSeconds = 22;
        settings.Twitch.Messages.WelcomeEnabled = true;
        settings.Twitch.Messages.FarewellEnabled = false;
        settings.Twitch.AutoBroadcast.AutoBroadcastEnabled = true;
        settings.Twitch.AutoBroadcast.BroadcastIntervalMinutes = 42;
        settings.Twitch.BotLifecycleAutomation.AutoConnectOnStreamOnline = true;
        settings.SpecialCommands.AllowedUsers = ["one", "two"];
        settings.Ranks.Ranks = [new("A", "A", 300), new("B", "B", 200), new("C", "C", 100)];

        return settings;
    }

    private static TwitchAccountSettings CreateAccount()
    {
        return new()
        {
            Login = "botlogin",
            UserId = "4242",
            AccessToken = "SECRET-3",
            RefreshToken = "SECRET-4",
            Scopes = ["chat:read", "chat:edit"],
            AccessTokenExpiresAt = new DateTimeOffset(2026, 9, 7, 10, 0, 0, TimeSpan.Zero),
        };
    }

    private static ObsIntegrationSettings CreateObsIntegrationSettings()
    {
        return new()
        {
            Enabled = true,
            Host = "obs-host",
            Port = 4466,
            Password = "SECRET-2",
            SceneName = "MainScene",
            DashboardSourceNames = ["mic", "desktop"],
        };
    }

    public sealed record DescriptionCase(string Name, string Description, IReadOnlyList<string> Expected)
    {
        public override string ToString()
        {
            return Name;
        }
    }

    [Test]
    public void SnapshotContributors_AreRegisteredAcrossSettingsAndPlatforms()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(Substitute.For<IEventBus>());
        services.AddSingleton(Substitute.For<ITargetChannelProvider>());
        services.AddSettingsStores();
        services.AddTwitchClients();
        services.AddYouTubeClients();

        using var provider = services.BuildServiceProvider();
        var keys = provider.GetServices<ISettingsSnapshotContributor>()
            .SelectMany(static contributor => contributor.CollectSnapshots())
            .Select(static entry => entry.Key)
            .ToArray();

        Assert.That(keys, Is.EquivalentTo(new[]
        {
            nameof(AppSettings),
            "ChatSettings",
            "ObsIntegrationSettings",
            "ObsChatSettings",
            "UpdateSettings",
            "DebugChannelSettings",
            "BroadcastProfilesSettings",
            "BroadcastProfilePlatformsSettings",
            "BroadcastProfileYouTubeSettings",
            "RecentCategoriesSettings",
            "PollsSettings",
            "CommandSettings",
            "ChannelsSettings",
            "Account.Bot",
            "Account.Broadcaster",
            "Account.Contexts",
            "YouTubeSettings",
            "YouTubeAccounts",
        }));
    }
}
