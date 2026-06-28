using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using PoproshaykaBot.Core.Infrastructure.Events;
using PoproshaykaBot.Core.Infrastructure.Events.Lifecycle;
using PoproshaykaBot.Core.Server;
using PoproshaykaBot.Core.Settings;
using PoproshaykaBot.Core.Settings.Obs;
using PoproshaykaBot.Core.Settings.Stores;
using PoproshaykaBot.Core.Settings.Update;
using PoproshaykaBot.Core.Twitch.Auth;
using PoproshaykaBot.Wpf.ViewModels.Settings;
using System.ComponentModel;
using System.Text.Json;
using System.Windows;

namespace PoproshaykaBot.Wpf.ViewModels;

public sealed partial class SettingsPageViewModel : ObservableObject, IPageHeader, IDisposable
{
    private static readonly HashSet<string> NonDirtyProperties = new(StringComparer.Ordinal)
    {
        nameof(Settings.HttpServerSectionViewModel.IsRunning),
        nameof(Settings.HttpServerSectionViewModel.ServerStatusText),
        nameof(Settings.HttpServerSectionViewModel.ServerStatusSeverity),
        nameof(Settings.HttpServerSectionViewModel.ObsUrl),
        nameof(Settings.ObsIntegrationSectionViewModel.IsBusy),
        nameof(Settings.ObsIntegrationSectionViewModel.StatusText),
        nameof(Settings.ObsIntegrationSectionViewModel.StatusSeverity),
        nameof(Settings.ObsIntegrationSectionViewModel.OverlayUrl),
        nameof(Settings.UpdateSettingsSectionViewModel.DownloadProgress),
        nameof(Settings.UpdateSettingsSectionViewModel.CurrentVersionText),
        nameof(Settings.UpdateSettingsSectionViewModel.IsFrameworkDependentVisible),
        nameof(Settings.UpdateSettingsSectionViewModel.RepositoryHintText),
        nameof(Settings.UpdateSettingsSectionViewModel.RepositoryHintSeverity),
        "HasErrors",
    };

    private readonly SettingsManager _settingsManager;
    private readonly AccountsStore _accountsStore;
    private readonly ObsChatStore _obsChatStore;
    private readonly ObsIntegrationStore _obsIntegrationStore;
    private readonly UpdateStore _updateStore;
    private readonly IEventBus _eventBus;
    private readonly KestrelHttpServer _kestrelHttpServer;
    private readonly ILogger<SettingsPageViewModel> _logger;
    private readonly ObservableObject[] _dirtyTrackedSections;

