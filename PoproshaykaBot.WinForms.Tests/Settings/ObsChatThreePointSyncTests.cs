using PoproshaykaBot.WinForms.Server.Obs;
using PoproshaykaBot.WinForms.Settings.Obs;
using System.Reflection;

namespace PoproshaykaBot.WinForms.Tests.Settings;

[TestFixture]
public sealed class ObsChatThreePointSyncTests
{
    [TestCaseSource(nameof(GetMutableSourceProperties))]
    public void MutatingObsChatSettingsField_ChangesObsChatCssSettingsOutput(PropertyInfo sourceProp)
    {
        var defaults = new ObsChatSettings();
        var mutated = new ObsChatSettings();

        var changed = TryMutate(sourceProp, mutated);
        Assert.That(changed, Is.True,
            $"Тест не смог мутировать ObsChatSettings.{sourceProp.Name}. "
            + "Добавьте поддержку типа в TryMutate.");

        var defaultCss = ObsChatCssSettings.FromObsChatSettings(defaults);
        var mutatedCss = ObsChatCssSettings.FromObsChatSettings(mutated);

        Assert.That(AreEqual(defaultCss, mutatedCss), Is.False,
            $"Изменение ObsChatSettings.{sourceProp.Name} не повлияло на ObsChatCssSettings — "
            + "пропустили шаг (1) three-point sync в FromObsChatSettings.");
    }

    [TestCaseSource(nameof(GetCssTargetProperties))]
    public void EveryObsChatCssSettingsField_IsConsumedInObsJs(PropertyInfo cssProp)
    {
        var jsPath = LocateOverlayJs();
        var jsSource = File.ReadAllText(jsPath);

        var camelCase = ToCamelCase(cssProp.Name);

        Assert.That(jsSource, Does.Contain(camelCase),
            $"Поле ObsChatCssSettings.{cssProp.Name} (JSON: {camelCase}) не используется в obs.js — "
            + "пропустили шаг (2) three-point sync.");
    }

    private static IEnumerable<PropertyInfo> GetMutableSourceProperties()
    {
        return typeof(ObsChatSettings)
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(p => p is { CanRead: true, CanWrite: true });
    }

    private static IEnumerable<PropertyInfo> GetCssTargetProperties()
    {
        return typeof(ObsChatCssSettings)
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(p => p is { CanRead: true, CanWrite: true });
    }

    private static bool TryMutate(PropertyInfo prop, ObsChatSettings target)
    {
        var current = prop.GetValue(target);

        switch (current)
        {
            case bool b:
                prop.SetValue(target, !b);
                return true;

            case int i:
                prop.SetValue(target, i + 1);
                return true;

            case string s when prop.Name.Contains("Animation"):
                prop.SetValue(target, MessageAnimationType.None);
                return !string.Equals((string?)prop.GetValue(target), s, StringComparison.Ordinal);

            case string s:
                prop.SetValue(target, s + "_mut");
                return true;

            case AppColor:
                prop.SetValue(target, (AppColor)Color.FromArgb(1, 2, 3, 4));
                return true;

            case Color:
                prop.SetValue(target, Color.FromArgb(1, 2, 3, 4));
                return true;
        }

        return false;
    }

    private static bool AreEqual(ObsChatCssSettings a, ObsChatCssSettings b)
    {
        foreach (var p in typeof(ObsChatCssSettings).GetProperties())
        {
            if (!Equals(p.GetValue(a), p.GetValue(b)))
            {
                return false;
            }
        }

        return true;
    }

    private static string ToCamelCase(string name)
    {
        if (string.IsNullOrEmpty(name) || char.IsLower(name[0]))
        {
            return name;
        }

        return char.ToLowerInvariant(name[0]) + name[1..];
    }

    private static string LocateOverlayJs()
    {
        var current = AppContext.BaseDirectory;

        for (var i = 0; i < 8; i++)
        {
            var candidate = Path.Combine(current, "PoproshaykaBot.WinForms", "Assets", "obs.js");
            if (File.Exists(candidate))
            {
                return candidate;
            }

            var parent = Directory.GetParent(current);
            if (parent == null)
            {
                break;
            }

            current = parent.FullName;
        }

        throw new FileNotFoundException("Не найден obs.js (нужен для проверки three-point sync).");
    }
}
