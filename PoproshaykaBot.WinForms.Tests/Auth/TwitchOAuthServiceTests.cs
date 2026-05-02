using PoproshaykaBot.WinForms.Auth;
using PoproshaykaBot.WinForms.Infrastructure.Events;
using PoproshaykaBot.WinForms.Infrastructure.Events.Lifecycle;
using PoproshaykaBot.WinForms.Settings;
using PoproshaykaBot.WinForms.Settings.Stores;

namespace PoproshaykaBot.WinForms.Tests.Auth;

[TestFixture]
public sealed class TwitchOAuthServiceTests
{
    [SetUp]
    public void SetUp()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "twitch-oauth-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
        _settings = new();
        _settingsManager = Substitute.For<SettingsManager>(NullLogger<SettingsManager>.Instance);
        _settingsManager.Current.Returns(_settings);
        _accountsStore = new(filePath: Path.Combine(_tempDir, "accounts.json"));

        _httpFactory = Substitute.For<IHttpClientFactory>();
        _eventBus = Substitute.For<IEventBus>();

        _service = new(_settingsManager, _accountsStore, _httpFactory, NullLogger<TwitchOAuthService>.Instance, _eventBus);
    }

    [TearDown]
    public void TearDown()
    {
        _service.Dispose();
        try
        {
            Directory.Delete(_tempDir, true);
        }
        catch
        {
        }
    }

    private string _tempDir = null!;
    private AppSettings _settings = null!;
    private SettingsManager _settingsManager = null!;
    private AccountsStore _accountsStore = null!;
    private IHttpClientFactory _httpFactory = null!;
    private IEventBus _eventBus = null!;
    private TwitchOAuthService _service = null!;

    [Test]
    public async Task UpdateSettings_DefaultsToNotPublishingAuthorizationRefreshed()
    {
        _service.UpdateSettings(TwitchOAuthRole.Bot,
            "access-token",
            "refresh-token",
            null,
            "bot-login",
            "user-id");

        await _eventBus.DidNotReceive()
            .PublishAsync(Arg.Any<TwitchAuthorizationRefreshed>(), Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task UpdateSettings_WithFlag_PublishesAuthorizationRefreshedForRole()
    {
        _service.UpdateSettings(TwitchOAuthRole.Bot,
            "access-token",
            "refresh-token",
            null,
            "bot-login",
            "user-id",
            publishAuthorizationRefreshed: true);

        await _eventBus.Received(1)
            .PublishAsync(Arg.Is<TwitchAuthorizationRefreshed>(e => e.Role == TwitchOAuthRole.Bot),
                Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task UpdateSettings_WithFlagForBroadcaster_PublishesEventForBroadcaster()
    {
        _service.UpdateSettings(TwitchOAuthRole.Broadcaster,
            "access-token",
            "refresh-token",
            null,
            "streamer-login",
            "user-id",
            publishAuthorizationRefreshed: true);

        await _eventBus.Received(1)
            .PublishAsync(Arg.Is<TwitchAuthorizationRefreshed>(e => e.Role == TwitchOAuthRole.Broadcaster),
                Arg.Any<CancellationToken>());
    }
}
