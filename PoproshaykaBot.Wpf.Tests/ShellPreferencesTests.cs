using KeepShell.Bootstrap;
using PoproshaykaBot.Wpf.Bootstrap;
using PoproshaykaBot.Wpf.ViewModels;

namespace PoproshaykaBot.Wpf.Tests;

[TestFixture]
public class ShellPreferencesTests
{
    private sealed class FakeStore : ISettingsStore
    {
        private readonly Dictionary<string, string> _map = new(StringComparer.Ordinal);

        public event EventHandler<string>? Changed;

        public string FilePath => "memory";

        public string? GetStringValue(string key)
        {
            return _map.TryGetValue(key, out var value) ? value : null;
        }

        public void SetValue(string key, string value)
        {
            _map[key] = value;
            Changed?.Invoke(this, key);
        }

        public void Flush()
        {
        }
    }

    [Test]
    public void Loads_existing_values_without_rewriting()
    {
        var store = new FakeStore();
        store.SetValue(SettingsKeys.NavCollapsed, "true");

        var prefs = new ShellPreferences(store);

        Assert.That(prefs.NavCollapsed, Is.True);
    }

    [Test]
    public void Persists_changes()
    {
        var store = new FakeStore();
        var prefs = new ShellPreferences(store);

        prefs.NavCollapsed = true;

        Assert.That(store.GetStringValue(SettingsKeys.NavCollapsed), Is.EqualTo("true"));
    }
}
