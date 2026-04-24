using PoproshaykaBot.WinForms.Chat;
using PoproshaykaBot.WinForms.Infrastructure.Di;
using PoproshaykaBot.WinForms.Infrastructure.Events;
using PoproshaykaBot.WinForms.Infrastructure.Events.Lifecycle;
using PoproshaykaBot.WinForms.Infrastructure.Events.Logging;
using PoproshaykaBot.WinForms.Infrastructure.Events.Streaming;
using PoproshaykaBot.WinForms.Infrastructure.Hosting;
using PoproshaykaBot.WinForms.Settings;
using PoproshaykaBot.WinForms.Streaming;
using PoproshaykaBot.WinForms.Users;

namespace PoproshaykaBot.WinForms;

public partial class MainForm : Form
{
    private const int MaxLogLines = 500;
    private readonly IFormFactory _forms;
    private readonly IEventBus _eventBus;
    private readonly ChatHistoryManager _chatHistoryManager;
    private readonly SettingsManager _settingsManager;
    private readonly BotConnectionManager _connectionManager;
    private readonly IStreamStatus _streamStatusManager;
    private readonly List<IDisposable> _subs = [];
    private readonly Dictionary<PanelContent, Control?> _contentControls = new();

    private BotLifecyclePhase _currentPhase = BotLifecyclePhase.Idle;
    private bool _suppressSlotComboEvents;
    private bool _shutdownStarted;
    private bool _initialized;
    private UserStatisticsForm? _userStatisticsForm;

    public MainForm(
        IServiceProvider services,
        IFormFactory forms,
        IEventBus eventBus,
        ChatHistoryManager chatHistoryManager,
        BotConnectionManager connectionManager,
        SettingsManager settingsManager,
        IStreamStatus streamStatusManager)
    {
        _forms = forms;
        _eventBus = eventBus;
        _chatHistoryManager = chatHistoryManager;
        _connectionManager = connectionManager;
        _settingsManager = settingsManager;
        _streamStatusManager = streamStatusManager;

        InitializeComponent();

        services.HydrateDescendants(this);
        services.HydrateDescendants(_chatHost);
        services.HydrateDescendants(_broadcastProfilesPanel);
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);

        if (_initialized || DesignMode)
        {
            return;
        }

        _initialized = true;

        Text = $"Попрощайка Бот v{GetDisplayVersion()}";

        _contentControls[PanelContent.Logs] = _logTextBox;
        _contentControls[PanelContent.Chat] = _chatHost;
        _contentControls[PanelContent.BroadcastProfiles] = _broadcastProfilesPanel;
        _contentControls[PanelContent.None] = null;

        InitializeSlots();

        _subs.Add(_eventBus.SubscribeOnUi<BotLogEntry>(this, OnBotLogEntry));
        _subs.Add(_eventBus.SubscribeOnUi<BotConnectionStatusUpdated>(this, statusEvent => OnBotConnectionProgress(statusEvent.Message)));
        _subs.Add(_eventBus.SubscribeOnUi<BotLifecyclePhaseChanged>(this, OnBotLifecyclePhaseChanged));
        _subs.Add(_eventBus.SubscribeOnUi<StreamWentOnline>(this, _ => UpdateStreamStatus()));
        _subs.Add(_eventBus.SubscribeOnUi<StreamWentOffline>(this, _ => UpdateStreamStatus()));
        _subs.DisposeOnClose(this);

        LoadSettings();
        UpdateStreamStatus();

        AddLogMessage("Приложение запущено. Нажмите 'Подключить бота' для начала работы.");

        KeyPreview = true;

        InitializeWebViewAsync();
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        base.OnFormClosing(e);

        if (_shutdownStarted || e.Cancel)
        {
            return;
        }

