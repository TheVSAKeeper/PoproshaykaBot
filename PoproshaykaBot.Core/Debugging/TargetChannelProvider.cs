using Microsoft.Extensions.Logging;
using PoproshaykaBot.Core.Infrastructure;
using PoproshaykaBot.Core.Settings;
using PoproshaykaBot.Core.Settings.Stores;

namespace PoproshaykaBot.Core.Debugging;

public sealed class TargetChannelProvider(
    SettingsManager settingsManager,
    DebugChannelStore debugChannelStore,
    DebugChannelOverride commandLine,
    ILogger<TargetChannelProvider>? logger = null)
    : ITargetChannelProvider
{
    private readonly object _syncLock = new();

    private TargetChannelState? _reported;
    private TargetChannelState? _session;

    public TargetChannelState Current
    {
        get
        {
            lock (_syncLock)
            {
                if (_session is { } pinned)
                {
                    return pinned;
                }
            }

            var (state, rejected) = Resolve();
            Report(state, rejected);
            return state;
        }
    }

    public void BeginSession()
    {
        var (state, rejected) = Resolve();
        Report(state, rejected);

        lock (_syncLock)
        {
            _session = state;
        }
    }

    public void EndSession()
    {
        lock (_syncLock)
        {
            _session = null;
        }
    }

    private (TargetChannelState State, string? Rejected) Resolve()
    {
        var own = settingsManager.Current.Twitch.Channel ?? string.Empty;
        var isolated = AppPaths.IsBaseDirectoryOverridden;

        if (commandLine.Channel is { Length: > 0 } fromCommandLine)
        {
            return (new(fromCommandLine, own, true, commandLine.AllowSending, isolated), null);
        }

        if (commandLine.RejectedChannel is { } rejectedArgument)
        {
            return (new(own, own, true, commandLine.AllowSending, isolated), rejectedArgument);
        }

        var stored = debugChannelStore.Load();

        if (!stored.IsEnabled)
        {
            return (new(own, own, false, true, isolated), null);
        }

        if (ChannelLogin.TryNormalize(stored.Channel, out var fromSettings))
        {
            return (new(fromSettings, own, true, stored.AllowSending || commandLine.AllowSending, isolated), null);
        }

        return (new(own, own, true, commandLine.AllowSending, isolated), stored.Channel ?? string.Empty);
    }

    private void Report(TargetChannelState state, string? rejected)
    {
        lock (_syncLock)
        {
            if (_reported == state)
            {
                return;
            }

            _reported = state;

            if (rejected is not null)
            {
                logger?.LogWarning("Отладка включена, но канал {Value} не распознан: бот остаётся на канале {OwnChannel} и молчит",
                    rejected.Length == 0 ? "(значение не указано)" : rejected,
                    state.OwnChannel);
            }

            if (!state.IsDebugSession)
            {
                return;
            }

            logger?.LogWarning("Режим отладки: бот работает на канале {Channel} (свой канал {OwnChannel}), отправка сообщений {Sending}",
                state.Login,
                state.OwnChannel,
                state.IsSendingAllowed ? "разрешена" : "выключена");
        }
    }
}
