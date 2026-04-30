using PoproshaykaBot.WinForms.Infrastructure.Di;
using PoproshaykaBot.WinForms.Infrastructure.Events;
using PoproshaykaBot.WinForms.Infrastructure.Events.Logging;

namespace PoproshaykaBot.WinForms.Tiles;

public sealed partial class LogsTileControl : UserControl
{
    private const int MaxLogLines = 500;

    private readonly List<IDisposable> _subs = [];
    private bool _initialized;

    public LogsTileControl()
    {
        InitializeComponent();
    }

    [Inject]
    public IEventBus Bus { get; internal init; } = null!;

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

        _subs.Add(Bus.SubscribeOnUi<BotLogEntry>(this, OnBotLogEntry));
        _subs.DisposeOnClose(this);
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

    private void AddLogMessage(string message)
    {
        if (_logTextBox.Lines.Length > MaxLogLines)
        {
            var charIndex = _logTextBox.GetFirstCharIndexFromLine(_logTextBox.Lines.Length - MaxLogLines);
            if (charIndex > 0)
            {
                _logTextBox.Select(0, charIndex);
                _logTextBox.SelectedText = string.Empty;
            }
        }

        _logTextBox.AppendText($"{DateTime.Now:HH:mm:ss} - {message}{Environment.NewLine}");
        _logTextBox.SelectionStart = _logTextBox.Text.Length;
        _logTextBox.ScrollToCaret();
    }
}
