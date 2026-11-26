using Microsoft.Extensions.Logging.Abstractions;
using PoproshaykaBot.Core.Infrastructure.Events;
using PoproshaykaBot.Core.Settings;
using PoproshaykaBot.Core.Settings.Migrations.LegacyImport;
using PoproshaykaBot.Core.Settings.Onboarding;
using PoproshaykaBot.Core.Settings.Stores;
using PoproshaykaBot.Core.Twitch.Auth;
using PoproshaykaBot.Core.Twitch.Chat;
using PoproshaykaBot.Wpf.Infrastructure;
using PoproshaykaBot.Wpf.ViewModels;
using System.IO;

namespace PoproshaykaBot.Wpf.Tests.Shell;

[TestFixture]
public class OnboardingBannerTests
{
    private string _directory = string.Empty;

    [SetUp]
    public void SetUp()
    {
        _directory = Path.Combine(Path.GetTempPath(), $"poproshayka-banner-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_directory);
    }

    [TearDown]
    public void TearDown()
    {
        try
        {
            Directory.Delete(_directory, recursive: true);
        }
        catch (IOException)
        {
        }
    }

    [Test]
    public void Ненастроенный_профиль_поднимает_баннер()
    {
        using var banner = CreateBanner();

        Assert.Multiple(() =>
        {
            Assert.That(banner.IsVisible, Is.True);
            Assert.That(banner.Text, Does.StartWith("Бот не готов к работе"));
        });
    }

    [Test]
    public void Подавленный_баннер_не_возвращается_обновлением()
    {
        using var banner = CreateBanner();

        banner.Suppress();
        banner.Refresh();

        Assert.That(banner.IsVisible, Is.False);
    }

    [TestCase(false, false, false, false, true, OnboardingStep.None, false)]
    [TestCase(false, false, false, true, true, OnboardingStep.None, true)]
    [TestCase(true, false, false, false, false, OnboardingStep.None, false)]
    [TestCase(true, false, false, true, true, OnboardingStep.None, true)]
    [TestCase(false, true, true, false, false, OnboardingStep.None, false)]
    [TestCase(false, true, false, false, true, OnboardingStep.None, false)]
    public void StartupOnboardingPlan_RoutesByChecklistAndImportOffer(
        bool twitchConfigured,
        bool youtubeEnabled,
        bool youtubeConfigured,
        bool hasImportOffer,
        bool expectedShow,
        OnboardingStep expectedStep,
        bool expectedIncludeImport)
    {
        var settings = new SettingsManager(NullLogger<SettingsManager>.Instance, Path.Combine(_directory, "settings.json"));
        var accounts = new AccountsStore(filePath: Path.Combine(_directory, "accounts.json"));
        var ytSettings = new YouTubeSettingsStore(filePath: Path.Combine(_directory, "youtube.json"));
        var ytAccounts = new YouTubeAccountsStore(filePath: Path.Combine(_directory, "youtube-accounts.json"));

        settings.SaveSettings(new AppSettings
        {
            Twitch = new TwitchSettings
            {
                Enabled = twitchConfigured || !youtubeEnabled,
                ClientId = twitchConfigured ? "client" : string.Empty,
                ClientSecret = twitchConfigured ? "secret" : string.Empty,
                Channel = twitchConfigured ? "streamer" : string.Empty,
            },
        });
        if (youtubeEnabled)
        {
            ytSettings.Save(new Core.YouTube.YouTubeSettings
            {
                Enabled = true,
                ClientId = youtubeConfigured ? "yt-client" : string.Empty,
                Channel = youtubeConfigured ? "@yt" : string.Empty,
            });
        }

        var checklist = new OnboardingChecklist(settings, accounts, youtubeSettings: ytSettings, youtubeAccounts: ytAccounts);
        var plan = App.GetStartupOnboardingPlan(checklist, hasImportOffer);

        Assert.Multiple(() =>
        {
            Assert.That(plan.ShouldShow, Is.EqualTo(expectedShow));
            Assert.That(plan.InitialStep, Is.EqualTo(expectedStep));
            Assert.That(plan.IncludeLegacyImport, Is.EqualTo(expectedIncludeImport));
        });
    }

    private OnboardingBannerViewModel CreateBanner()
    {
        var settings = new SettingsManager(NullLogger<SettingsManager>.Instance, Path.Combine(_directory, "settings.json"));
        var accounts = new AccountsStore(filePath: Path.Combine(_directory, "accounts.json"));
        var checklist = new OnboardingChecklist(settings, accounts);
        var bus = new InMemoryEventBus(NullLogger<InMemoryEventBus>.Instance);

        return new(checklist, new FakeWizardLauncher(), bus);
    }

    private sealed class FakeWizardLauncher : IOnboardingWizardLauncher
    {
        public event EventHandler? Closed;

        public bool HasBeenShown { get; private set; }

        public void Show(OnboardingStep step = OnboardingStep.None)
        {
            HasBeenShown = true;
            Closed?.Invoke(this, EventArgs.Empty);
        }
    }
}
