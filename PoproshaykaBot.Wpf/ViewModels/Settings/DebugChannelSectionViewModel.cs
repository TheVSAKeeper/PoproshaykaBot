using CommunityToolkit.Mvvm.ComponentModel;
using PoproshaykaBot.Core.Debugging;
using PoproshaykaBot.Core.Settings.Debugging;

namespace PoproshaykaBot.Wpf.ViewModels.Settings;

public sealed partial class DebugChannelSectionViewModel : ObservableObject
{
    private readonly DebugChannelOverride _commandLine;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HintText))]
    [NotifyPropertyChangedFor(nameof(HintSeverity))]
    private bool _isEnabled;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HintText))]
    [NotifyPropertyChangedFor(nameof(HintSeverity))]
    private string _channel = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HintText))]
    [NotifyPropertyChangedFor(nameof(HintSeverity))]
    private bool _allowSending;

    public DebugChannelSectionViewModel(DebugChannelOverride commandLine)
    {
        ArgumentNullException.ThrowIfNull(commandLine);

        _commandLine = commandLine;
    }

    public string HintText
    {
        get
        {
            if (_commandLine.Channel is { Length: > 0 } fromCommandLine)
            {
                return $"Сейчас действует канал из командной строки: {fromCommandLine}. Настройки этого раздела применятся при следующем запуске без ключа {DebugChannelOverride.ChannelArgument}.";
            }

            if (!IsEnabled)
            {
                return "Бот работает на своём канале из раздела «Основные».";
            }

            if (!ChannelLogin.TryNormalize(Channel, out var login))
            {
                return "Укажите имя канала Twitch — например, mrbeast или ссылку на канал.";
            }

            return AllowSending
                ? $"Бот будет писать в чат канала {login} по-настоящему: приветствия, ответы на команды и рассылка."
                : $"Бот будет только читать чат канала {login}. Сообщения не отправляются, они видны в журнале.";
        }
    }

    public StatusSeverity HintSeverity
    {
        get
        {
            if (_commandLine.Channel is { Length: > 0 })
            {
                return StatusSeverity.None;
            }

            if (!IsEnabled)
            {
                return StatusSeverity.None;
            }

            if (!ChannelLogin.TryNormalize(Channel, out _))
            {
                return StatusSeverity.Error;
            }

            return AllowSending ? StatusSeverity.Warning : StatusSeverity.Success;
        }
    }

    public void LoadSettings(DebugChannelSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);

        IsEnabled = settings.IsEnabled;
        Channel = settings.Channel;
        AllowSending = settings.AllowSending;
    }

    public void SaveSettings(DebugChannelSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);

        settings.IsEnabled = IsEnabled;
        settings.Channel = ChannelLogin.TryNormalize(Channel, out var login) ? login : Channel.Trim();
        settings.AllowSending = AllowSending;
    }
}
