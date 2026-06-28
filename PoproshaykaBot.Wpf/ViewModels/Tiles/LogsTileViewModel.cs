using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using KeepShell.Services;
using KeepShell.ViewModels;
using Microsoft.Extensions.Logging;
using PoproshaykaBot.Core.Infrastructure.Logging;
using PoproshaykaBot.Wpf.Bootstrap;
using System.Collections.Concurrent;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Windows;
using System.Windows.Threading;

namespace PoproshaykaBot.Wpf.ViewModels.Tiles;

public sealed partial class LogsTileViewModel : DashboardTileViewModel, IDisposable
{
    private const int Capacity = 1000;
    private const int MaxBatchPerTick = 256;

    private readonly UiLogSink _uiLogSink;
    private readonly ErrorReportService _errorReportService;
    private readonly ILogger<LogsTileViewModel> _logger;
    private readonly DispatcherTimer _flushTimer;
    private readonly Queue<LogRowViewModel> _buffer = new(Capacity);
    private readonly ConcurrentQueue<LogRowViewModel> _pendingRows = [];
    private bool _disposed;

    [ObservableProperty]
    private ObservableCollection<LogRowViewModel> _items = [];

    public LogsTileViewModel(UiLogSink uiLogSink, ErrorReportService errorReportService, ILogger<LogsTileViewModel> logger)
        : base("Логи")
    {
        _uiLogSink = uiLogSink;
        _errorReportService = errorReportService;
        _logger = logger;
        Items = [];

        _flushTimer = new(DispatcherPriority.Background)
        {
            Interval = TimeSpan.FromMilliseconds(150),
        };
        _flushTimer.Tick += OnFlushTick;
        _flushTimer.Start();

        foreach (var entry in uiLogSink.Snapshot())
        {
            AppendInternal(new LogRowViewModel(entry.Timestamp, entry.Level, entry.Text, null, null));
        }

        uiLogSink.Emitted += OnEmitted;
    }

    [RelayCommand]
    private void OpenSupport()
    {
        try
        {
            var payload = _errorReportService.Build(null, "Отчёт из плитки «Логи»");

            try
            {
                Clipboard.SetText(payload.LogClipboard);
            }
            catch (Exception clipboardException)
            {
                _logger.SupportClipboardCopyFailed(clipboardException);
            }

            Process.Start(new ProcessStartInfo(payload.IssueUrl) { UseShellExecute = true });
        }
        catch (Exception exception)
        {
            _logger.SupportReportFailed(exception);
        }
    }

    private void OnEmitted(UiLogEntry entry)
    {
        if (_disposed)
        {
            return;
        }

        var row = new LogRowViewModel(entry.Timestamp, entry.Level, entry.Text, null, null);
        _pendingRows.Enqueue(row);
    }

    private void OnFlushTick(object? sender, EventArgs e)
    {
        if (_pendingRows.IsEmpty)
        {
            return;
        }

        var drained = 0;

        while (drained < MaxBatchPerTick && _pendingRows.TryDequeue(out var row))
        {
            AppendInternal(row);
            drained++;
        }
    }

    private void AppendInternal(LogRowViewModel row)
    {
        _buffer.Enqueue(row);

        while (_buffer.Count > Capacity)
        {
            _buffer.Dequeue();
        }

        Items.Add(row);

        while (Items.Count > Capacity)
        {
            Items.RemoveAt(0);
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _flushTimer.Stop();
        _uiLogSink.Emitted -= OnEmitted;
    }
}
