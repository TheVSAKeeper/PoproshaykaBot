using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MahApps.Metro.IconPacks;
using Microsoft.Extensions.Logging;
using PoproshaykaBot.Core.Infrastructure.Events;
using PoproshaykaBot.Core.Infrastructure.Events.Lifecycle;
using PoproshaykaBot.Core.Infrastructure.Events.Settings;
using PoproshaykaBot.Core.Server;
using PoproshaykaBot.Core.Settings;
using PoproshaykaBot.Core.Settings.Obs;
using PoproshaykaBot.Core.Settings.Stores;
using PoproshaykaBot.Core.Settings.Update;
using PoproshaykaBot.Core.Twitch.Auth;
using PoproshaykaBot.Wpf.ViewModels.Settings;
using System.ComponentModel;
using PoproshaykaBot.Wpf.Bootstrap;
using System.Text.Json;

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

    private const string ObsSectionKey = "obs";

    private readonly SettingsManager _settingsManager;
    private readonly AccountsStore _accountsStore;
    private readonly ObsChatStore _obsChatStore;
    private readonly ObsIntegrationStore _obsIntegrationStore;
    private readonly UpdateStore _updateStore;
    private readonly DashboardLayoutStore _dashboardLayoutStore;
    private readonly IEventBus _eventBus;
    private readonly KestrelHttpServer _kestrelHttpServer;
    private readonly ILogger<SettingsPageViewModel> _logger;
    private readonly IDialogService _dialogService;

    private readonly ObservableObject[] _dirtyTrackedSections;
    private readonly ISettingsStore _uiSettings;

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

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SearchTextEmpty))]
    private string _searchText = string.Empty;

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
        DashboardLayoutSectionViewModel dashboardLayout,
        ShellPreferences shell,
        SettingsManager settingsManager,
        AccountsStore accountsStore,
        ObsChatStore obsChatStore,
        ObsIntegrationStore obsIntegrationStore,
        UpdateStore updateStore,
        DashboardLayoutStore dashboardLayoutStore,
        IEventBus eventBus,
        KestrelHttpServer kestrelHttpServer,
        ILogger<SettingsPageViewModel> logger,
        IDialogService dialogService,
        ISettingsStore uiSettings)
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
        DashboardLayout = dashboardLayout;
        Shell = shell;

        _settingsManager = settingsManager;
        _accountsStore = accountsStore;
        _obsChatStore = obsChatStore;
        _obsIntegrationStore = obsIntegrationStore;
        _updateStore = updateStore;
        _dashboardLayoutStore = dashboardLayoutStore;
        _eventBus = eventBus;
        _kestrelHttpServer = kestrelHttpServer;
        _logger = logger;
        _dialogService = dialogService;
        _uiSettings = uiSettings;

        Sections.Restore(uiSettings.GetStringValue(SettingsKeys.SettingsSection));
        Sections.PropertyChanged += OnSectionsPropertyChanged;

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
        DashboardLayout.Edited += OnDashboardLayoutEdited;

        OnEnter();
    }

    public event EventHandler? SettingsSaved;

    public string PageTitle => "Настройки";

    public string? PageDescription => "Параметры бота, чата OBS, авторизации и обновлений.";

    public SettingsSectionList Sections { get; } = new(
        new SettingsSection("basic", "Основные", PackIconLucideKind.Settings2, "канал twitch аккаунт отображения чата лимиты ограничения отправки задержка приветствие сообщения шаблоны текст"),
        new SettingsSection("oauth", "Авторизация", PackIconLucideKind.KeyRound, "oauth токен client id secret redirect uri scopes бот стример вещатель права доступа вход"),
        new SettingsSection("obs", "OBS", PackIconLucideKind.MonitorPlay, "websocket подключение хост пароль сцена браузер источник оверлей чат цвета шрифт анимация http сервер порт"),
        new SettingsSection("stream", "Трансляция", PackIconLucideKind.Radio, "автоматический режим рассылка eventsub автозапуск бота остановка опросы голосование категория название"),
        new SettingsSection("dashboard", "Дашборд", PackIconLucideKind.LayoutDashboard, "раскладка плитки сетка колонки строки обзор палитра перетаскивание"),
        new SettingsSection("update", "Обновления", PackIconLucideKind.Download, "github релизы версия репозиторий проверка загрузка установка портативная сборка"),
        new SettingsSection("appearance", "Оформление", PackIconLucideKind.Palette, "тема масштаб шрифта размер текста заголовок страницы внешний вид"),
        new SettingsSection("misc", "Прочее", PackIconLucideKind.Wrench, "данные приложения папка настроек логи профили трансляций импорт экспорт сброс"));

    public bool SearchTextEmpty => string.IsNullOrWhiteSpace(SearchText);

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

    public DashboardLayoutSectionViewModel DashboardLayout { get; }

    public ShellPreferences Shell { get; }

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
            DashboardLayout.LoadSettings(_dashboardLayoutStore.LoadDashboard());
            Polls.RevertCommand.Execute(null);

            OAuth.RefreshChannel(Basic.GetChannel());
        }
        finally
        {
            _suppressDirty = false;
        }

        ResetDirty();
        RunObsAutoCheckIfSelected();
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
        DashboardLayout.Edited -= OnDashboardLayoutEdited;
        Sections.PropertyChanged -= OnSectionsPropertyChanged;
    }

    partial void OnSearchTextChanged(string value)
    {
        Sections.Filter(value);
    }

    [RelayCommand]
    private void ClearSearch()
    {
        SearchText = string.Empty;
    }

    private void OnSectionsPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (string.Equals(e.PropertyName, nameof(SettingsSectionList.Selected), StringComparison.Ordinal)
            && Sections.Selected is { } section)
        {
            _uiSettings.SetValue(SettingsKeys.SettingsSection, section.Key);
            RunObsAutoCheckIfSelected();
        }
    }

    private void RunObsAutoCheckIfSelected()
    {
        if (string.Equals(Sections.Selected?.Key, ObsSectionKey, StringComparison.Ordinal))
        {
            ObsIntegration.RunAutoConnectionCheck();
        }
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
            var prevChannel = _settingsManager.Current.Twitch.Channel;
            var prevChatDisplayAccount = _settingsManager.Current.Twitch.ChatDisplayAccount;

            ReconcileHttpServerPort();
            var newPort = _settings.Twitch.HttpServerPort;

            _settingsManager.SaveSettings(_settings);
            _accountsStore.SaveAll(_botDraft, _broadcasterDraft);
            SaveObsChatDraftWithConflictCheck();
            _obsIntegrationStore.Save(_obsIntegrationDraft);
            _updateStore.Save(_updateDraft);
            _dashboardLayoutStore.SaveDashboard(DashboardLayout.BuildLayout());

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

            if (!string.Equals(prevChannel, _settings.Twitch.Channel, StringComparison.OrdinalIgnoreCase)
                || prevChatDisplayAccount != _settings.Twitch.ChatDisplayAccount)
            {
                await _eventBus.PublishAsync(new ChatDisplaySettingsChanged(_settings.Twitch.Channel, _settings.Twitch.ChatDisplayAccount));
            }

            var portChanged = newPort != prevPort;
            var restartFailed = false;

            if (portChanged && _kestrelHttpServer.IsRunning)
            {
                restartFailed = !await TryRestartHttpServerAsync(prevPort, newPort);
            }

            ResetDirty();
            SettingsSaved?.Invoke(this, EventArgs.Empty);

            if (restartFailed)
            {
                return;
            }

            var info = portChanged
                ? $"Настройки сохранены. HTTP сервер перезапущен на порту {newPort}."
                : "Настройки успешно сохранены.";

            _dialogService.Info("Настройки", info);
        }
        catch (Exception exception)
        {
            _logger.SettingsSaveFailed(exception);
            _dialogService.Error("Ошибка", $"Ошибка сохранения настроек: {exception.Message}");
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
            _logger.SettingsRedirectUriInvalid(_settings.Twitch.RedirectUri);

            return;
        }

        if (_settings.Twitch.HttpServerPort == derivedPort)
        {
            return;
        }

        _logger.SettingsHttpPortUpdated(_settings.Twitch.HttpServerPort, derivedPort);

        _settings.Twitch.HttpServerPort = derivedPort;
    }

    private async Task<bool> TryRestartHttpServerAsync(int prevPort, int newPort)
    {
        try
        {
            _logger.SettingsHttpServerRestarting(prevPort, newPort);
            await _kestrelHttpServer.StopAsync();
            await _kestrelHttpServer.StartAsync();
            return true;
        }
        catch (Exception exception)
        {
            _logger.SettingsHttpServerRestartFailed(exception, newPort);
            _dialogService.Error(
                "Ошибка перезапуска HTTP сервера",
                $"Не удалось перезапустить HTTP сервер на порту {newPort}: {exception.Message}\n\nНастройки сохранены. Перезапустите приложение, чтобы изменения вступили в силу.");

            return false;
        }
    }

    private void SaveObsChatDraftWithConflictCheck()
    {
        var current = _obsChatStore.Load();
        var currentJson = SerializeObsChat(current);

        if (!string.Equals(currentJson, _obsChatBaselineJson, StringComparison.Ordinal))
        {
            if (!_dialogService.ConfirmWarning(
                "Конфликт настроек чат-оверлея",
                "Настройки чат-оверлея были изменены извне (например, через демо-страницу) уже после открытия этой страницы.\n\nПерезаписать их значениями отсюда?\n\n«Да» – применить значения из этой страницы.\n«Нет» – оставить внешние изменения, не трогая вкладку «Чат OBS»."))
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

        if (ReferenceEquals(sender, ObsIntegration) && ObsIntegration.IsAutoPopulating)
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

    private void OnDashboardLayoutEdited(object? sender, EventArgs e)
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
