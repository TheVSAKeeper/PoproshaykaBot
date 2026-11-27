using PoproshaykaBot.Core.Settings;

namespace PoproshaykaBot.Core.Infrastructure;

public static class AppPaths
{
    public const string BaseDirectoryEnvironmentVariable = "POPROSHAYKA_BASE_DIR";
    public const string AppDataMarkerFileName = "appdata.flag";

    private const string AppDataFolderName = "PoproshaykaBot";

    private static readonly Lock SyncLock = new();
    private static bool _initialized;
    private static string? _baseDirectoryOverride;
    private static bool _isPortable;
    private static string _defaultBaseDirectory = string.Empty;
    private static string _baseDirectory = string.Empty;

    public static bool IsPortable
    {
        get
        {
            EnsureInitialized();
            lock (SyncLock)
            {
                return _isPortable;
            }
        }
    }

    public static bool IsBaseDirectoryOverridden
    {
        get
        {
            EnsureInitialized();
            lock (SyncLock)
            {
                return _baseDirectoryOverride is not null;
            }
        }
    }

    public static string BaseDirectory
    {
        get
        {
            EnsureInitialized();
            lock (SyncLock)
            {
                return _baseDirectory;
            }
        }
    }

    public static string DefaultBaseDirectory
    {
        get
        {
            EnsureInitialized();
            lock (SyncLock)
            {
                return _defaultBaseDirectory;
            }
        }
    }

    public static string AppDataDirectory =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), AppDataFolderName);

    public static string ExecutableDirectory => GetExecutableDirectory();

    public static string SettingsDirectory => Path.Combine(BaseDirectory, SettingsFileNames.SettingsFolderName);

    public static void Refresh()
    {
        lock (SyncLock)
        {
            RefreshCore(ExecutableDirectory);
        }
    }

    public static void SetAppDataStorageMarker(bool useAppData)
    {
        SetAppDataStorageMarker(useAppData, ExecutableDirectory);
    }

    internal static void SetAppDataStorageMarker(bool useAppData, string executableDirectory)
    {
        ArgumentException.ThrowIfNullOrEmpty(executableDirectory);

        var markerPath = Path.Combine(executableDirectory, AppDataMarkerFileName);
        if (useAppData)
        {
            Directory.CreateDirectory(executableDirectory);
            File.WriteAllText(markerPath, string.Empty);
        }
        else if (File.Exists(markerPath))
        {
            File.Delete(markerPath);
        }

        lock (SyncLock)
        {
            RefreshCore(executableDirectory);
        }
    }

    public static string Combine(string segment)
    {
        return Path.Combine(BaseDirectory, segment);
    }

    public static string Combine(string segment1, string segment2)
    {
        return Path.Combine(BaseDirectory, segment1, segment2);
    }

    public static string Combine(string segment1, string segment2, string segment3)
    {
        return Path.Combine(BaseDirectory, segment1, segment2, segment3);
    }

    public static string SettingsFile(string fileName)
    {
        return Path.Combine(BaseDirectory, SettingsFileNames.SettingsFolderName, fileName);
    }

    private static void EnsureInitialized()
    {
        lock (SyncLock)
        {
            if (!_initialized)
            {
                RefreshCore(GetExecutableDirectory());
            }
        }
    }

    private static void RefreshCore(string executableDirectory)
    {
        _baseDirectoryOverride = ResolveBaseDirectoryOverride();
        _isPortable = ResolvePortable(executableDirectory);
        _defaultBaseDirectory = _isPortable ? executableDirectory : AppDataDirectory;
        _baseDirectory = _baseDirectoryOverride ?? _defaultBaseDirectory;
        _initialized = true;
    }

    private static string? ResolveBaseDirectoryOverride()
    {
        var raw = Environment.GetEnvironmentVariable(BaseDirectoryEnvironmentVariable);
        return string.IsNullOrWhiteSpace(raw) ? null : Path.GetFullPath(raw);
    }

    internal static bool ResolvePortable(string executableDirectory)
    {
        return !File.Exists(Path.Combine(executableDirectory, AppDataMarkerFileName));
    }

    private static string GetExecutableDirectory()
    {
        var processPath = Environment.ProcessPath;

        if (!string.IsNullOrEmpty(processPath))
        {
            var directory = Path.GetDirectoryName(processPath);

            if (!string.IsNullOrEmpty(directory))
            {
                return directory;
            }
        }

        return AppContext.BaseDirectory;
    }
}
