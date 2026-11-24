using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using KeepShell.Services;
using PoproshaykaBot.Core.Platforms;
using PoproshaykaBot.Core.Platforms.Auth;
using PoproshaykaBot.Wpf.Bootstrap;
using PoproshaykaBot.Wpf.Infrastructure.Platforms;
using System.Windows.Input;

namespace PoproshaykaBot.Wpf.ViewModels.Tiles;

public sealed partial class ChatDisplayPaneViewModel(
    IShellLauncher shellLauncher, IChatPlatformRegistry? platformRegistry, ISettingsStore? uiSettings = null) : ObservableObject
{
    private Func<string, string, Task>? _signInBrowserAsync;
    private Action<string, string>? _resetBrowserSession;
    private bool _supportsWebChatLogin;
    private bool _updatingAccounts;
    private StreamingPlatform? _accountsPlatform;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasAccountChoices))]
    private IReadOnlyList<ChatDisplayAccountOption> _accountChoices = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanSignInBrowser), nameof(CanResetBrowserSession))]
    [NotifyCanExecuteChangedFor(nameof(SignInBrowserCommand), nameof(ResetBrowserSessionCommand))]
    private ChatDisplayAccountOption? _selectedAccount;

    [ObservableProperty]
    private string? _statusNotice;

    [ObservableProperty]
    private bool _isShown;

    [ObservableProperty]
    private bool _hasFallback;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(FallbackActionCommand), nameof(FallbackActionText))]
    private bool _runtimeMissing;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(FallbackActionCommand), nameof(FallbackActionText))]
    private Uri? _externalUri;

    [ObservableProperty]
    private string _fallbackHeading = string.Empty;

    [ObservableProperty]
    private string _fallbackDescription = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DisplayName), nameof(CanSignInBrowser), nameof(CanResetBrowserSession))]
    [NotifyCanExecuteChangedFor(nameof(SignInBrowserCommand), nameof(ResetBrowserSessionCommand))]
    private StreamingPlatform _platform;

    public string DisplayName => PlatformVisuals.GetDisplayName(Platform, platformRegistry);
    public Uri? ChannelUri { get; private set; }
    public bool SupportsClutterBlocking { get; private set; }
    public bool AppliesPreferredColorScheme { get; private set; }

    public bool HasAccountChoices => AccountChoices.Count > 1;

    public bool CanSignInBrowser => _supportsWebChatLogin
        && !string.IsNullOrWhiteSpace(SelectedAccount?.Id)
        && SelectedAccount.BrowserProfile is null
        && _signInBrowserAsync is not null;

    public bool CanResetBrowserSession => _supportsWebChatLogin
        && !string.IsNullOrWhiteSpace(SelectedAccount?.Id)
        && SelectedAccount.BrowserProfile is not null
        && _resetBrowserSession is not null;

    internal event Action? ChatAccountChanged;

    internal void SetBrowserSessionHandlers(
        bool supportsWebChatLogin,
        Func<string, string, Task>? signInBrowserAsync,
        Action<string, string>? resetBrowserSession)
    {
        _supportsWebChatLogin = supportsWebChatLogin;
        _signInBrowserAsync = signInBrowserAsync;
        _resetBrowserSession = resetBrowserSession;
        OnPropertyChanged(nameof(CanSignInBrowser));
        OnPropertyChanged(nameof(CanResetBrowserSession));
        SignInBrowserCommand.NotifyCanExecuteChanged();
        ResetBrowserSessionCommand.NotifyCanExecuteChanged();
    }

    [RelayCommand(CanExecute = nameof(CanSignInBrowser))]
    private async Task SignInBrowserAsync()
    {
        if (!CanSignInBrowser || SelectedAccount is not { } account || _signInBrowserAsync is null)
        {
            return;
        }

        await _signInBrowserAsync(account.Id, account.Label);
    }

    [RelayCommand(CanExecute = nameof(CanResetBrowserSession))]
    private void ResetBrowserSession()
    {
        if (!CanResetBrowserSession || SelectedAccount is not { } account || _resetBrowserSession is null)
        {
            return;
        }

        _resetBrowserSession(account.Id, account.Label);
    }

    internal void SetAccounts(IPlatformAccountDirectory? directory, string defaultLabel)
    {
        var options = new List<ChatDisplayAccountOption> { new(string.Empty, defaultLabel, null) };
        foreach (var account in directory?.ListAccounts() ?? [])
        {
            var authorizations = account.Authorizations.Where(static grant => grant.HasToken).ToArray();
            if (authorizations.Length == 0)
            {
                continue;
            }

            var browserProfile = authorizations.Select(grant => grant.ChatBrowserProfile).FirstOrDefault(profile => profile is not null);
            var roles = string.Join(", ", authorizations.Select(grant => PlatformAuthRoles.Describe(grant.Role)).Distinct(StringComparer.Ordinal));
            options.Add(new(account.Key.AccountId, $"{account.DisplayName} ({roles})", browserProfile));
        }

        if (_accountsPlatform == Platform && options.SequenceEqual(AccountChoices))
        {
            return;
        }

        var persistedId = uiSettings?.GetStringValue(AccountSettingKey)
            ?? (_accountsPlatform == Platform ? SelectedAccount?.Id : null);
        var previous = SelectedAccount;
        _updatingAccounts = true;
        try
        {
            AccountChoices = options;
            _accountsPlatform = Platform;
            SelectedAccount = options.Find(option => option.Id == persistedId) ?? options[0];
        }
        finally
        {
            _updatingAccounts = false;
        }

        ApplyAccountChange(previous, SelectedAccount);
    }

    private string AccountSettingKey => SettingsKeys.ChatTileAccountPrefix + Platform.ToString().ToLowerInvariant();

    partial void OnSelectedAccountChanged(ChatDisplayAccountOption? oldValue, ChatDisplayAccountOption? newValue)
    {
        if (_updatingAccounts || _accountsPlatform != Platform || newValue is null || !AccountChoices.Contains(newValue))
        {
            return;
        }

        uiSettings?.SetValue(AccountSettingKey, newValue.Id);
        ApplyAccountChange(oldValue, newValue);
    }

    private void ApplyAccountChange(ChatDisplayAccountOption? previous, ChatDisplayAccountOption? current)
    {
        if (previous?.Id != current?.Id)
        {
            StatusNotice = null;
        }

        if (previous?.BrowserProfile != current?.BrowserProfile)
        {
            ChatAccountChanged?.Invoke();
        }
    }

    public ICommand? FallbackActionCommand => RuntimeMissing ? OpenRuntimeDownloadCommand
        : ExternalUri is not null ? OpenInBrowserCommand : null;

    public string? FallbackActionText => RuntimeMissing ? "Скачать WebView2 Runtime"
        : ExternalUri is not null ? "Открыть в браузере" : null;

    internal bool SetTarget(
        StreamingPlatform platform,
        Uri? channelUri,
        Uri? externalUri,
        bool shown,
        bool supportsClutterBlocking = false,
        bool appliesPreferredColorScheme = false,
        string? unavailableHeading = null,
        string? unavailableDescription = null)
    {
        ExternalUri = externalUri;
        SupportsClutterBlocking = supportsClutterBlocking;
        AppliesPreferredColorScheme = appliesPreferredColorScheme;
        var changed = IsShown != shown || Platform != platform || ChannelUri != channelUri;
        if (changed)
        {
            StatusNotice = null;
        }

        IsShown = shown;
        Platform = platform;
        ChannelUri = channelUri;
        if (changed && shown)
        {
            HasFallback = channelUri is null;
            RuntimeMissing = false;
            if (channelUri is null)
            {
                FallbackHeading = unavailableHeading ?? "Чат эфира недоступен";
                FallbackDescription = unavailableDescription ?? "Чат появится, когда у выбранного канала начнётся эфир с доступным чатом.";
            }
        }

        return changed;
    }

    internal void ShowRuntimeMissingFallback()
    {
        HasFallback = true;
        RuntimeMissing = true;
        FallbackHeading = "Требуется Microsoft Edge WebView2 Runtime";
        FallbackDescription = "Установите его, чтобы открыть чат в приложении.";
    }

    internal void ShowGenericFallback()
    {
        HasFallback = true;
        RuntimeMissing = false;
        FallbackHeading = $"Не удалось открыть чат {DisplayName}";
        FallbackDescription = ExternalUri is not null ? "Откройте чат в браузере или попробуйте обновить его."
            : "Попробуйте обновить чат. Подробности – в журнале.";
    }

    [RelayCommand]
    private void OpenInBrowser()
    {
        if (ExternalUri is not null)
        {
            shellLauncher.Open(ExternalUri.AbsoluteUri);
        }
    }

    [RelayCommand]
    private void OpenRuntimeDownload() => shellLauncher.Open("https://go.microsoft.com/fwlink/p/?LinkId=2124703");
}

public sealed record ChatDisplayAccountOption(
    string Id, string Label, PlatformChatBrowserProfile? BrowserProfile);