        _shutdownStarted = true;
        e.Cancel = true;
        _ = ShutdownAndCloseAsync();
    }

    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        base.OnFormClosed(e);

        DisposeIfOrphan(_logTextBox);
        DisposeIfOrphan(_chatHost);
        DisposeIfOrphan(_broadcastProfilesPanel);
    }

    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        switch (keyData)
        {
            case Keys.Alt | Keys.U:
                OnOpenUserStatistics();
                return true;

            case Keys.Control | Keys.Shift | Keys.Delete:
                ClearChatHistory();
                return true;
        }

        return base.ProcessCmdKey(ref msg, keyData);
    }

    private async void OnConnectButtonClicked(object? sender, EventArgs e)
    {
        switch (_currentPhase)
        {
            case BotLifecyclePhase.Idle:
            case BotLifecyclePhase.Cancelled:
            case BotLifecyclePhase.Failed:
            case BotLifecyclePhase.Disconnected:
                StartBotConnection();
                break;

            case BotLifecyclePhase.Connecting:
                _connectionManager.CancelConnection();
                AddLogMessage("Отмена подключения...");
                break;

            case BotLifecyclePhase.Connected:
                await DisconnectBotAsync();
                break;

            case BotLifecyclePhase.Disconnecting:
                break;
        }
    }

    private void OnSwitchChatViewButtonClicked(object? sender, EventArgs e)
    {
        var settings = _settingsManager.Current;
        settings.Ui.CurrentChatViewMode = settings.Ui.CurrentChatViewMode == ChatViewMode.Legacy
            ? ChatViewMode.Overlay
            : ChatViewMode.Legacy;

        _settingsManager.SaveSettings(settings);
        ApplyChatViewMode();
    }

    private void OnLeftSlotComboChanged(object? sender, EventArgs e)
    {
        if (_suppressSlotComboEvents)
        {
            return;
        }

        if (_leftContentCombo.SelectedItem is not PanelContentItem item)
        {
            return;
        }

        ApplySlotChange(item.Value, null);
    }

    private void OnRightSlotComboChanged(object? sender, EventArgs e)
    {
        if (_suppressSlotComboEvents)
        {
            return;
        }

        if (_rightContentCombo.SelectedItem is not PanelContentItem item)
        {
            return;
        }

        ApplySlotChange(null, item.Value);
    }

    private void OnSettingsButtonClicked(object? sender, EventArgs e)
    {
        var settingsForm = _forms.Create<SettingsForm>();

        if (settingsForm.ShowDialog(this) != DialogResult.OK)
        {
            return;
        }

        LoadSettings();
        AddLogMessage("Настройки обновлены.");
    }

    private void OnUserStatisticsButtonClicked(object? sender, EventArgs e)
    {
        OnOpenUserStatistics();
    }

    private async void OnStreamInfoTimerTick(object? sender, EventArgs e)
    {
        if (_streamStatusManager.CurrentStatus != StreamStatus.Online)
        {
            return;
        }

        try
        {
            await _streamStatusManager.RefreshCurrentStatusAsync();
            UpdateStreamInfo();
        }
        catch (Exception exception)
        {
            AddLogMessage($"Ошибка обновления информации о стриме: {exception.Message}");
        }
    }

    private static void DisposeIfOrphan(Control? control)
    {
        if (control is { IsDisposed: false, Parent: null })
        {
            control.Dispose();
        }
    }

    private static string GetDisplayVersion()
    {
        var version = Application.ProductVersion;
        var plusIndex = version.IndexOf('+', StringComparison.Ordinal);

        if (plusIndex > 0)
        {
            version = version[..plusIndex];
        }

        return version;
    }

    private static void RebuildSlotCombo(ToolStripComboBox combo, IEnumerable<PanelContent> all)
    {
        combo.Items.Clear();

        foreach (var content in all)
        {
            combo.Items.Add(new PanelContentItem(content));
        }
    }

    private static void SelectComboValue(ToolStripComboBox combo, PanelContent value)
    {
        for (var i = 0; i < combo.Items.Count; i++)
        {
            if (combo.Items[i] is not PanelContentItem item || item.Value != value)
            {
                continue;
            }

            combo.SelectedIndex = i;
            return;
        }

        combo.SelectedIndex = -1;
    }

    private void OnBotLogEntry(BotLogEntry entry)
    {
        var message = entry.Source switch
        {
            "Http" => $"HTTP: {entry.Message}",
            _ => entry.Message,
        };

        AddLogMessage(message);
    }

    private async Task ShutdownAndCloseAsync()
    {
        try
        {
            _connectionManager.CancelConnection();

            if (_userStatisticsForm is { IsDisposed: false })
            {
                _userStatisticsForm.Close();
                _userStatisticsForm = null;
            }

            await DisconnectBotAsync();
        }
        catch (Exception ex)
        {
            AddLogMessage($"Ошибка завершения работы: {ex.Message}");
        }
        finally
        {
            Close();
        }
    }

    private void ApplySlotChange(PanelContent? left, PanelContent? right)
    {
        var settings = _settingsManager.Current;

        if (left.HasValue)
        {
            settings.Ui.LeftSlotContent = left.Value;
        }

        if (right.HasValue)
        {
            settings.Ui.RightSlotContent = right.Value;
        }

        if (settings.Ui.LeftSlotContent != PanelContent.None && settings.Ui.LeftSlotContent == settings.Ui.RightSlotContent)
        {
            if (left.HasValue)
            {
                settings.Ui.RightSlotContent = PanelContent.None;
            }
            else
            {
                settings.Ui.LeftSlotContent = PanelContent.None;
            }
        }

        _settingsManager.SaveSettings(settings);

        _suppressSlotComboEvents = true;

        try
        {
            SelectComboValue(_leftContentCombo, settings.Ui.LeftSlotContent);
            SelectComboValue(_rightContentCombo, settings.Ui.RightSlotContent);
        }
        finally
        {
            _suppressSlotComboEvents = false;
        }

        ApplySlotSelection();
    }

    private void OnBotLifecyclePhaseChanged(BotLifecyclePhaseChanged phaseEvent)
    {
        _currentPhase = phaseEvent.Phase;
        ApplyPhaseVisuals(phaseEvent.Phase);

        switch (phaseEvent.Phase)
        {
            case BotLifecyclePhase.Connecting:
                AddLogMessage("Подключение бота...");
                break;

            case BotLifecyclePhase.Connected:
                AddLogMessage("Бот успешно подключен!");
                UpdateStreamStatus();
                break;

            case BotLifecyclePhase.Cancelled:
                AddLogMessage("Подключение отменено пользователем.");
                break;

            case BotLifecyclePhase.Failed:
                AddLogMessage($"Ошибка подключения бота: {phaseEvent.Exception?.Message}");

                MessageBox.Show($"Ошибка подключения бота: {phaseEvent.Exception?.Message}", "Ошибка",
                    MessageBoxButtons.OK, MessageBoxIcon.Error);

                break;

            case BotLifecyclePhase.Disconnecting:
                AddLogMessage("Отключение бота...");
                break;

            case BotLifecyclePhase.Disconnected:
                AddLogMessage("Бот отключен.");
                UpdateStreamStatus();
                break;
        }
    }

    private void ApplyPhaseVisuals(BotLifecyclePhase phase)
    {
        switch (phase)
        {
            case BotLifecyclePhase.Connecting:
                _connectToolStripButton.Text = "⏹️ Отменить";
                _connectToolStripButton.BackColor = Color.Orange;
                ShowConnectionProgress(true);
                break;

            case BotLifecyclePhase.Connected:
                _connectToolStripButton.Text = "🔌 Отключить";
                _connectToolStripButton.BackColor = Color.LightGreen;
                ShowConnectionProgress(false);
                break;

            case BotLifecyclePhase.Disconnecting:
                _connectToolStripButton.Text = "⏳ Отключение...";
                _connectToolStripButton.BackColor = Color.Orange;
                ShowConnectionProgress(true);
                break;

            case BotLifecyclePhase.Idle:
            case BotLifecyclePhase.Disconnected:
            case BotLifecyclePhase.Cancelled:
            case BotLifecyclePhase.Failed:
                _connectToolStripButton.Text = "🔌 Подключить";
                _connectToolStripButton.BackColor = SystemColors.Control;
                ShowConnectionProgress(false);
                break;
        }
    }

    private void OnBotConnectionProgress(string message)
    {
        _connectionStatusLabel.Text = message;
    }

    private void OnOpenUserStatistics()
    {
        if (_userStatisticsForm == null || _userStatisticsForm.IsDisposed)
        {
            _userStatisticsForm = _forms.Create<UserStatisticsForm>();
            _userStatisticsForm.Show(this);
        }
        else
        {
            _userStatisticsForm.Focus();
        }
    }

    private void UpdateStreamStatus()
    {
        _streamInfoWidget.UpdateStatus(_streamStatusManager.CurrentStatus, _streamStatusManager.CurrentStream);

        if (_streamStatusManager.CurrentStatus == StreamStatus.Online)
        {
            if (!_streamInfoTimer.Enabled)
            {
                _streamInfoTimer.Start();
            }
        }
        else
        {
            if (_streamInfoTimer.Enabled)
            {
                _streamInfoTimer.Stop();
            }
        }
    }

    private void UpdateStreamInfo()
    {
        UpdateStreamStatus();
    }

    private void ClearChatHistory()
    {
        var result = MessageBox.Show("Вы уверены, что хотите очистить всю историю сообщений чата?\n\nЭто действие нельзя отменить.",
            "Очистка истории чата",
            MessageBoxButtons.YesNo,
            MessageBoxIcon.Question,
            MessageBoxDefaultButton.Button2);

        if (result != DialogResult.Yes)
        {
            return;
        }

        _chatHistoryManager.ClearHistory();
        AddLogMessage("История сообщений чата очищена.");
    }

    private void InitializeSlots()
    {
        var all = new[]
        {
            PanelContent.None,
            PanelContent.Logs,
            PanelContent.Chat,
            PanelContent.BroadcastProfiles,
        };

        RebuildSlotCombo(_leftContentCombo, all);
        RebuildSlotCombo(_rightContentCombo, all);

        var ui = _settingsManager.Current.Ui;

        _suppressSlotComboEvents = true;

        try
        {
            SelectComboValue(_leftContentCombo, ui.LeftSlotContent);
            SelectComboValue(_rightContentCombo, ui.RightSlotContent);
        }
        finally
        {
            _suppressSlotComboEvents = false;
        }

        _leftContentCombo.SelectedIndexChanged += OnLeftSlotComboChanged;
        _rightContentCombo.SelectedIndexChanged += OnRightSlotComboChanged;
    }

    private void ApplySlotSelection()
    {
        var ui = _settingsManager.Current.Ui;

        _leftSlot.SetBody(ResolveBody(ui.LeftSlotContent));
        _rightSlot.SetBody(ResolveBody(ui.RightSlotContent));

        _slotsTableLayoutPanel.ColumnStyles.Clear();

        var leftVisible = ui.LeftSlotContent != PanelContent.None;
        var rightVisible = ui.RightSlotContent != PanelContent.None;

        if (leftVisible && rightVisible)
        {
            _slotsTableLayoutPanel.ColumnStyles.Add(new(SizeType.Percent, 50F));
            _slotsTableLayoutPanel.ColumnStyles.Add(new(SizeType.Percent, 50F));
        }
        else if (leftVisible)
        {
            _slotsTableLayoutPanel.ColumnStyles.Add(new(SizeType.Percent, 100F));
            _slotsTableLayoutPanel.ColumnStyles.Add(new(SizeType.Absolute, 0F));
        }
        else if (rightVisible)
        {
            _slotsTableLayoutPanel.ColumnStyles.Add(new(SizeType.Absolute, 0F));
            _slotsTableLayoutPanel.ColumnStyles.Add(new(SizeType.Percent, 100F));
        }
        else
        {
            _slotsTableLayoutPanel.ColumnStyles.Add(new(SizeType.Absolute, 0F));
            _slotsTableLayoutPanel.ColumnStyles.Add(new(SizeType.Absolute, 0F));
        }

        _leftSlot.Visible = leftVisible;
        _rightSlot.Visible = rightVisible;

        var profilesShown =
            ui.LeftSlotContent == PanelContent.BroadcastProfiles || ui.RightSlotContent == PanelContent.BroadcastProfiles;

        _broadcastProfileQuickPanel.Visible = !profilesShown;
        _mainTableLayoutPanel.RowStyles[2].Height = profilesShown ? 0F : 36F;

        ApplyChatViewMode();
        _slotsTableLayoutPanel.PerformLayout();
    }

    private Control? ResolveBody(PanelContent content)
    {
        return _contentControls[content];
    }

    private void ApplyChatViewMode()
    {
        var ui = _settingsManager.Current.Ui;
        var chatShown =
            ui.LeftSlotContent == PanelContent.Chat || ui.RightSlotContent == PanelContent.Chat;

        _chatViewToolStripButton.Visible = chatShown;
        _chatViewSeparator.Visible = chatShown;

        if (!chatShown)
        {
            _chatDisplay.Visible = false;
            _overlayWebView.Visible = false;
            return;
        }

        if (ui.CurrentChatViewMode == ChatViewMode.Legacy)
        {
            _chatDisplay.Visible = true;
            _overlayWebView.Visible = false;
            _chatDisplay.BringToFront();
            _chatViewToolStripButton.Text = "👁️ Чат";
            _chatViewToolStripButton.BackColor = SystemColors.Control;
        }
        else
        {
            _chatDisplay.Visible = false;
            _overlayWebView.Visible = true;
            _overlayWebView.BringToFront();
            _chatViewToolStripButton.Text = "👁️ Overlay";
            _chatViewToolStripButton.BackColor = Color.LightBlue;

            if (_overlayWebView.CoreWebView2 == null)
            {
                InitializeWebViewAsync();
            }
            else
            {
                UpdateOverlayUrl();
            }
        }
    }

    private async void InitializeWebViewAsync()
    {
        try
        {
            await _overlayWebView.EnsureCoreWebView2Async(null);
            UpdateOverlayUrl();
        }
        catch (Exception ex)
        {
            AddLogMessage($"Ошибка инициализации WebView2: {ex.Message}");
        }
    }

    private void UpdateOverlayUrl()
    {
        if (_overlayWebView.CoreWebView2 == null)
        {
            return;
        }

        var port = _settingsManager.Current.Twitch.HttpServerPort;
        var url = $"http://localhost:{port}/chat?preview=true";

        if (_overlayWebView.Source?.ToString() != url)
        {
            _overlayWebView.CoreWebView2.Navigate(url);
        }
    }

    private void StartBotConnection()
    {
        try
        {
            _connectionManager.StartConnection();
        }
        catch (InvalidOperationException exception)
        {
            AddLogMessage($"Ошибка запуска подключения: {exception.Message}");
        }
    }

    private void AddLogMessage(string message)
    {
        if (_logTextBox.Lines.Length > MaxLogLines)
        {
            var charIndex = _logTextBox.GetFirstCharIndexFromLine(_logTextBox.Lines.Length - MaxLogLines);
            if (charIndex > 0)
            {
                _logTextBox.Select(0, charIndex);
                _logTextBox.SelectedText = "";
            }
        }

        _logTextBox.AppendText($"{DateTime.Now:HH:mm:ss} - {message}{Environment.NewLine}");
        _logTextBox.SelectionStart = _logTextBox.Text.Length;
        _logTextBox.ScrollToCaret();
    }

    private void ShowConnectionProgress(bool show)
    {
        _connectionProgressBar.Visible = show;
        _connectionStatusLabel.Visible = show;

        if (show)
        {
            _connectionProgressBar.Style = ProgressBarStyle.Marquee;
            _connectionStatusLabel.Text = "Подключение...";
        }
        else
        {
            _connectionStatusLabel.Text = "";
        }
    }

    private async Task DisconnectBotAsync()
    {
        if (_currentPhase != BotLifecyclePhase.Connected)
        {
            return;
        }

        try
        {
            await _connectionManager.StopAsync();
        }
        catch (Exception exception)
        {
            AddLogMessage($"Ошибка при отключении бота: {exception.Message}");
        }
    }

    private void LoadSettings()
    {
        try
        {
            var settings = _settingsManager.Current;
            AddLogMessage("Настройки Twitch загружены.");
            ApplySlotSelection();
        }
        catch (Exception exception)
        {
            AddLogMessage($"Ошибка загрузки настроек: {exception.Message}");
        }
    }

    private sealed record PanelContentItem(PanelContent Value)
    {
        public override string ToString()
        {
            return Value switch
            {
                PanelContent.None => "— Нет —",
                PanelContent.Logs => "📜 Логи",
                PanelContent.Chat => "💬 Чат",
                PanelContent.BroadcastProfiles => "🎛 Профили",
                _ => Value.ToString(),
            };
        }
    }
}
