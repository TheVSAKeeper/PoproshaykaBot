using Timer = System.Windows.Forms.Timer;

namespace PoproshaykaBot.WinForms.Controls;

internal sealed class ToolStripStatusIndicator : IDisposable
{
    private const int AutoResetIntervalMs = 5000;

    private readonly Timer _resetTimer;

    public ToolStripStatusIndicator()
    {
        Label = new()
        {
            Text = string.Empty,
            Margin = new(8, 0, 0, 0),
        };

        _resetTimer = new()
        {
            Interval = AutoResetIntervalMs,
        };

        _resetTimer.Tick += OnResetTick;
    }

    public ToolStripLabel Label { get; }

    public void Show(string text, bool isError)
    {
        Label.Text = text;
        Label.ForeColor = isError ? Color.Firebrick : SystemColors.ControlText;

        _resetTimer.Stop();

        if (!string.IsNullOrEmpty(text))
        {
            _resetTimer.Start();
        }
    }

    public void Clear()
    {
        Show(string.Empty, false);
    }

    public void Dispose()
    {
        _resetTimer.Tick -= OnResetTick;
        _resetTimer.Dispose();
    }

    private void OnResetTick(object? sender, EventArgs e)
    {
        _resetTimer.Stop();
        Label.Text = string.Empty;
        Label.ForeColor = SystemColors.ControlText;
    }
}
