using KeepShell.Diagnostics;
using KeepShell.Mcp;
using KeepShell.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using PoproshaykaBot.Core.Debugging;
using PoproshaykaBot.Core.Infrastructure.Diagnostics;
using PoproshaykaBot.Core.Infrastructure.Events;
using PoproshaykaBot.Core.Platforms.Auth;
using PoproshaykaBot.Core.Settings;
using PoproshaykaBot.Core.Settings.Migrations.LegacyImport;
using PoproshaykaBot.Core.Settings.Obs;
using PoproshaykaBot.Core.Settings.Stores;
using PoproshaykaBot.Core.Twitch;
using PoproshaykaBot.Core.YouTube;
using PoproshaykaBot.Wpf.Infrastructure.Diagnostics;
using System.IO;
using System.Reflection;

namespace PoproshaykaBot.Wpf.Tests.Diagnostics;

[TestFixture]
public class DiagnosticsSecretSourceTests
{
    private const string ClientSecret = "client-secret-0123456789";
    private const string BotAccessToken = "bot-access-0123456789";
    private const string BotRefreshToken = "bot-refresh-0123456789";
    private const string BroadcasterAccessToken = "broadcaster-access-0123456789";
    private const string YouTubeClientSecret = "yt-client-secret-0123456789";
    private const string YouTubeBotAccessToken = "yt-bot-access-0123456789";
    private const string YouTubeBotRefreshToken = "yt-bot-refresh-0123456789";
    private const string YouTubeBroadcasterAccessToken = "yt-caster-access-0123456789";
    private const string ObsPassword = "obs-password-0123456789";

    private string _directory = string.Empty;

    [SetUp]
    public void SetUp()
    {
        _directory = Path.Combine(Path.GetTempPath(), "PoproshaykaBot-secrets-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_directory);
    }

    [TearDown]
    public void TearDown()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }

    [Test]
    public void Живые_секреты_вымарываются_из_пакета()
    {
        var secrets = new DiagnosticsSecrets(DiagnosticsSecretRules.Merge([DiagnosticsSecretRules.Common, Create().Collect()]));

        var text = secrets.Redact(
            $"токен {BotAccessToken} уехал в строку журнала без имени поля"
            + Environment.NewLine
            + $"\"ClientSecret\": \"{ClientSecret}\", \"Password\": \"{ObsPassword}\", \"RefreshToken\": \"{BotRefreshToken}\""
            + Environment.NewLine
            + $"broadcaster={BroadcasterAccessToken}, yt={YouTubeClientSecret} {YouTubeBotAccessToken} {YouTubeBotRefreshToken} {YouTubeBroadcasterAccessToken}");

        using (Assert.EnterMultipleScope())
        {
            Assert.That(text, Does.Not.Contain(ClientSecret));
            Assert.That(text, Does.Not.Contain(BotAccessToken), "Токен без имени поля ловится только по значению – ради этого источник их и отдаёт");
            Assert.That(text, Does.Not.Contain(BotRefreshToken));
            Assert.That(text, Does.Not.Contain(BroadcasterAccessToken));
            Assert.That(text, Does.Not.Contain(YouTubeClientSecret));
            Assert.That(text, Does.Not.Contain(YouTubeBotAccessToken));
            Assert.That(text, Does.Not.Contain(YouTubeBotRefreshToken));
            Assert.That(text, Does.Not.Contain(YouTubeBroadcasterAccessToken));
            Assert.That(text, Does.Not.Contain(ObsPassword));
            Assert.That(text, Does.Contain("уехал в строку журнала"), "Вымарывание не должно съедать соседний текст");
        }
    }

