using PoproshaykaBot.WinForms.Infrastructure.Di;
using PoproshaykaBot.WinForms.Infrastructure.Logging;

namespace PoproshaykaBot.WinForms.Tiles;

public sealed partial class LogsTileControl : UserControl
{
    private const int MaxLogLines = UiLogSink.BufferCapacity;

    private bool _initialized;

    public LogsTileControl()
    {
        InitializeComponent();
    }

    [Inject]
    public UiLogSink Sink { get; internal init; } = null!;

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

        foreach (var entry in Sink.Snapshot())
        {
            AppendEntry(entry);
        }

        Sink.Emitted += OnEmitted;
        Disposed += (_, _) => Sink.Emitted -= OnEmitted;
    }

    private void OnEmitted(UiLogEntry entry)
    {
        if (IsDisposed || !IsHandleCreated)
        {
            return;
        }

        if (InvokeRequired)
        {
            try
            {
                BeginInvoke(() =>
                {
                    if (IsDisposed || !IsHandleCreated)
                    {
                        return;
                    }

                    AppendEntry(entry);
                });
            }
            catch (ObjectDisposedException)
            {
            }
            catch (InvalidOperationException)
            {
            }

            return;
        }

        AppendEntry(entry);
    }

    private void AppendEntry(UiLogEntry entry)
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

        _logTextBox.AppendText(entry.Text + Environment.NewLine);
        _logTextBox.SelectionStart = _logTextBox.Text.Length;
        _logTextBox.ScrollToCaret();
    }
}
