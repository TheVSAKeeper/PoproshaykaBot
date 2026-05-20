namespace PoproshaykaBot.WinForms.Widgets;

public sealed partial class ObsOutputStatusCard : UserControl
{
    public ObsOutputStatusCard()
    {
        InitializeComponent();
    }

    public void Show(string title, bool? active, bool? paused, string? timecode)
    {
        _titleLabel.Text = title;

        var resolution = ResolveState(active, paused);
        _stateLabel.Text = resolution.Text;
        _stateLabel.ForeColor = resolution.Color;

        var formatted = resolution.ShowTimecode ? FormatTimecode(timecode) : null;
        _timecodeLabel.Text = formatted ?? string.Empty;
        _timecodeLabel.Visible = !string.IsNullOrEmpty(formatted);
    }

    private static (string Text, Color Color, bool ShowTimecode) ResolveState(bool? active, bool? paused)
    {
        return (active, paused) switch
        {
            (true, true) => ("⏸ пауза", Color.Orange, true),
            (true, _) => ("● идёт", Color.Green, true),
            (false, _) => ("○ нет", Color.DimGray, false),
            _ => ("— неизвестно", Color.Gray, false),
        };
    }

    private static string? FormatTimecode(string? timecode)
    {
        if (string.IsNullOrWhiteSpace(timecode))
        {
            return null;
        }

        var dotIndex = timecode.IndexOf('.');
        var withoutMilliseconds = dotIndex >= 0 ? timecode[..dotIndex] : timecode;

        var parts = withoutMilliseconds.Split(':');
        if (parts.Length != 3)
        {
            return withoutMilliseconds;
        }

        var trimmedHours = parts[0].TrimStart('0');
        return trimmedHours.Length == 0
            ? $"{parts[1]}:{parts[2]}"
            : $"{trimmedHours}:{parts[1]}:{parts[2]}";
    }
}