    [Test]
    public void Отказ_одного_источника_не_уносит_прочитанные_секреты()
    {
        var settings = new ThrowingSettingsManager(Path.Combine(_directory, "settings.json"));
        var source = new BotDiagnosticsSecretSource(
            CreateProviders(settings, CreateAccounts(), CreateYouTubeAccounts(), CreateObs()),
            NullLogger<BotDiagnosticsSecretSource>.Instance);

        var rules = source.Collect();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(rules.Complete, Is.False, "Пакет не вправе обещать вымарывание, которого не было");
            Assert.That(rules.Values, Does.Contain(BotAccessToken), "Токен прочитался – его вымарывают, даже если соседний источник отказал");
            Assert.That(rules.Values, Does.Contain(BroadcasterAccessToken));
            Assert.That(rules.Values, Does.Contain(YouTubeClientSecret));
            Assert.That(rules.Values, Does.Contain(YouTubeBotAccessToken));
            Assert.That(rules.Values, Does.Contain(ObsPassword));
            Assert.That(rules.Values, Does.Not.Contain(ClientSecret));
        }
    }

    [Test]
    public void LegacyDataCatalog_CarriesTokens_Entries_Match_Di_SecretProviders()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IEventBus>(new InMemoryEventBus(NullLogger<InMemoryEventBus>.Instance));
        services.AddSingleton(DispatchProxy.Create<ITargetChannelProvider, InertProxy>());
        services.AddSettingsStores();
        services.AddTwitchClients();
        services.AddYouTubeClients();

        using var provider = services.BuildServiceProvider();
        var providerSources = provider.GetServices<ISecretValueProvider>()
            .Select(static item => item.SourceName)
            .ToArray();

        var tokenFiles = LegacyDataCatalog.AllEntries
            .Where(static entry => entry.CarriesTokens)
            .Select(static entry => entry.FileName)
            .ToArray();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(tokenFiles, Is.EquivalentTo(new[]
            {
                SettingsFileNames.SettingsFileName,
                SettingsFileNames.AccountsFileName,
                SettingsFileNames.YouTubeAccountsFileName,
            }));

            Assert.That(providerSources, Has.Some.EqualTo("настройки Twitch"));
            Assert.That(providerSources, Has.Some.EqualTo("настройки YouTube"));
            Assert.That(providerSources, Has.Some.EqualTo("настройки OBS"));
            Assert.That(providerSources, Has.Some.StartsWith("токены роли"));
            Assert.That(providerSources, Has.Some.StartsWith("токены YouTube роли"));
        }
    }

    [Test]
    public void McpPreferences_SecretProvider_RedactsBothPersistedAndRuntimeOverrideTokens()
    {
        const string persistedToken = "mcp-persisted-secret-0123456789ab";
        const string runtimeToken = "mcp-runtime-override-fedcba987654";

        var options = new McpServerOptions
        {
            ServerName = "poproshaykabot",
            AppName = "PoproshaykaBot",
            AppVersion = "3.1.0.15",
            DefaultPort = 7656,
            ShotsDirectory = _directory,
        };
        var settings = new MemorySettings();
        var preferences = new McpPreferences(settings, options, isElevated: false)
        {
            Token = persistedToken,
        };
        preferences.ApplyRuntimeOverrides(token: runtimeToken);

        ISecretValueProvider mcpProvider = new DelegateSecretValueProvider(
            "настройки MCP",
            () => [preferences.Token, preferences.EffectiveToken]);

        var source = new BotDiagnosticsSecretSource([mcpProvider], NullLogger<BotDiagnosticsSecretSource>.Instance);
        var secrets = new DiagnosticsSecrets(DiagnosticsSecretRules.Merge([DiagnosticsSecretRules.Common, source.Collect()]));

        var redacted = secrets.Redact(
            $"persisted={persistedToken}; runtime={runtimeToken}; note=сохранить-соседний-текст");

        using (Assert.EnterMultipleScope())
        {
            Assert.That(redacted, Does.Not.Contain(persistedToken));
            Assert.That(redacted, Does.Not.Contain(runtimeToken));
            Assert.That(redacted, Does.Contain("сохранить-соседний-текст"));
        }
    }

    private BotDiagnosticsSecretSource Create()
    {
        var settings = new SettingsManager(NullLogger<SettingsManager>.Instance, Path.Combine(_directory, "settings.json"));

        settings.Mutate(current => current.Twitch.ClientSecret = ClientSecret);

        return new(
            CreateProviders(settings, CreateAccounts(), CreateYouTubeAccounts(), CreateObs()),
            NullLogger<BotDiagnosticsSecretSource>.Instance);
    }

    private static IReadOnlyList<ISecretValueProvider> CreateProviders(
        SettingsManager settings,
        AccountsStore accounts,
        YouTubeAccountsStore youtubeAccounts,
        ObsIntegrationStore obs)
    {
        var providers = new List<ISecretValueProvider>
        {
            new DelegateSecretValueProvider("настройки Twitch", () => [settings.Current.Twitch.ClientSecret]),
            new DelegateSecretValueProvider("настройки YouTube", () => [youtubeAccounts.LoadClientSecret()]),
            new DelegateSecretValueProvider("настройки OBS", () => [obs.Load().Password]),
        };

        foreach (var role in PlatformAuthRoles.Active)
        {
            providers.Add(new DelegateSecretValueProvider($"токены роли {role}", () =>
            {
                var account = accounts.Load(role);
                return [account.AccessToken, account.RefreshToken];
            }));

            providers.Add(new DelegateSecretValueProvider($"токены YouTube роли {role}", () =>
            {
                var account = youtubeAccounts.Load(role);
                return [account.AccessToken, account.RefreshToken];
            }));
        }

        return providers;
    }

    private AccountsStore CreateAccounts()
    {
        var accounts = new AccountsStore(filePath: Path.Combine(_directory, "accounts.json"));

        accounts.SaveAll(
            new()
            {
                Login = "bot",
                AccessToken = BotAccessToken,
                RefreshToken = BotRefreshToken,
            },
            new()
            {
                Login = "broadcaster",
                AccessToken = BroadcasterAccessToken,
                RefreshToken = string.Empty,
            });

        return accounts;
    }

    private YouTubeAccountsStore CreateYouTubeAccounts()
    {
        var accounts = new YouTubeAccountsStore(filePath: Path.Combine(_directory, "youtube-accounts.json"));

        accounts.SaveAll(
            YouTubeClientSecret,
            new()
            {
                ChannelId = "UCbot",
                AccessToken = YouTubeBotAccessToken,
                RefreshToken = YouTubeBotRefreshToken,
            },
            new()
            {
                ChannelId = "UCcaster",
                AccessToken = YouTubeBroadcasterAccessToken,
                RefreshToken = string.Empty,
            });

        return accounts;
    }

    private ObsIntegrationStore CreateObs()
    {
        var obs = new ObsIntegrationStore(new InMemoryEventBus(NullLogger<InMemoryEventBus>.Instance),
            filePath: Path.Combine(_directory, "obs-integration.json"));

        obs.Save(new ObsIntegrationSettings { Password = ObsPassword });

        return obs;
    }

    private sealed class ThrowingSettingsManager(string filePath)
        : SettingsManager(NullLogger<SettingsManager>.Instance, filePath)
    {
        public override AppSettings Current => throw new InvalidOperationException("настройки прочитать не удалось");
    }

    private class InertProxy : DispatchProxy
    {
        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args) => null;
    }
}
