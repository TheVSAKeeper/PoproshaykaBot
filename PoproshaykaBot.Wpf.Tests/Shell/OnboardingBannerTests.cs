using KeepShell.Bootstrap;
using KeepShell.Testing;
using Microsoft.Extensions.Logging.Abstractions;
using PoproshaykaBot.Core.Infrastructure;
using PoproshaykaBot.Core.Infrastructure.Events;
using PoproshaykaBot.Core.Settings;
using PoproshaykaBot.Core.Settings.Migrations.LegacyImport;
using PoproshaykaBot.Core.Settings.Onboarding;
using PoproshaykaBot.Core.Settings.Stores;
using PoproshaykaBot.Wpf.Bootstrap;
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

    [Test]
    public void Кнопка_баннера_вызывает_переход_к_настройке_каналов()
    {
        var callbackCount = 0;
        var eventCount = 0;
        using var banner = CreateBanner(() => callbackCount++);
        banner.ConfigureRequested += (_, _) => eventCount++;

        banner.ConfigureCommand.Execute(null);

        Assert.Multiple(() =>
        {
            Assert.That(callbackCount, Is.EqualTo(1));
            Assert.That(eventCount, Is.EqualTo(1));
        });
    }

    [TestCase(false, false, false, true)]
    [TestCase(true, false, false, false)]
    [TestCase(false, true, false, false)]
    [TestCase(false, false, true, false)]
    public void ShouldShowFirstRunStart_RespectsEmptyFolderDismissalAndGallery(
        bool hasTargetData,
        bool isDismissed,
        bool isGallery,
        bool expectedShow)
    {
        ISettingsStore uiSettings = new MemorySettings();
        if (isDismissed)
        {
            uiSettings.SetBool(SettingsKeys.LegacyImportDismissed, true);
        }

        LegacyDataSummary? target = hasTargetData
            ? new LegacyDataSummary
            {
                SourcePath = _directory,
                Kind = LegacyDataSourceKind.CurrentBaseDirectory,
                HasMonolithicSettings = false,
                HasSettingsDirectory = true,
                HasOAuthTokens = false,
                Files = [],
            }
            : null;

        var shouldShow = App.ShouldShowFirstRunStart(target, uiSettings, isGallery);

        Assert.That(shouldShow, Is.EqualTo(expectedShow));
    }

    private OnboardingBannerViewModel CreateBanner(Action? onConfigure = null)
    {
        var settings = new SettingsManager(NullLogger<SettingsManager>.Instance, Path.Combine(_directory, "settings.json"));
        var accounts = new AccountsStore(filePath: Path.Combine(_directory, "accounts.json"));
        var checklist = new OnboardingChecklist(settings, accounts);
        var bus = new InMemoryEventBus(NullLogger<InMemoryEventBus>.Instance);

        return new(checklist, bus, onConfigure);
    }
}
