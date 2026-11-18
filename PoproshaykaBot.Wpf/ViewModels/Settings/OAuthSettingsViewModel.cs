using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using PoproshaykaBot.Core.Platforms.Auth;
using PoproshaykaBot.Core.Settings;
using PoproshaykaBot.Core.Settings.Stores;
using PoproshaykaBot.Core.Twitch.Auth;
using PoproshaykaBot.Wpf.Infrastructure;
using System.ComponentModel.DataAnnotations;

namespace PoproshaykaBot.Wpf.ViewModels.Settings;

public sealed partial class OAuthSettingsViewModel : ObservableValidator, IDisposable
{
    [ObservableProperty]
    private string _clientId = string.Empty;

    [ObservableProperty]
    private string _clientSecret = string.Empty;

    [ObservableProperty]
    [NotifyDataErrorInfo]
    [CustomValidation(typeof(OAuthSettingsViewModel), nameof(ValidateRedirectUri))]
    private string _redirectUri = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ChannelHintText))]
    [NotifyPropertyChangedFor(nameof(ChannelHintSeverity))]
    private string _channel = string.Empty;

    public OAuthSettingsViewModel(
        ITwitchOAuthService oauthService,
        AccountsStore accountsStore,
        IDialogService dialogService,
        IShellLauncher shellLauncher,
        IEmbeddedTwitchAuthDialog embeddedAuth,
        IUiDispatcher uiDispatcher,
        TimeProvider timeProvider,
        ILoggerFactory loggerFactory)
    {
        var logger = loggerFactory.CreateLogger<PlatformOAuthAccountViewModel>();

        Bot = new(
            new TwitchAccountAuthAdapter(PlatformAuthRole.Bot, oauthService, accountsStore, GetCredentials, embeddedAuth),
            dialogService,
            shellLauncher,
            uiDispatcher,
            timeProvider,
            logger);

        Broadcaster = new(
            new TwitchAccountAuthAdapter(PlatformAuthRole.Broadcaster, oauthService, accountsStore, GetCredentials, embeddedAuth),
            dialogService,
            shellLauncher,
            uiDispatcher,
            timeProvider,
            logger);

        Bot.SettingChanged += OnChildSettingChanged;
        Broadcaster.SettingChanged += OnChildSettingChanged;
    }

    public event EventHandler? SettingChanged;

    public event EventHandler? LaunchOnboardingRequested;

    public string Title => "Авторизация";

    public PlatformOAuthAccountViewModel Bot { get; }

    public PlatformOAuthAccountViewModel Broadcaster { get; }

    public string ChannelHintText => string.IsNullOrWhiteSpace(Channel)
        ? "Подключите свой канал или добавьте его в карточке «Каналы»."
        : $"Канал: {Channel}";

    public StatusSeverity ChannelHintSeverity => string.IsNullOrWhiteSpace(Channel)
        ? StatusSeverity.Warning
        : StatusSeverity.None;

    public void Load(AppSettings settings, TwitchAccountSettings botDraft, TwitchAccountSettings broadcasterDraft)
    {
        LoadApplication(settings);
        Bot.Load(botDraft);
        Broadcaster.Load(broadcasterDraft);
    }

    public void LoadApplication(AppSettings settings)
    {
        ClientId = settings.Twitch.ClientId;
        ClientSecret = settings.Twitch.ClientSecret;
        RedirectUri = settings.Twitch.RedirectUri;
        Channel = settings.Twitch.Channel;

    }

    public void SaveTo(AppSettings settings, TwitchAccountSettings botTarget, TwitchAccountSettings broadcasterTarget)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(botTarget);
        ArgumentNullException.ThrowIfNull(broadcasterTarget);

        SaveApplication(settings);
        Bot.SaveTo(botTarget);
        Broadcaster.SaveTo(broadcasterTarget);
    }

    public void SaveApplication(AppSettings settings)
    {
        settings.Twitch.ClientId = ClientId.Trim();
        settings.Twitch.ClientSecret = ClientSecret.Trim();
        settings.Twitch.RedirectUri = RedirectUri.Trim();
    }

    public void RefreshChannel(string? channel)
    {
        Channel = channel ?? string.Empty;
    }

    public void Dispose()
    {
        Bot.SettingChanged -= OnChildSettingChanged;
        Broadcaster.SettingChanged -= OnChildSettingChanged;

        Bot.Dispose();
        Broadcaster.Dispose();
    }

    partial void OnClientIdChanged(string value)
    {
        RaiseSettingChanged();
    }

    partial void OnClientSecretChanged(string value)
    {
        RaiseSettingChanged();
    }

    partial void OnRedirectUriChanged(string value)
    {
        RaiseSettingChanged();
    }

    public static ValidationResult? ValidateRedirectUri(string? value, ValidationContext context)
    {
        var trimmed = value?.Trim();

        if (string.IsNullOrEmpty(trimmed))
        {
            return new("Укажите Redirect URI: на него Twitch возвращает код авторизации");
        }

        if (!Uri.TryCreate(trimmed, UriKind.Absolute, out var uri)
            || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
        {
            return new("Нужен абсолютный адрес http или https, например http://localhost:8080");
        }

        return uri.IsDefaultPort
            ? new ValidationResult("Укажите порт явно: по нему настраивается встроенный HTTP сервер")
            : ValidationResult.Success;
    }

    [RelayCommand]
    private void LaunchOnboarding()
    {
        LaunchOnboardingRequested?.Invoke(this, EventArgs.Empty);
    }

    private OAuthCredentialsSnapshot GetCredentials()
    {
        return new(ClientId.Trim(), ClientSecret.Trim(), RedirectUri.Trim(), Channel.Trim());
    }

    private void OnChildSettingChanged(object? sender, EventArgs e)
    {
        RaiseSettingChanged();
    }

    private void RaiseSettingChanged()
    {
        SettingChanged?.Invoke(this, EventArgs.Empty);
    }
}

public readonly record struct OAuthCredentialsSnapshot(string ClientId, string ClientSecret, string RedirectUri, string Channel);
