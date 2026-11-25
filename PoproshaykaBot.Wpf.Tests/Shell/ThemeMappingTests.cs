using KeepShell.Bootstrap;
using KeepShell.Testing;
using KeepShell.ViewModels;
using PoproshaykaBot.Wpf.Bootstrap;
using PoproshaykaBot.Wpf.ViewModels;
using PoproshaykaBot.Wpf.ViewModels.Settings;
using PoproshaykaBot.Wpf.ViewModels.Settings.Contributors;
using System.IO;
using System.Reflection;
using System.Text.RegularExpressions;
using System.Windows;

namespace PoproshaykaBot.Wpf.Tests.Shell;

[TestFixture]
public class ThemeMappingTests
{
    private const string HostProjectName = "PoproshaykaBot.Wpf";
    private const string TestProjectName = "PoproshaykaBot.Wpf.Tests";

    private static readonly Regex DynamicResourceKey =
        new(@"\{DynamicResource\s+(?<key>[^}\s,]+)\s*\}", RegexOptions.Compiled, TimeSpan.FromSeconds(5));

    [Test]
    public void Every_resource_key_used_by_host_exists_in_every_theme()
    {
        PackScheme.Ensure();
        ThemeManager.Register(ThemeManager.DefaultLight);
        ThemeManager.Register(ThemeManager.DefaultDark);

        var keys = UsedKeys();

        Assert.That(keys, Is.Not.Empty);
        Assert.That(ThemeManager.Themes, Is.Not.Empty);

        foreach (var theme in ThemeManager.Themes)
        {
            var declared = DeclaredKeys(theme.Palette, theme.Tokens);
            declared.Add(AppScaledSizes.ShortFieldWidthKey);
            var missing = keys.Where(key => !declared.Contains(key)).ToArray();

            Assert.That(missing, Is.Empty, $"Тема «{theme.Key}» не объявляет ключи: {string.Join(", ", missing)}");
        }
    }

    [Test]
    [Apartment(ApartmentState.STA)]
    public void ThemeSelectionViewModel_switches_themes_and_persists_selection()
    {
        TestResources.Ensure(
        [
            "pack://application:,,,/KeepShell;component/Resources/Themes/Light.xaml",
            "pack://application:,,,/KeepShell;component/Resources/Themes/Tokens.xaml",
        ]);
        ThemeManager.Register(ThemeManager.DefaultLight);
        ThemeManager.Register(ThemeManager.DefaultDark);

        var previousKey = ThemeManager.Current?.Key;
        var settings = new MemorySettings();
        using var selection = new ThemeSelectionViewModel(settings);

        try
        {
            ThemeManager.Apply(ThemeManager.DefaultLight.Key);
            selection.Select(ThemeManager.DefaultDark.Key);

            Assert.Multiple(() =>
            {
                Assert.That(ThemeManager.Current?.Key, Is.EqualTo(ThemeManager.DefaultDark.Key));
                Assert.That(selection.CurrentKey, Is.EqualTo(ThemeManager.DefaultDark.Key));
                Assert.That(settings.GetStringValue(ThemeManager.SettingsKeyName), Is.EqualTo(ThemeManager.DefaultDark.Key));
            });

            selection.CurrentKey = ThemeManager.DefaultLight.Key;

            Assert.Multiple(() =>
            {
                Assert.That(ThemeManager.Current?.Key, Is.EqualTo(ThemeManager.DefaultLight.Key));
                Assert.That(settings.GetStringValue(ThemeManager.SettingsKeyName), Is.EqualTo(ThemeManager.DefaultLight.Key));
            });
        }
        finally
        {
            if (!string.IsNullOrWhiteSpace(previousKey))
            {
                ThemeManager.Apply(previousKey);
            }
        }
    }

    [Test]
    public void Appearance_section_dynamic_keywords_reflect_registered_themes()
    {
        PackScheme.Ensure();
        ThemeManager.Register(ThemeManager.DefaultLight);
        ThemeManager.Register(ThemeManager.DefaultDark);

        var settings = new MemorySettings();
        using var selection = new ThemeSelectionViewModel(settings);
        var contributor = new AppearanceSettingsSectionContributor(
            new AppearanceSettingsSectionViewModel(selection, new ShellPreferences(settings)));

        var section = contributor.CreateSection();

        Assert.Multiple(() =>
        {
            Assert.That(section.Matches(SettingsSection.ParseQuery(ThemeManager.DefaultLight.DisplayName)), Is.True);
            Assert.That(section.Matches(SettingsSection.ParseQuery(ThemeManager.DefaultDark.DisplayName)), Is.True);
            Assert.That(section.Matches(SettingsSection.ParseQuery(ThemeManager.DefaultDark.Key)), Is.True);
            Assert.That(section.Matches(["__nonexistent_theme_keyword__"]), Is.False);
        });
    }

    private static SortedSet<string> UsedKeys()
    {
        var keys = new SortedSet<string>(StringComparer.Ordinal);

        foreach (var field in typeof(ThemeKeys).GetFields(BindingFlags.Public | BindingFlags.Static))
        {
            if (field.IsLiteral && field.GetRawConstantValue() is string key)
            {
                keys.Add(key);
            }
        }

        var markup = MarkupFiles();

        Assert.That(markup, Is.Not.Empty, "Разметка хоста не найдена рядом с каталогом тестов.");

        foreach (var file in markup)
        {
            foreach (Match match in DynamicResourceKey.Matches(File.ReadAllText(file)))
            {
                keys.Add(match.Groups["key"].Value);
            }
        }

        return keys;
    }

    private static string[] MarkupFiles()
    {
        var assemblyDirectory = Path.GetDirectoryName(typeof(ThemeMappingTests).Assembly.Location) ?? string.Empty;
        var segment = Path.DirectorySeparatorChar + TestProjectName + Path.DirectorySeparatorChar;
        var index = assemblyDirectory.LastIndexOf(segment, StringComparison.OrdinalIgnoreCase);

        if (index < 0)
        {
            return [];
        }

        var host = Path.Combine(assemblyDirectory[..index], HostProjectName);

        if (!Directory.Exists(host))
        {
            return [];
        }

        return Directory.GetFiles(host, "*.xaml", SearchOption.AllDirectories)
            .Where(file => !IsBuildOutput(file, host))
            .ToArray();
    }

    private static bool IsBuildOutput(string file, string host)
    {
        var relative = Path.GetRelativePath(host, file);
        var root = relative.Split(Path.DirectorySeparatorChar)[0];

        return root.Equals("bin", StringComparison.OrdinalIgnoreCase)
            || root.Equals("obj", StringComparison.OrdinalIgnoreCase);
    }

    private static HashSet<string> DeclaredKeys(params Uri[] sources)
    {
        var keys = new HashSet<string>(StringComparer.Ordinal);

        foreach (var source in sources)
        {
            var dictionary = new ResourceDictionary
            {
                Source = source,
            };

            foreach (var key in dictionary.Keys)
            {
                if (key is string name)
                {
                    keys.Add(name);
                }
            }
        }

        return keys;
    }
}
