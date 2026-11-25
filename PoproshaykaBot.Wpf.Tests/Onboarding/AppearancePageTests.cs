using KeepShell.Testing;
using KeepShell.ViewModels;
using PoproshaykaBot.Wpf.ViewModels;
using PoproshaykaBot.Wpf.ViewModels.Onboarding;

namespace PoproshaykaBot.Wpf.Tests.Onboarding;

[TestFixture]
public sealed class AppearancePageTests
{
    [OneTimeSetUp]
    public void RegisterPackUris() => PackScheme.Ensure();

    [TestCase(false)]
    [TestCase(true)]
    public void ShellPreferencesAreSavedOnlyWhenWizardCommits(bool commit)
    {
        var store = new MemorySettings();
        var shell = new ShellPreferences(store);
        using var theme = new ThemeSelectionViewModel(store);
        using (var page = new AppearancePageViewModel(theme, shell, store))
        {
            page.Appearance.Shell.ShowPageHeader = false;
            page.Appearance.Shell.EnableToastNotifications = false;
            Assert.That(new ShellPreferences(store).ShowPageHeader, Is.True);
            Assert.That(shell.ShowPageHeader, Is.False);

            if (commit)
            {
                page.Commit();
            }
        }

        var saved = new ShellPreferences(store);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(saved.ShowPageHeader, Is.EqualTo(!commit));
            Assert.That(saved.EnableToastNotifications, Is.EqualTo(!commit));
            Assert.That(shell.ShowPageHeader, Is.EqualTo(!commit));
        }
    }

    [Test]
    public void RepeatedDisposalDoesNotRestorePreviewOverLaterChanges()
    {
        var store = new MemorySettings();
        var shell = new ShellPreferences(store);
        using var theme = new ThemeSelectionViewModel(store);
        using var page = new AppearancePageViewModel(theme, shell, store);

        page.Appearance.Shell.ShowPageHeader = false;
        page.Dispose();
        Assert.That(shell.ShowPageHeader, Is.True);

        shell.ShowPageHeader = false;
        page.Dispose();

        Assert.That(shell.ShowPageHeader, Is.False);
    }
}
