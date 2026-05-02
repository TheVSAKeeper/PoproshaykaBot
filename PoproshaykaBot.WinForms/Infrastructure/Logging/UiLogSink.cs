using Serilog.Core;
using Serilog.Events;
using Serilog.Formatting;
using Serilog.Formatting.Display;

namespace PoproshaykaBot.WinForms.Infrastructure.Logging;

public sealed class UiLogSink : ILogEventSink
{
    public const int BufferCapacity = 500;

    private const string DefaultOutputTemplate = "[{Timestamp:HH:mm:ss} {Level:u3}] {Message:lj}";

    private readonly object _lock = new();
    private readonly Queue<UiLogEntry> _buffer = new(BufferCapacity);
    private readonly ITextFormatter _formatter;

    public UiLogSink()
        : this(new MessageTemplateTextFormatter(DefaultOutputTemplate))
    {
    }

    public UiLogSink(ITextFormatter formatter)
    {
        _formatter = formatter;
    }

    public event Action<UiLogEntry>? Emitted;

    public void Emit(LogEvent logEvent)
    {
        using var writer = new StringWriter();
        _formatter.Format(logEvent, writer);
        var text = writer.ToString().TrimEnd('\r', '\n');
        var entry = new UiLogEntry(logEvent.Timestamp, logEvent.Level, text);

        lock (_lock)
        {
            if (_buffer.Count >= BufferCapacity)
            {
                _buffer.Dequeue();
            }

            _buffer.Enqueue(entry);
        }

        Emitted?.Invoke(entry);
    }

    public IReadOnlyList<UiLogEntry> Snapshot()
    {
        lock (_lock)
        {
            return _buffer.ToArray();
        }
    }
}

public sealed record UiLogEntry(DateTimeOffset Timestamp, LogEventLevel Level, string Text);
