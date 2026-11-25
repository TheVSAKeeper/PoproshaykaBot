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

    [TestCase(false, false, false, OnboardingStep.None)]
    [TestCase(true, false, false, null)]
    [TestCase(false, true, false, OnboardingStep.Credentials)]
    [TestCase(true, true, false, OnboardingStep.BotAuthorization)]
    [TestCase(true, true, true, null)]
    public void StartupWizard_UsesTheFirstMissingStepOnlyAfterImport(
        bool configured, bool imported, bool currentBotGrants, OnboardingStep? expected)
    {
        var settings = new SettingsManager(NullLogger<SettingsManager>.Instance, Path.Combine(_directory, "settings.json"));
        var accounts = new AccountsStore(filePath: Path.Combine(_directory, "accounts.json"));
        if (configured)
        {
            settings.SaveSettings(new AppSettings
            {
                Twitch = new TwitchSettings { ClientId = "client", ClientSecret = "secret", Channel = "streamer" },
            });
            accounts.SaveAll(new TwitchAccountSettings
            {
                AccessToken = "bot",
                StoredScopes = currentBotGrants ? [.. TwitchScopes.BotRequired] : ["chat:read"],
            }, new TwitchAccountSettings());
        }

        var result = imported ? new LegacyImportResult
        {
            SourcePath = "old",
            TargetPath = "new",
            CopiedFiles = ["settings/settings.json"],
            Failures = [new("settings/commands.json", "I/O")],
            SkippedFiles = [],
        } : null;
        var checklist = new OnboardingChecklist(settings, accounts);

        Assert.That(App.GetStartupWizardStep(checklist, result), Is.EqualTo(expected));
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
