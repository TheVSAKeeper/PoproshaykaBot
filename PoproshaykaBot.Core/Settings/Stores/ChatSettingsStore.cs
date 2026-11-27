using Microsoft.Extensions.Logging;
using PoproshaykaBot.Core.Chat.History;
using PoproshaykaBot.Core.Infrastructure;
using PoproshaykaBot.Core.Settings.Chat;

namespace PoproshaykaBot.Core.Settings.Stores;

public sealed class ChatSettingsStore : IChatHistorySettingsProvider
{
    private readonly SettingsManager? _settingsManager;
    private readonly JsonStore<ChatSettings> _store;
    private readonly Lock _overrideLock = new();
    private bool _hasExplicitValue;

    public ChatSettingsStore(
        SettingsManager? settingsManager = null,
        ILogger<ChatSettingsStore>? logger = null,
        string? filePath = null)
    {
        var path = filePath ?? AppPaths.SettingsFile(SettingsFileNames.ChatSettingsFileName);

        _settingsManager = settingsManager;
        _hasExplicitValue = File.Exists(path);
        _store = new(path, logger, describe: Describe);

        logger?.LogDebug("ChatSettingsStore инициализирован из {FilePath}", path);
    }

    public event Action<ChatSettings>? Changed;

    public static string Describe(ChatSettings settings)
    {
        return SettingsDescriber.Describe(settings);
    }

    public ChatSettings Load()
    {
        var loaded = _store.Load();
        loaded.HistoryMaxItems = ResolveHistoryMaxItems(loaded);
        return loaded;
    }

    public ChatSettings GetCurrent()
    {
        return Load();
    }

    public bool Save(ChatSettings value)
    {
        ArgumentNullException.ThrowIfNull(value);

        var snapshot = JsonStoreClone.DeepClone(value);
        snapshot.HistoryMaxItems = ChatSettings.ClampHistoryMaxItems(snapshot.HistoryMaxItems);

        var written = _store.Save(snapshot);

        lock (_overrideLock)
        {
            _hasExplicitValue = true;
        }

        Changed?.Invoke(JsonStoreClone.DeepClone(snapshot));
        return written;
    }

    public bool Mutate(Action<ChatSettings> mutator)
    {
        ArgumentNullException.ThrowIfNull(mutator);

        ChatSettings? committed = null;
        var written = _store.Mutate(state =>
        {
            state.HistoryMaxItems = ResolveHistoryMaxItems(state);
            mutator(state);
            state.HistoryMaxItems = ChatSettings.ClampHistoryMaxItems(state.HistoryMaxItems);
            committed = JsonStoreClone.DeepClone(state);
        });

        lock (_overrideLock)
        {
            _hasExplicitValue = true;
        }

        if (committed is not null)
        {
            Changed?.Invoke(committed);
        }

        return written;
    }

    private int ResolveHistoryMaxItems(ChatSettings loaded)
    {
        bool hasExplicitValue;
        lock (_overrideLock)
        {
            hasExplicitValue = _hasExplicitValue;
        }

        if (!hasExplicitValue && _settingsManager is not null)
        {
            return ChatSettings.ClampHistoryMaxItems(_settingsManager.Current.Twitch.Infrastructure.ChatHistoryMaxItems);
        }

        return ChatSettings.ClampHistoryMaxItems(loaded.HistoryMaxItems);
    }
}
