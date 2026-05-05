using PoproshaykaBot.Core.Polls;
using PoproshaykaBot.Core.Settings;
using PoproshaykaBot.Core.Settings.Obs;
using PoproshaykaBot.Core.Settings.Stores;
using PoproshaykaBot.Core.Settings.Ui;
using PoproshaykaBot.Core.Twitch.Auth;
using PoproshaykaBot.WinForms.Infrastructure.Di;
using System.Text.Json;

namespace PoproshaykaBot.WinForms.Forms.Settings;

public partial class SettingsForm : Form
{
    private readonly SettingsManager _settingsManager;
    private readonly AccountsStore _accountsStore;
    private readonly ObsChatStore _obsChatStore;
    private readonly DashboardLayoutStore _dashboardLayoutStore;
    private readonly PollsStore _pollsStore;
    private readonly List<SettingsDraftSection> _sections;
    private AppSettings _settings;
    private TwitchAccountSettings _botDraft;
    private TwitchAccountSettings _broadcasterDraft;
    private ObsChatSettings _obsChatDraft;
    private PollsSettings _pollsDraft;
    private DashboardLayoutSettings? _dashboardDraft;
    private bool _initialized;
    private bool _hasChanges;

    public SettingsForm(
        SettingsManager settingsManager,
        AccountsStore accountsStore,
        ObsChatStore obsChatStore,
        DashboardLayoutStore dashboardLayoutStore,
        PollsStore pollsStore)
    {
        _settingsManager = settingsManager;
        _accountsStore = accountsStore;
        _obsChatStore = obsChatStore;
        _dashboardLayoutStore = dashboardLayoutStore;
        _pollsStore = pollsStore;

        _settings = DeepClone(settingsManager.Current);
        _botDraft = DeepClone(accountsStore.LoadBot());
        _broadcasterDraft = DeepClone(accountsStore.LoadBroadcaster());
        _obsChatDraft = DeepClone(obsChatStore.Load());
        _pollsDraft = DeepClone(pollsStore.Load());
        _dashboardDraft = DeepCloneNullable(dashboardLayoutStore.LoadDashboard());

        InitializeComponent();

        _sections =
        [
            new(() => _basicSettingsControl.LoadSettings(_settings.Twitch),
                () => _basicSettingsControl.SaveSettings(_settings.Twitch)),
            new(() => _rateLimitingSettingsControl.LoadSettings(_settings.Twitch),
                () => _rateLimitingSettingsControl.SaveSettings(_settings.Twitch)),
            new(() => _messagesSettingsControl.LoadSettings(_settings.Twitch.Messages),
                () => _messagesSettingsControl.SaveSettings(_settings.Twitch.Messages)),
            new(() => _httpServerSettingsControl.LoadSettings(_settings.Twitch),
                () =>
                {
                    // read-only
                }),
            new(() => _oauthSettingsControl.LoadSettings(_settings, _botDraft, _broadcasterDraft),
                () => _oauthSettingsControl.SaveSettings(_settings)),
            new(() => _obsChatSettingsControl.LoadSettings(_obsChatDraft),
                () => _obsChatSettingsControl.SaveSettings(_obsChatDraft)),
            new(() => _autoBroadcastSettingsControl.LoadSettings(_settings.Twitch.AutoBroadcast),
                () => _autoBroadcastSettingsControl.SaveSettings(_settings.Twitch.AutoBroadcast)),
            new(() => _botLifecycleAutomationSettingsControl.LoadSettings(_settings.Twitch.BotLifecycleAutomation),
                () => _botLifecycleAutomationSettingsControl.SaveSettings(_settings.Twitch.BotLifecycleAutomation)),
            new(() => _miscSettingsControl.LoadSettings(_settings),
                () => _miscSettingsControl.SaveSettings(_settings)),
            new(() => _pollsSettingsControl.LoadSettings(_pollsDraft),
                () => _pollsSettingsControl.SaveSettings(_pollsDraft)),
            new(() => _dashboardSettingsControl.LoadSettings(_dashboardDraft),
                () => _dashboardDraft = _dashboardSettingsControl.SaveSettings()),
        ];
    }

    public event EventHandler? SettingsApplied;

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);

        if (_initialized)
        {
            return;
        }

        if (this.IsInDesignMode())
        {
            return;
        }

        _initialized = true;

        LoadSettingsToControls();
    }

    private void OnSettingChanged(object? sender, EventArgs e)
    {
        _hasChanges = true;
        UpdateButtonStates();
    }

    private void OnOkButtonClicked(object sender, EventArgs e)
    {
        if (_hasChanges)
        {
            ApplySettings();
        }

        DialogResult = DialogResult.OK;
        Close();
    }

    private void OnCancelButtonClicked(object sender, EventArgs e)
    {
        DialogResult = DialogResult.Cancel;
        Close();
    }

    private void OnApplyButtonClicked(object sender, EventArgs e)
    {
        ApplySettings();
    }

    private void OnResetButtonClicked(object sender, EventArgs e)
    {
        var result = MessageBox.Show("Вы уверены, что хотите сбросить все настройки к значениям по умолчанию?",
            "Подтверждение сброса",
            MessageBoxButtons.YesNo,
            MessageBoxIcon.Question);

        if (result != DialogResult.Yes)
        {
            return;
        }

        _settings = new();
        _botDraft = new();
        _broadcasterDraft = new();
        _obsChatDraft = new();
        _pollsDraft = new();
        _dashboardDraft = null;

        LoadSettingsToControls();
        _hasChanges = true;
        UpdateButtonStates();
    }

    private static T DeepClone<T>(T source) where T : class, new()
    {
        var json = JsonSerializer.Serialize(source);
        return JsonSerializer.Deserialize<T>(json) ?? new();
    }

    private static T? DeepCloneNullable<T>(T? source) where T : class
    {
        if (source == null)
        {
            return null;
        }

        var json = JsonSerializer.Serialize(source);
        return JsonSerializer.Deserialize<T>(json);
    }

    private void LoadSettingsToControls()
    {
        foreach (var section in _sections)
        {
            section.Load();
        }

        _hasChanges = false;
        UpdateButtonStates();
    }

    private void SaveSettingsFromControls()
    {
        foreach (var section in _sections)
        {
            section.Save();
        }
    }

    private void UpdateButtonStates()
    {
        _applyButton.Enabled = _hasChanges;
    }

    private void ApplySettings()
    {
        try
        {
            SaveSettingsFromControls();

            _settingsManager.SaveSettings(_settings);
            _accountsStore.SaveAll(_botDraft, _broadcasterDraft);
            _obsChatStore.Save(_obsChatDraft);
            _pollsStore.Save(_pollsDraft);

            if (_dashboardDraft != null)
            {
                _dashboardLayoutStore.SaveDashboard(_dashboardDraft);
            }

            _hasChanges = false;
            UpdateButtonStates();
            SettingsApplied?.Invoke(this, EventArgs.Empty);

            MessageBox.Show("Настройки успешно сохранены.", "Настройки",
                MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        catch (Exception exception)
        {
            MessageBox.Show($"Ошибка сохранения настроек: {exception.Message}", "Ошибка",
                MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private readonly record struct SettingsDraftSection(Action Load, Action Save);
}