    private AppSettings _settings = new();
    private TwitchAccountSettings _botDraft = new();
    private TwitchAccountSettings _broadcasterDraft = new();
    private ObsChatSettings _obsChatDraft = new();
    private string _obsChatBaselineJson = string.Empty;
    private ObsIntegrationSettings _obsIntegrationDraft = new();
    private UpdateSettings _updateDraft = new();
    private bool _suppressDirty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasChanges))]
    [NotifyCanExecuteChangedFor(nameof(SaveCommand))]
    private bool _dirty;

    public SettingsPageViewModel(
        BasicSettingsSectionViewModel basic,
        RateLimitingSettingsViewModel rateLimiting,
        HttpServerSectionViewModel httpServer,
        MessagesSettingsSectionViewModel messages,
        OAuthSettingsViewModel oauth,
        ObsChatSettingsSectionViewModel obsChat,
        ObsIntegrationSectionViewModel obsIntegration,
        AutoBroadcastSettingsViewModel autoBroadcast,
        BotLifecycleAutomationSectionViewModel botLifecycle,
        PollsSettingsSectionViewModel polls,
        MiscSettingsSectionViewModel misc,
        UpdateSettingsSectionViewModel update,
        SettingsManager settingsManager,
        AccountsStore accountsStore,
        ObsChatStore obsChatStore,
        ObsIntegrationStore obsIntegrationStore,
        UpdateStore updateStore,
        IEventBus eventBus,
        KestrelHttpServer kestrelHttpServer,
        ILogger<SettingsPageViewModel> logger)
    {
        Basic = basic;
        RateLimiting = rateLimiting;
        HttpServer = httpServer;
        Messages = messages;
        OAuth = oauth;
        ObsChat = obsChat;
        ObsIntegration = obsIntegration;
        AutoBroadcast = autoBroadcast;
        BotLifecycle = botLifecycle;
        Polls = polls;
        Misc = misc;
        Update = update;

        _settingsManager = settingsManager;
        _accountsStore = accountsStore;
        _obsChatStore = obsChatStore;
        _obsIntegrationStore = obsIntegrationStore;
        _updateStore = updateStore;
        _eventBus = eventBus;
        _kestrelHttpServer = kestrelHttpServer;
        _logger = logger;

        _dirtyTrackedSections = [Basic, RateLimiting, AutoBroadcast, BotLifecycle, ObsChat, ObsIntegration, Update];

        foreach (var section in _dirtyTrackedSections)
        {
            section.PropertyChanged += OnSectionPropertyChanged;
        }

        foreach (var entry in Messages.Entries)
        {
            entry.PropertyChanged += OnSectionPropertyChanged;
        }

        OAuth.SettingChanged += OnOAuthSettingChanged;
        Polls.PropertyChanged += OnPollsPropertyChanged;

        OnEnter();
    }

    public string PageTitle => "Настройки";

    public string? PageDescription => "Параметры бота, чата OBS, авторизации и обновлений.";

    public BasicSettingsSectionViewModel Basic { get; }

    public RateLimitingSettingsViewModel RateLimiting { get; }

    public HttpServerSectionViewModel HttpServer { get; }

    public MessagesSettingsSectionViewModel Messages { get; }

    public OAuthSettingsViewModel OAuth { get; }

    public ObsChatSettingsSectionViewModel ObsChat { get; }

    public ObsIntegrationSectionViewModel ObsIntegration { get; }

    public AutoBroadcastSettingsViewModel AutoBroadcast { get; }

    public BotLifecycleAutomationSectionViewModel BotLifecycle { get; }

    public PollsSettingsSectionViewModel Polls { get; }

    public MiscSettingsSectionViewModel Misc { get; }

    public UpdateSettingsSectionViewModel Update { get; }

    public bool HasChanges => Dirty || Polls.HasChanges;

    public void OnEnter()
    {
        _suppressDirty = true;

        try
        {
            _settings = JsonStoreClone.DeepClone(_settingsManager.Current);
            _botDraft = JsonStoreClone.DeepClone(_accountsStore.LoadBot());
            _broadcasterDraft = JsonStoreClone.DeepClone(_accountsStore.LoadBroadcaster());
            _obsChatDraft = JsonStoreClone.DeepClone(_obsChatStore.Load());
            _obsChatBaselineJson = SerializeObsChat(_obsChatDraft);
            _obsIntegrationDraft = JsonStoreClone.DeepClone(_obsIntegrationStore.Load());
            _updateDraft = JsonStoreClone.DeepClone(_updateStore.Load());

            Basic.LoadSettings(_settings.Twitch);
            RateLimiting.LoadSettings(_settings.Twitch);
            HttpServer.LoadSettings(_settings.Twitch);
            Messages.LoadSettings(_settings.Twitch.Messages);
            OAuth.Load(_settings, _botDraft, _broadcasterDraft);
            ObsChat.LoadFrom(_obsChatDraft);
            ObsIntegration.Load(_obsIntegrationDraft);
            AutoBroadcast.LoadSettings(_settings.Twitch.AutoBroadcast);
            BotLifecycle.LoadSettings(_settings.Twitch.BotLifecycleAutomation);
            Update.LoadSettings(_updateDraft);
            Polls.RevertCommand.Execute(null);

            OAuth.RefreshChannel(Basic.GetChannel());
        }
        finally
        {
            _suppressDirty = false;
        }

        ResetDirty();
    }

    public void Dispose()
    {
        foreach (var section in _dirtyTrackedSections)
        {
            section.PropertyChanged -= OnSectionPropertyChanged;
        }

        foreach (var entry in Messages.Entries)
        {
            entry.PropertyChanged -= OnSectionPropertyChanged;
        }

        OAuth.SettingChanged -= OnOAuthSettingChanged;
        Polls.PropertyChanged -= OnPollsPropertyChanged;
    }

    [RelayCommand(CanExecute = nameof(HasChanges))]
    private async Task SaveAsync()
    {
        try
        {
            Basic.SaveSettings(_settings.Twitch);
            RateLimiting.SaveSettings(_settings.Twitch);
            Messages.SaveSettings(_settings.Twitch.Messages);
            OAuth.Save(_settings);
            ObsChat.ApplyTo(_obsChatDraft);
            ObsIntegration.SaveTo(_obsIntegrationDraft);
            AutoBroadcast.SaveSettings(_settings.Twitch.AutoBroadcast);
            BotLifecycle.SaveSettings(_settings.Twitch.BotLifecycleAutomation);
            Update.SaveSettings(_updateDraft);

            var prevBotToken = _accountsStore.LoadBot().AccessToken;
            var prevBroadcasterToken = _accountsStore.LoadBroadcaster().AccessToken;
            var prevPort = _settingsManager.Current.Twitch.HttpServerPort;

            ReconcileHttpServerPort();
            var newPort = _settings.Twitch.HttpServerPort;

            _settingsManager.SaveSettings(_settings);
            _accountsStore.SaveAll(_botDraft, _broadcasterDraft);
            SaveObsChatDraftWithConflictCheck();
            _obsIntegrationStore.Save(_obsIntegrationDraft);
            _updateStore.Save(_updateDraft);

            if (Polls.HasChanges)
            {
                Polls.SaveCommand.Execute(null);
            }

            if (!string.Equals(prevBotToken, _botDraft.AccessToken, StringComparison.Ordinal))
            {
                await _eventBus.PublishAsync(new TwitchAuthorizationRefreshed(TwitchOAuthRole.Bot));
            }

            if (!string.Equals(prevBroadcasterToken, _broadcasterDraft.AccessToken, StringComparison.Ordinal))
            {
                await _eventBus.PublishAsync(new TwitchAuthorizationRefreshed(TwitchOAuthRole.Broadcaster));
            }

            var portChanged = newPort != prevPort;
            var restartFailed = false;

            if (portChanged && _kestrelHttpServer.IsRunning)
            {
                restartFailed = !await TryRestartHttpServerAsync(prevPort, newPort);
            }

            ResetDirty();

            if (restartFailed)
            {
                return;
            }

            var info = portChanged
                ? $"Настройки сохранены. HTTP сервер перезапущен на порту {newPort}."
                : "Настройки успешно сохранены.";

            StyledMessageBox.Show(info, "Настройки", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "Ошибка сохранения настроек");
            StyledMessageBox.Show($"Ошибка сохранения настроек: {exception.Message}", "Ошибка",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    [RelayCommand]
    private void Revert()
    {
        OnEnter();
    }

    private void ReconcileHttpServerPort()
    {
        if (!RedirectUriPortResolver.TryResolve(_settings.Twitch.RedirectUri, out var derivedPort))
        {
            _logger.LogWarning("Некорректный RedirectUri '{RedirectUri}' — порт HTTP сервера не обновлён",
                _settings.Twitch.RedirectUri);

            return;
        }

        if (_settings.Twitch.HttpServerPort == derivedPort)
        {
            return;
        }

        _logger.LogInformation("Порт HTTP сервера обновлён с {OldPort} на {NewPort} в соответствии с RedirectUri",
            _settings.Twitch.HttpServerPort, derivedPort);

        _settings.Twitch.HttpServerPort = derivedPort;
    }

    private async Task<bool> TryRestartHttpServerAsync(int prevPort, int newPort)
    {
        try
        {
            _logger.LogInformation("Перезапуск HTTP сервера: порт {OldPort} -> {NewPort}", prevPort, newPort);
            await _kestrelHttpServer.StopAsync();
            await _kestrelHttpServer.StartAsync();
            return true;
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "Ошибка перезапуска HTTP сервера на порту {NewPort}", newPort);
            StyledMessageBox.Show($"""
                                   Не удалось перезапустить HTTP сервер на порту {newPort}: {exception.Message}

                                   Настройки сохранены. Перезапустите приложение, чтобы изменения вступили в силу.
                                   """,
                "Ошибка перезапуска HTTP сервера",
                MessageBoxButton.OK,
                MessageBoxImage.Error);

            return false;
        }
    }

    private void SaveObsChatDraftWithConflictCheck()
    {
        var current = _obsChatStore.Load();
        var currentJson = SerializeObsChat(current);

        if (!string.Equals(currentJson, _obsChatBaselineJson, StringComparison.Ordinal))
        {
            var answer = StyledMessageBox.Show(
                """
                Настройки чат-оверлея были изменены извне (например, через демо-страницу) уже после открытия этой страницы.

                Перезаписать их значениями отсюда?

                «Да» – применить значения из этой страницы.
                «Нет» – оставить внешние изменения, не трогая вкладку «Чат OBS».
                """,
                "Конфликт настроек чат-оверлея",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);

            if (answer != MessageBoxResult.Yes)
            {
                _obsChatDraft = JsonStoreClone.DeepClone(current);

                _suppressDirty = true;

                try
                {
                    ObsChat.LoadFrom(_obsChatDraft);
                }
                finally
                {
                    _suppressDirty = false;
                }

                _obsChatBaselineJson = currentJson;
                return;
            }
        }

        _obsChatStore.Save(_obsChatDraft);
        _obsChatBaselineJson = SerializeObsChat(_obsChatDraft);
    }

    private void ResetDirty()
    {
        Dirty = false;
        OnPropertyChanged(nameof(HasChanges));
        SaveCommand.NotifyCanExecuteChanged();
    }

    private void OnSectionPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (_suppressDirty)
        {
            return;
        }

        if (e.PropertyName is { } name && NonDirtyProperties.Contains(name))
        {
            return;
        }

        if (ReferenceEquals(sender, Basic) && string.Equals(e.PropertyName, nameof(BasicSettingsSectionViewModel.Channel), StringComparison.Ordinal))
        {
            OAuth.RefreshChannel(Basic.GetChannel());
        }

        Dirty = true;
    }

    private void OnOAuthSettingChanged(object? sender, EventArgs e)
    {
        if (!_suppressDirty)
        {
            Dirty = true;
        }
    }

    private void OnPollsPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (string.Equals(e.PropertyName, nameof(PollsSettingsSectionViewModel.HasChanges), StringComparison.Ordinal))
        {
            OnPropertyChanged(nameof(HasChanges));
            SaveCommand.NotifyCanExecuteChanged();
        }
    }

    private static string SerializeObsChat(ObsChatSettings settings)
    {
        return JsonSerializer.Serialize(settings, JsonStoreOptions.Default);
    }
}
