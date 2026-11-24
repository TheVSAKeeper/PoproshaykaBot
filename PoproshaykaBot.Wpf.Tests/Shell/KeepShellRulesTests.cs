using KeepShell.Testing.Rules;
using NUnit.Framework;
using PoproshaykaBot.Wpf.ViewModels;
using PoproshaykaBot.Wpf.ViewModels.Dialogs;
using System.IO;

namespace PoproshaykaBot.Wpf.Tests.Shell;

[TestFixture]
internal sealed class KeepShellRulesTests
{
    [Test]
    public void В_XAML_разметке_нет_нарушений_правил_KeepShell()
    {
        var wpfRoot = FindWpfProjectRoot();
        var violations = new List<RuleViolation>();

        foreach (var file in ProjectFiles.Enumerate(wpfRoot, "*.xaml"))
        {
            violations.AddRange(MarkupRules.ReadOnlyBindings(file, wpfRoot));
            violations.AddRange(MarkupRules.PackUris(file, wpfRoot));
        }

        Assert.That(violations, Is.Empty, () => RuleViolation.Report(violations));
    }

    [Test]
    public void В_ViewModels_нет_прямых_вызовов_платформы()
    {
        var wpfRoot = FindWpfProjectRoot();
        var viewModelsDir = Path.Combine(wpfRoot, "ViewModels");
        var files = ProjectFiles.Enumerate(viewModelsDir, "*.cs");

        var violations = PlatformCallRule.Check(files, wpfRoot);

        Assert.That(violations, Is.Empty, () => RuleViolation.Report(violations));
    }

    [Test]
    public void У_каждого_DialogViewModel_есть_парное_представление()
    {
        var violations = ViewPairRule.MissingViews(
            typeof(ShellViewModel).Assembly,
            type => ViewPairRule.IsDialogViewModel(type)
                && type != typeof(EmbeddedTwitchAuthDialogViewModel)
                && type != typeof(EmbeddedYouTubeAuthDialogViewModel)
                && type != typeof(EmbeddedPlatformChatLoginDialogViewModel));

        Assert.That(violations, Is.Empty, () => RuleViolation.Report(violations));
    }

    private static string FindWpfProjectRoot()
    {
        var dir = new DirectoryInfo(TestContext.CurrentContext.TestDirectory);

        while (dir is not null)
        {
            var candidate = Path.Combine(dir.FullName, "PoproshaykaBot.Wpf");

            if (File.Exists(Path.Combine(candidate, "PoproshaykaBot.Wpf.csproj")))
            {
                return candidate;
            }

            dir = dir.Parent;
        }

        throw new DirectoryNotFoundException("Не найден каталог проекта PoproshaykaBot.Wpf.");
    }
}
