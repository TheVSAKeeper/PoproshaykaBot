using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using KeepShell.Mcp;
using System.ComponentModel;
using System.Globalization;

namespace PoproshaykaBot.Wpf.ViewModels.Settings;

public sealed partial class McpSettingsSectionViewModel : ObservableObject, IDisposable
{
    private static readonly TimeSpan CopyFeedbackDuration = TimeSpan.FromSeconds(3);

    private static readonly string[] DefaultSnippetFormats =
    [
        ".mcp.json (JSON)",
        "Claude CLI",
        "Codex (config.toml)",
        "opencode.json",
        "Адрес и заголовок",
    ];

    private readonly McpServerOptions _options;
    private readonly IDialogService _dialogService;
    private readonly IClipboardService _clipboard;
    private readonly IUiTimer? _copyFeedbackTimer;

    private string _portText;
    private bool _syncingPort;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ConnectSnippet))]
    [NotifyPropertyChangedFor(nameof(ConnectSnippetTooltip))]
    [NotifyPropertyChangedFor(nameof(ConnectSnippetAutomationName))]
    private int _selectedSnippetFormatIndex;

    [ObservableProperty]
    private string? _copyFeedback;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasPortError))]
    private string? _portError;

    public McpSettingsSectionViewModel(
        McpPreferences preferences,
        McpServerHost server,
        McpServerOptions options,
        IDialogService dialogService,
        IClipboardService clipboard,
        IUiDispatcher? uiDispatcher = null)
    {
        Preferences = preferences;
        Server = server;
        _options = options;
        _dialogService = dialogService;
        _clipboard = clipboard;
        _portText = preferences.Port.ToString(CultureInfo.InvariantCulture);
        _copyFeedbackTimer = uiDispatcher?.CreateTimer(CopyFeedbackDuration, ClearCopyFeedback);

        Preferences.PropertyChanged += OnPreferencesPropertyChanged;
        Server.PropertyChanged += OnServerPropertyChanged;
    }

    public McpPreferences Preferences { get; }

    public McpServerHost Server { get; }

    public IReadOnlyList<string> SnippetFormats => DefaultSnippetFormats;

    public bool HasPortError => PortError is not null;

    public string PortText
    {
        get => _portText;
        set
        {
            var next = value ?? string.Empty;

            if (string.Equals(_portText, next, StringComparison.Ordinal))
            {
                return;
            }

            _portText = next;
            OnPropertyChanged();
            ClearCopyFeedback();
            ApplyPortText(next);
        }
    }

    public bool CanToggleMutations => !Server.IsElevated;

    public bool AllowMutations
    {
        get => Preferences.EffectiveAllowMutations;
        set
        {
            if (!CanToggleMutations)
            {
                return;
            }

            Preferences.AllowMutations = value;
        }
    }

    public string DisplayToken => Preferences.EffectiveToken;

    public bool HasConnectEndpoint => Server.BoundPort is > 0 || Preferences.EffectivePort > 0;

    public string Url => Server.BoundPort is > 0
        ? $"http://127.0.0.1:{Server.BoundPort}{_options.EndpointPath}"
        : Preferences.EffectivePort > 0
            ? $"http://127.0.0.1:{Preferences.EffectivePort}{_options.EndpointPath}"
            : string.Empty;

    public string ConnectSnippet => HasConnectEndpoint
        ? McpConnectSnippets.For(EffectiveSnippetFormatIndex, Url, DisplayToken, _options.ServerName)
        : string.Empty;

    public string ConnectSnippetTooltip => EffectiveSnippetFormatIndex switch
    {
        1 => "Скопировать команду claude mcp add для Claude CLI",
        2 => "Скопировать блок TOML для config.toml Codex",
        3 => "Скопировать JSON для opencode.json",
        4 => "Скопировать адрес и заголовок авторизации",
        _ => "Скопировать JSON для .mcp.json агента",
    };

    public string ConnectSnippetAutomationName => EffectiveSnippetFormatIndex switch
    {
        1 => "Строка подключения к MCP-серверу для Claude CLI",
        2 => "Строка подключения к MCP-серверу в формате Codex TOML",
        3 => "Строка подключения к MCP-серверу для opencode.json",
        4 => "Адрес и заголовок подключения к MCP-серверу",
        _ => "Строка подключения к MCP-серверу в формате JSON",
    };

    public string StatusText
    {
        get
        {
            if (Server.IsRunning && Server.Endpoint is { } endpoint)
            {
                return Server.IsElevated
                    ? $"Сервер слушает {endpoint} (только чтение – права администратора)"
                    : $"Сервер слушает {endpoint}";
            }

            if (Preferences.EffectiveEnabled)
            {
                return Server.IsStarting && Server.LastError is null
                    ? "Сервер запускается…"
                    : "Сервер включён, но не поднялся";
            }

            return "Сервер выключен – агент подключиться не сможет";
        }
    }

    private int EffectiveSnippetFormatIndex =>
        SelectedSnippetFormatIndex >= 0 && SelectedSnippetFormatIndex < McpConnectSnippets.FormatCount
            ? SelectedSnippetFormatIndex
            : 0;

    public void Dispose()
    {
        _copyFeedbackTimer?.Stop();
        Preferences.PropertyChanged -= OnPreferencesPropertyChanged;
        Server.PropertyChanged -= OnServerPropertyChanged;
    }

    [RelayCommand]
    private void CopyToken()
    {
        if (_clipboard.TrySetText(DisplayToken))
        {
            ShowCopyFeedback("Токен скопирован в буфер обмена");
            return;
        }

        ClearCopyFeedback();
        _dialogService.Error("Ошибка", "Не удалось скопировать токен в буфер обмена.");
    }

    private bool CanCopyConnectSnippet => HasConnectEndpoint;

    [RelayCommand(CanExecute = nameof(CanCopyConnectSnippet))]
    private void CopyConnectSnippet()
    {
        if (!HasConnectEndpoint)
        {
            return;
        }

        if (_clipboard.TrySetText(ConnectSnippet))
        {
            ShowCopyFeedback("Строка подключения скопирована");
            return;
        }

        ClearCopyFeedback();
        _dialogService.Error("Ошибка", "Не удалось скопировать строку подключения в буфер обмена.");
    }

    partial void OnSelectedSnippetFormatIndexChanged(int value)
    {
        ClearCopyFeedback();
    }

    private void ApplyPortText(string text)
    {
        var trimmed = text.Trim();

        if (!int.TryParse(trimmed, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed)
            || parsed < McpPreferences.PortMin
            || parsed > McpPreferences.PortMax)
        {
            PortError = $"Укажите целое число от {McpPreferences.PortMin} до {McpPreferences.PortMax}.";
            return;
        }

        PortError = null;

        var normalized = parsed.ToString(CultureInfo.InvariantCulture);

        if (!string.Equals(_portText, normalized, StringComparison.Ordinal))
        {
            _portText = normalized;
            OnPropertyChanged(nameof(PortText));
        }

        if (Preferences.Port != parsed)
        {
            _syncingPort = true;

            try
            {
                Preferences.Port = parsed;
            }
            finally
            {
                _syncingPort = false;
            }
        }
    }

    private void SyncPortTextFromPreferences()
    {
        if (_syncingPort)
        {
            return;
        }

        var synced = Preferences.Port.ToString(CultureInfo.InvariantCulture);
        PortError = null;

        if (!string.Equals(_portText, synced, StringComparison.Ordinal))
        {
            _portText = synced;
            OnPropertyChanged(nameof(PortText));
        }
    }

    private void ShowCopyFeedback(string message)
    {
        CopyFeedback = message;

        if (_copyFeedbackTimer is not null)
        {
            _copyFeedbackTimer.Stop();
            _copyFeedbackTimer.Start();
        }
    }

    private void ClearCopyFeedback()
    {
        _copyFeedbackTimer?.Stop();
        CopyFeedback = null;
    }

    private void OnPreferencesPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        var all = string.IsNullOrEmpty(e.PropertyName);

        if (all || e.PropertyName is nameof(McpPreferences.Port))
        {
            SyncPortTextFromPreferences();
        }

        if (all || e.PropertyName is nameof(McpPreferences.Port) or nameof(McpPreferences.EffectivePort))
        {
            OnPropertyChanged(nameof(HasConnectEndpoint));
            OnPropertyChanged(nameof(Url));
            OnPropertyChanged(nameof(ConnectSnippet));
            CopyConnectSnippetCommand.NotifyCanExecuteChanged();
        }

        if (all || e.PropertyName is nameof(McpPreferences.Token) or nameof(McpPreferences.EffectiveToken))
        {
            OnPropertyChanged(nameof(DisplayToken));
            OnPropertyChanged(nameof(ConnectSnippet));
        }

        if (all || e.PropertyName is nameof(McpPreferences.Enabled) or nameof(McpPreferences.EffectiveEnabled))
        {
            OnPropertyChanged(nameof(StatusText));
        }

        if (all || e.PropertyName is nameof(McpPreferences.AllowMutations) or nameof(McpPreferences.EffectiveAllowMutations))
        {
            OnPropertyChanged(nameof(AllowMutations));
            OnPropertyChanged(nameof(StatusText));
        }

        if (all || e.PropertyName is nameof(McpPreferences.Port)
                or nameof(McpPreferences.Token)
                or nameof(McpPreferences.EffectiveToken)
                or nameof(McpPreferences.Enabled)
                or nameof(McpPreferences.AllowMutations))
        {
            ClearCopyFeedback();
        }
    }

    private void OnServerPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        var all = string.IsNullOrEmpty(e.PropertyName);

        if (all || e.PropertyName is nameof(McpServerHost.BoundPort) or nameof(McpServerHost.Endpoint))
        {
            OnPropertyChanged(nameof(HasConnectEndpoint));
            OnPropertyChanged(nameof(Url));
            OnPropertyChanged(nameof(ConnectSnippet));
            CopyConnectSnippetCommand.NotifyCanExecuteChanged();
        }

        if (all || e.PropertyName is nameof(McpServerHost.Endpoint)
                or nameof(McpServerHost.IsRunning)
                or nameof(McpServerHost.IsStarting)
                or nameof(McpServerHost.LastError)
                or nameof(McpServerHost.BoundPort))
        {
            OnPropertyChanged(nameof(StatusText));
        }
    }
}
