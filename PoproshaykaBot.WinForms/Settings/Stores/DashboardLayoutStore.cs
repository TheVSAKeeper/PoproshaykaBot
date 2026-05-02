using Microsoft.Extensions.Logging;
using PoproshaykaBot.WinForms.Infrastructure;
using PoproshaykaBot.WinForms.Infrastructure.Persistence;
using PoproshaykaBot.WinForms.Settings.Ui;
using System.Text.Json;

namespace PoproshaykaBot.WinForms.Settings.Stores;

public class DashboardLayoutStore
{
    private readonly ILogger<DashboardLayoutStore>? _logger;
    private readonly string _filePath;
    private readonly object _syncLock = new();

    private readonly DashboardLayoutFileDto _state;

    public DashboardLayoutStore(ILogger<DashboardLayoutStore>? logger = null, string? filePath = null)
    {
        _logger = logger;
        _filePath = filePath ?? AppPaths.SettingsFile("dashboard-layout.json");
        _state = ReadFile();
    }

    public virtual DashboardLayoutSettings? LoadDashboard()
    {
        return _state.Dashboard;
    }

    public virtual MainWindowSettings? LoadMainWindow()
    {
        return _state.MainWindow;
    }

    public virtual void SaveDashboard(DashboardLayoutSettings layout)
    {
        ArgumentNullException.ThrowIfNull(layout);

        lock (_syncLock)
        {
            _state.Dashboard = layout;
            SaveInternal();
        }
    }

    public virtual void SaveMainWindow(MainWindowSettings window)
    {
        ArgumentNullException.ThrowIfNull(window);

        lock (_syncLock)
        {
            _state.MainWindow = window;
            SaveInternal();
        }
    }

    private void SaveInternal()
    {
        var json = JsonSerializer.Serialize(_state, JsonStoreOptions.Default);
        AtomicFile.Save(_filePath, json, _logger);
    }

    private DashboardLayoutFileDto ReadFile()
    {
        if (!File.Exists(_filePath))
        {
            return new();
        }

        try
        {
            var json = File.ReadAllText(_filePath);
            return JsonSerializer.Deserialize<DashboardLayoutFileDto>(json, JsonStoreOptions.Default) ?? new();
        }
        catch (Exception exception)
        {
            _logger?.LogError(exception, "Ошибка чтения {FilePath}, применяются дефолты", _filePath);
            JsonStoreBackup.CreateBackup(_filePath, "invalid", _logger);
            return new();
        }
    }

    private sealed class DashboardLayoutFileDto
    {
        public DashboardLayoutSettings? Dashboard { get; set; }
        public MainWindowSettings? MainWindow { get; set; }
    }
}
