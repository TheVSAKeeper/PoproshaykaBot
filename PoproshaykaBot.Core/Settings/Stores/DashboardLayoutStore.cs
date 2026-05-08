using Microsoft.Extensions.Logging;
using PoproshaykaBot.Core.Infrastructure;
using PoproshaykaBot.Core.Infrastructure.Persistence;
using PoproshaykaBot.Core.Settings.Ui;
using System.Text.Json;

namespace PoproshaykaBot.Core.Settings.Stores;

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

        _logger?.LogDebug("DashboardLayoutStore инициализирован из {FilePath}", _filePath);
    }

    public virtual DashboardLayoutSettings? LoadDashboard()
    {
        lock (_syncLock)
        {
            return _state.Dashboard == null ? null : JsonStoreClone.DeepClone(_state.Dashboard);
        }
    }

    public virtual MainWindowSettings? LoadMainWindow()
    {
        lock (_syncLock)
        {
            return _state.MainWindow == null ? null : JsonStoreClone.DeepClone(_state.MainWindow);
        }
    }

    public virtual void SaveDashboard(DashboardLayoutSettings layout)
    {
        ArgumentNullException.ThrowIfNull(layout);

        lock (_syncLock)
        {
            _state.Dashboard = JsonStoreClone.DeepClone(layout);
            SaveInternal();

            _logger?.LogDebug("DashboardLayoutStore: раскладка дашборда сохранена");
        }
    }

    public virtual void SaveMainWindow(MainWindowSettings window)
    {
        ArgumentNullException.ThrowIfNull(window);

        lock (_syncLock)
        {
            _state.MainWindow = JsonStoreClone.DeepClone(window);
            SaveInternal();

            _logger?.LogDebug("DashboardLayoutStore: параметры главного окна сохранены");
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
            _logger?.LogDebug("DashboardLayoutStore: файл {FilePath} не найден, используются дефолты", _filePath);
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
