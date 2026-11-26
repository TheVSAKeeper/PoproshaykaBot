using PoproshaykaBot.Wpf.ViewModels.Settings;

using PoproshaykaBot.Wpf.Bootstrap;

namespace PoproshaykaBot.Wpf.ViewModels.Onboarding;

public sealed class AppearancePageViewModel : OnboardingPageViewModelBase, IDisposable
{
    private readonly ThemeSelectionViewModel _theme;
    private readonly ShellPreferences _shell;
    private readonly DraftSettings _draft;
    private readonly string? _initialTheme;
    private readonly bool _initialPageHeader;
    private readonly bool _initialNotifications;
    private bool _committed;
    private bool _skipRollback;
    private int _disposed;

    public AppearancePageViewModel(ThemeSelectionViewModel theme, ShellPreferences shell, ISettingsStore settings)
    {
        _theme = theme;
        _shell = shell;
        _initialTheme = theme.CurrentKey;
        _initialPageHeader = shell.ShowPageHeader;
        _initialNotifications = shell.EnableToastNotifications;
        _draft = new(settings);
        Appearance = new(new ThemeSelectionViewModel(_draft), new ShellPreferences(_draft));
        _draft.Changed += OnDraftChanged;
    }

    public override string PageTitle => "Оформление";

    public AppearanceSettingsSectionViewModel Appearance { get; }

    public override void OnEnter(OnboardingContext context)
    {
        CanAdvance = true;
    }

    internal void Commit()
    {
        if (_draft.HasChanged(ThemeManager.SettingsKeyName))
        {
            _theme.Select(Appearance.Theme.CurrentKey);
        }

        if (_draft.HasChanged(SettingsKeys.FontScale))
        {
            _shell.FontScale = Appearance.Shell.FontScale;
        }

        _shell.SaveAppearanceFlags(_draft.HasChanged(SettingsKeys.ShowPageHeader),
            _draft.HasChanged(SettingsKeys.EnableToastNotifications));

        _committed = true;
    }

    internal void DiscardRollback()
    {
        _skipRollback = true;
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        _draft.Changed -= OnDraftChanged;
        Appearance.Theme.Dispose();

        if (_committed || _skipRollback)
        {
            return;
        }

        if (_draft.HasChanged(ThemeManager.SettingsKeyName) && _initialTheme is { } key)
        {
            ThemeManager.Apply(key);
        }

        if (_draft.HasChanged(SettingsKeys.FontScale))
        {
            FontScaleManager.Apply(_shell.FontScale);
        }

        _shell.PreviewAppearanceFlags(
            _draft.HasChanged(SettingsKeys.ShowPageHeader) ? _initialPageHeader : _shell.ShowPageHeader,
            _draft.HasChanged(SettingsKeys.EnableToastNotifications) ? _initialNotifications : _shell.EnableToastNotifications);
    }

    private void OnDraftChanged(object? sender, string key)
    {
        if (key is SettingsKeys.ShowPageHeader or SettingsKeys.EnableToastNotifications)
        {
            _shell.PreviewAppearanceFlags(Appearance.Shell.ShowPageHeader, Appearance.Shell.EnableToastNotifications);
        }
    }

    private sealed class DraftSettings(ISettingsStore saved) : ISettingsStore
    {
        private readonly Dictionary<string, string> _values = new(StringComparer.Ordinal);

        public event EventHandler<string>? Changed;

        public event EventHandler<SettingsWriteFailedEventArgs>? WriteFailed { add { } remove { } }

        public string FilePath => saved.FilePath;

        public bool HasChanged(string key) => _values.ContainsKey(key);

        public string? GetStringValue(string key) => _values.GetValueOrDefault(key) ?? saved.GetStringValue(key);

        public void SetValue(string key, string value)
        {
            _values[key] = value;
            Changed?.Invoke(this, key);
        }

        public void Flush()
        {
        }
    }
}
