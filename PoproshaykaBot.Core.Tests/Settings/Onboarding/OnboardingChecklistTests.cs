using PoproshaykaBot.Core.Platforms;
using PoproshaykaBot.Core.Platforms.Auth;
using PoproshaykaBot.Core.Settings;
using PoproshaykaBot.Core.Settings.Onboarding;
using PoproshaykaBot.Core.Settings.Stores;
using PoproshaykaBot.Core.Twitch.Chat;
using PoproshaykaBot.Core.YouTube;
using PoproshaykaBot.Core.YouTube.Auth;

namespace PoproshaykaBot.Core.Tests.Settings.Onboarding;

[TestFixture]
public sealed class OnboardingChecklistTests
{
    private DirectoryInfo _directory = null!;

    [SetUp]
    public void SetUp()
    {
        _directory = Directory.CreateTempSubdirectory("onboarding-checklist-tests");
    }

    [TearDown]
    public void TearDown()
    {
        _directory.Delete(recursive: true);
    }

    [TestCaseSource(nameof(ChecklistCases))]
    public void EvaluatesMultistreamReadiness(ChecklistCase testCase)
    {
        var settingsManager = CreateSettingsManager();
        settingsManager.SaveSettings(new AppSettings
        {
            Twitch = new TwitchSettings
            {
                Enabled = testCase.TwitchEnabled,
                ClientId = testCase.TwitchClientId,
                ClientSecret = testCase.TwitchClientSecret,
                Channel = testCase.TwitchChannel,
                RedirectUri = testCase.TwitchRedirectUri,
            },
        });

        var accountsStore = new AccountsStore(filePath: Path.Combine(_directory.FullName, "accounts.json"));
        if (testCase.TwitchBotAuthorized)
        {
            SeedTwitchBot(accountsStore, testCase.TwitchBotSeedContextId);
        }

        var youtubeSettings = new YouTubeSettingsStore(filePath: Path.Combine(_directory.FullName, "youtube.json"));
        youtubeSettings.Save(new YouTubeSettings
        {
            Enabled = testCase.YouTubeEnabled,
            ClientId = testCase.YouTubeClientId,
            Channel = testCase.YouTubeChannel,
        });

        var youtubeAccounts = new YouTubeAccountsStore(filePath: Path.Combine(_directory.FullName, "youtube-accounts.json"));
        if (testCase.YouTubeBotAuthorized)
        {
            SeedYouTubeBot(youtubeAccounts, testCase.YouTubeBotSeedContextId);
        }

        var catalog = new ChannelCatalogStore(filePath: Path.Combine(_directory.FullName, "channels.json"));
        var catalogEntries = new List<ChannelCatalogEntry>();
        if (!string.IsNullOrWhiteSpace(testCase.TwitchCatalogBotContextId))
        {
            catalogEntries.Add(new ChannelCatalogEntry
            {
                Platform = StreamingPlatform.Twitch,
                ChannelId = "1001",
                Handle = "twitch_catalog",
                IsConfigured = true,
                BotContextId = testCase.TwitchCatalogBotContextId,
            });
        }

        if (!string.IsNullOrWhiteSpace(testCase.YouTubeCatalogBotContextId))
        {
            catalogEntries.Add(new ChannelCatalogEntry
            {
                Platform = StreamingPlatform.YouTube,
                ChannelId = "UC_CATALOG",
                Handle = "@yt_catalog",
                IsConfigured = true,
                BotContextId = testCase.YouTubeCatalogBotContextId,
            });
        }

        if (catalogEntries.Count > 0)
        {
            catalog.Save(new() { Channels = catalogEntries });
        }

        var checklist = new OnboardingChecklist(
            settingsManager,
            accountsStore,
            catalogEntries.Count > 0 ? catalog : null,
            authorization: null,
            youtubeSettings,
            youtubeAccounts);

        var missing = checklist.GetMissingItems();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(checklist.RequiresWizard, Is.EqualTo(testCase.ExpectedRequiresWizard));
            Assert.That(checklist.FirstMissingStep, Is.EqualTo(testCase.ExpectedFirstMissingStep));
            Assert.That(checklist.IsComplete, Is.EqualTo(testCase.ExpectedIsComplete));
            Assert.That(missing, Is.EqualTo(testCase.ExpectedMissing));
        }
    }

    [Test]
    public void NeitherEnabled_WhenYouTubeStoreIsNull_RequiresPlatforms()
    {
        var settingsManager = CreateSettingsManager();
        settingsManager.SaveSettings(new AppSettings
        {
            Twitch = new TwitchSettings { Enabled = false, ClientId = "id", ClientSecret = "secret" },
        });
        var accountsStore = new AccountsStore(filePath: Path.Combine(_directory.FullName, "accounts-null-yt.json"));

        var checklist = new OnboardingChecklist(settingsManager, accountsStore);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(checklist.RequiresWizard, Is.True);
            Assert.That(checklist.FirstMissingStep, Is.EqualTo(OnboardingStep.Platforms));
            Assert.That(checklist.IsComplete, Is.False);
            Assert.That(checklist.GetMissingItems(), Is.EqualTo(new[] { "площадки трансляции" }));
        }
    }

    [Test]
    public void FreshTwitchSettings_DefaultsToEmptyChannelAndRequiresChannel()
    {
        var fresh = new TwitchSettings();
        var settingsManager = CreateSettingsManager();
        settingsManager.SaveSettings(new AppSettings
        {
            Twitch = new TwitchSettings
            {
                Enabled = true,
                ClientId = "tw-id",
                ClientSecret = "tw-secret",
            },
        });
        var accountsStore = new AccountsStore(filePath: Path.Combine(_directory.FullName, "accounts-fresh.json"));
        SeedTwitchBot(accountsStore, null);

        var checklist = new OnboardingChecklist(settingsManager, accountsStore);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(fresh.Channel, Is.Empty);
            Assert.That(checklist.IsComplete, Is.False);
            Assert.That(checklist.FirstMissingStep, Is.EqualTo(OnboardingStep.Credentials));
            Assert.That(checklist.GetMissingItems(), Is.EqualTo(new[] { "канал" }));
        }
    }

    [TestCase(true, false)]
    [TestCase(false, true)]
    public void YouTubeOnly_UnusableBotAuthorization_RequiresYouTubeStep(bool readOnlyScope, bool expiredWithoutRefresh)
    {
        var settingsManager = CreateSettingsManager();
        settingsManager.SaveSettings(new AppSettings { Twitch = new TwitchSettings { Enabled = false } });
        var accountsStore = new AccountsStore(filePath: Path.Combine(_directory.FullName, "accounts-yt-unusable.json"));
        var youtubeSettings = new YouTubeSettingsStore(filePath: Path.Combine(_directory.FullName, "youtube-unusable.json"));
        youtubeSettings.Save(new YouTubeSettings
        {
            Enabled = true,
            ClientId = "yt-id",
            Channel = "UC1234567890123456789012",
        });
        var youtubeAccounts = new YouTubeAccountsStore(filePath: Path.Combine(_directory.FullName, "youtube-accounts-unusable.json"));
        youtubeAccounts.Mutate(PlatformAuthRole.Bot, account =>
        {
            account.ChannelId = "UC_BOT";
            account.AccessToken = "yt-access";
            account.RefreshToken = expiredWithoutRefresh ? string.Empty : "yt-refresh";
            account.AccessTokenExpiresAt = expiredWithoutRefresh
                ? DateTimeOffset.UtcNow.AddMinutes(-10)
                : DateTimeOffset.UtcNow.AddHours(1);
            account.Scopes = readOnlyScope ? [YouTubeScopes.ReadOnly] : [.. YouTubeScopes.BotRequired];
            account.StoredScopes = readOnlyScope ? [YouTubeScopes.ReadOnly] : [.. YouTubeScopes.BotRequired];
        });

        var checklist = new OnboardingChecklist(
            settingsManager,
            accountsStore,
            youtubeSettings: youtubeSettings,
            youtubeAccounts: youtubeAccounts);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(checklist.IsComplete, Is.False);
            Assert.That(checklist.FirstMissingStep, Is.EqualTo(OnboardingStep.YouTube));
            Assert.That(checklist.GetMissingItems(), Is.EqualTo(new[] { "авторизация бота YouTube" }));
        }
    }

    private SettingsManager CreateSettingsManager() =>
        new(NullLogger<SettingsManager>.Instance, Path.Combine(_directory.FullName, "settings.json"));

    private static void SeedTwitchBot(AccountsStore store, string? contextId)
    {
        store.Mutate(PlatformAuthRole.Bot, contextId, account =>
        {
            account.UserId = "bot-1";
            account.Login = "twbot";
            account.AccessToken = "access-token";
            account.RefreshToken = "refresh-token";
            account.AccessTokenExpiresAt = DateTimeOffset.UtcNow.AddHours(1);
            account.Scopes = [.. TwitchScopes.BotRequired];
            account.StoredScopes = [.. TwitchScopes.BotRequired];
        });
    }

    private static void SeedYouTubeBot(YouTubeAccountsStore store, string? contextId)
    {
        store.Mutate(PlatformAuthRole.Bot, contextId, account =>
        {
            account.ChannelId = "UC_BOT";
            account.ChannelTitle = "YtBot";
            account.AccessToken = "yt-access";
            account.RefreshToken = "yt-refresh";
            account.Scopes = [.. YouTubeScopes.BotRequired];
            account.StoredScopes = [.. YouTubeScopes.BotRequired];
        });
    }

    private static IEnumerable<ChecklistCase> ChecklistCases()
    {
        yield return new ChecklistCase("NeitherEnabled_WithStoredData")
        {
            TwitchEnabled = false,
            TwitchClientId = "tw-id",
            TwitchClientSecret = "tw-secret",
            TwitchBotAuthorized = true,
            YouTubeEnabled = false,
            YouTubeClientId = "yt-id",
            YouTubeBotAuthorized = true,
            ExpectedRequiresWizard = true,
            ExpectedFirstMissingStep = OnboardingStep.Platforms,
            ExpectedIsComplete = false,
            ExpectedMissing = ["площадки трансляции"],
        };

        yield return new ChecklistCase("TwitchOnly_Empty")
        {
            TwitchEnabled = true,
            TwitchClientId = string.Empty,
            TwitchClientSecret = string.Empty,
            TwitchChannel = string.Empty,
            TwitchRedirectUri = "invalid",
            TwitchBotAuthorized = false,
            YouTubeEnabled = false,
            ExpectedRequiresWizard = true,
            ExpectedFirstMissingStep = OnboardingStep.Credentials,
            ExpectedIsComplete = false,
            ExpectedMissing = ["Client ID/Secret", "канал", "адрес перенаправления Twitch", "авторизация бота"],
        };

        yield return new ChecklistCase("TwitchOnly_Partial_MissingBotAuth")
        {
            TwitchEnabled = true,
            TwitchClientId = "tw-id",
            TwitchClientSecret = "tw-secret",
            TwitchChannel = "streamer",
            TwitchBotAuthorized = false,
            YouTubeEnabled = false,
            ExpectedRequiresWizard = false,
            ExpectedFirstMissingStep = OnboardingStep.BotAuthorization,
            ExpectedIsComplete = false,
            ExpectedMissing = ["авторизация бота"],
        };

        yield return new ChecklistCase("TwitchOnly_Complete")
        {
            TwitchEnabled = true,
            TwitchClientId = "tw-id",
            TwitchClientSecret = "tw-secret",
            TwitchChannel = "streamer",
            TwitchBotAuthorized = true,
            YouTubeEnabled = false,
            ExpectedRequiresWizard = false,
            ExpectedFirstMissingStep = OnboardingStep.None,
            ExpectedIsComplete = true,
            ExpectedMissing = [],
        };

        yield return new ChecklistCase("TwitchOnly_NamedBotContext_MissingNamedAuth")
        {
            TwitchEnabled = true,
            TwitchClientId = "tw-id",
            TwitchClientSecret = "tw-secret",
            TwitchChannel = string.Empty,
            TwitchBotAuthorized = true,
            TwitchBotSeedContextId = AuthContextIds.Default,
            TwitchCatalogBotContextId = "bot-2",
            YouTubeEnabled = false,
            ExpectedRequiresWizard = false,
            ExpectedFirstMissingStep = OnboardingStep.BotAuthorization,
            ExpectedIsComplete = false,
            ExpectedMissing = ["авторизация бота"],
        };

        yield return new ChecklistCase("TwitchOnly_NamedBotContext_Complete")
        {
            TwitchEnabled = true,
            TwitchClientId = "tw-id",
            TwitchClientSecret = "tw-secret",
            TwitchChannel = string.Empty,
            TwitchBotAuthorized = true,
            TwitchBotSeedContextId = "bot-2",
            TwitchCatalogBotContextId = "bot-2",
            YouTubeEnabled = false,
            ExpectedRequiresWizard = false,
            ExpectedFirstMissingStep = OnboardingStep.None,
            ExpectedIsComplete = true,
            ExpectedMissing = [],
        };

        yield return new ChecklistCase("YouTubeOnly_Empty_IgnoresMissingTwitch")
        {
            TwitchEnabled = false,
            TwitchClientId = string.Empty,
            TwitchClientSecret = string.Empty,
            TwitchChannel = string.Empty,
            TwitchBotAuthorized = false,
            YouTubeEnabled = true,
            YouTubeClientId = string.Empty,
            YouTubeChannel = string.Empty,
            YouTubeBotAuthorized = false,
            ExpectedRequiresWizard = true,
            ExpectedFirstMissingStep = OnboardingStep.YouTube,
            ExpectedIsComplete = false,
            ExpectedMissing = ["Client ID YouTube", "канал YouTube", "авторизация бота YouTube"],
        };

        yield return new ChecklistCase("YouTubeOnly_Partial_MissingBotAuth")
        {
            TwitchEnabled = false,
            YouTubeEnabled = true,
            YouTubeClientId = "yt-id",
            YouTubeChannel = "@channel",
            YouTubeBotAuthorized = false,
            ExpectedRequiresWizard = false,
            ExpectedFirstMissingStep = OnboardingStep.YouTube,
            ExpectedIsComplete = false,
            ExpectedMissing = ["авторизация бота YouTube"],
        };

        yield return new ChecklistCase("YouTubeOnly_Complete_WithoutTwitch")
        {
            TwitchEnabled = false,
            TwitchClientId = string.Empty,
            TwitchClientSecret = string.Empty,
            TwitchChannel = string.Empty,
            TwitchBotAuthorized = false,
            YouTubeEnabled = true,
            YouTubeClientId = "yt-id",
            YouTubeChannel = "@channel",
            YouTubeBotAuthorized = true,
            ExpectedRequiresWizard = false,
            ExpectedFirstMissingStep = OnboardingStep.None,
            ExpectedIsComplete = true,
            ExpectedMissing = [],
        };

        yield return new ChecklistCase("YouTubeOnly_NamedBotContext_MissingNamedAuth")
        {
            TwitchEnabled = false,
            YouTubeEnabled = true,
            YouTubeClientId = "yt-id",
            YouTubeChannel = string.Empty,
            YouTubeBotAuthorized = true,
            YouTubeBotSeedContextId = AuthContextIds.Default,
            YouTubeCatalogBotContextId = "yt-bot-2",
            ExpectedRequiresWizard = false,
            ExpectedFirstMissingStep = OnboardingStep.YouTube,
            ExpectedIsComplete = false,
            ExpectedMissing = ["авторизация бота YouTube"],
        };

        yield return new ChecklistCase("YouTubeOnly_NamedBotContext_Complete")
        {
            TwitchEnabled = false,
            YouTubeEnabled = true,
            YouTubeClientId = "yt-id",
            YouTubeChannel = string.Empty,
            YouTubeBotAuthorized = true,
            YouTubeBotSeedContextId = "yt-bot-2",
            YouTubeCatalogBotContextId = "yt-bot-2",
            ExpectedRequiresWizard = false,
            ExpectedFirstMissingStep = OnboardingStep.None,
            ExpectedIsComplete = true,
            ExpectedMissing = [],
        };

        yield return new ChecklistCase("Multistream_BothEmpty")
        {
            TwitchEnabled = true,
            TwitchClientId = string.Empty,
            TwitchClientSecret = string.Empty,
            TwitchChannel = string.Empty,
            TwitchBotAuthorized = false,
            YouTubeEnabled = true,
            YouTubeClientId = string.Empty,
            YouTubeChannel = string.Empty,
            YouTubeBotAuthorized = false,
            ExpectedRequiresWizard = true,
            ExpectedFirstMissingStep = OnboardingStep.Credentials,
            ExpectedIsComplete = false,
            ExpectedMissing = ["Client ID/Secret", "канал", "авторизация бота", "Client ID YouTube", "канал YouTube", "авторизация бота YouTube"],
        };

        yield return new ChecklistCase("Multistream_TwitchIncomplete_YouTubeComplete_DoesNotRequireWizard")
        {
            TwitchEnabled = true,
            TwitchClientId = string.Empty,
            TwitchClientSecret = string.Empty,
            TwitchChannel = "streamer",
            TwitchBotAuthorized = false,
            YouTubeEnabled = true,
            YouTubeClientId = "yt-id",
            YouTubeChannel = "@channel",
            YouTubeBotAuthorized = true,
            ExpectedRequiresWizard = false,
            ExpectedFirstMissingStep = OnboardingStep.Credentials,
            ExpectedIsComplete = false,
            ExpectedMissing = ["Client ID/Secret", "авторизация бота"],
        };

        yield return new ChecklistCase("Multistream_TwitchComplete_YouTubeIncomplete")
        {
            TwitchEnabled = true,
            TwitchClientId = "tw-id",
            TwitchClientSecret = "tw-secret",
            TwitchChannel = "streamer",
            TwitchBotAuthorized = true,
            YouTubeEnabled = true,
            YouTubeClientId = "yt-id",
            YouTubeChannel = "@channel",
            YouTubeBotAuthorized = false,
            ExpectedRequiresWizard = false,
            ExpectedFirstMissingStep = OnboardingStep.YouTube,
            ExpectedIsComplete = false,
            ExpectedMissing = ["авторизация бота YouTube"],
        };

        yield return new ChecklistCase("Multistream_BothComplete")
        {
            TwitchEnabled = true,
            TwitchClientId = "tw-id",
            TwitchClientSecret = "tw-secret",
            TwitchChannel = "streamer",
            TwitchBotAuthorized = true,
            YouTubeEnabled = true,
            YouTubeClientId = "yt-id",
            YouTubeChannel = "@channel",
            YouTubeBotAuthorized = true,
            ExpectedRequiresWizard = false,
            ExpectedFirstMissingStep = OnboardingStep.None,
            ExpectedIsComplete = true,
            ExpectedMissing = [],
        };
    }

    public sealed record ChecklistCase(string Name)
    {
        public bool TwitchEnabled { get; init; }
        public string TwitchClientId { get; init; } = string.Empty;
        public string TwitchClientSecret { get; init; } = string.Empty;
        public string TwitchChannel { get; init; } = "bobito217";
        public string TwitchRedirectUri { get; init; } = "http://localhost:8080";
        public bool TwitchBotAuthorized { get; init; }
        public string? TwitchBotSeedContextId { get; init; }
        public string? TwitchCatalogBotContextId { get; init; }
        public bool YouTubeEnabled { get; init; }
        public string YouTubeClientId { get; init; } = string.Empty;
        public string YouTubeChannel { get; init; } = string.Empty;
        public bool YouTubeBotAuthorized { get; init; }
        public string? YouTubeBotSeedContextId { get; init; }
        public string? YouTubeCatalogBotContextId { get; init; }
        public bool ExpectedRequiresWizard { get; init; }
        public OnboardingStep ExpectedFirstMissingStep { get; init; }
        public bool ExpectedIsComplete { get; init; }
        public IReadOnlyList<string> ExpectedMissing { get; init; } = [];

        public override string ToString() => Name;
    }
}
