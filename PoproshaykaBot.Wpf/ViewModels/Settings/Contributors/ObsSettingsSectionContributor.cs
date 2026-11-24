using MahApps.Metro.IconPacks;
using PoproshaykaBot.Core.Obs;
using PoproshaykaBot.Core.Settings;
using PoproshaykaBot.Core.Settings.Obs;
using PoproshaykaBot.Core.Settings.Stores;
using System.ComponentModel;
using System.Text.Json;

namespace PoproshaykaBot.Wpf.ViewModels.Settings.Contributors;

public sealed class ObsSettingsSectionContributor : ISettingsSectionContributor
{
    private static readonly HashSet<string> NonDirtyProperties = new(StringComparer.Ordinal)
    {
        nameof(HttpServerSectionViewModel.IsRunning),
        nameof(HttpServerSectionViewModel.ServerStatusText),
        nameof(HttpServerSectionViewModel.ServerStatusSeverity),
        nameof(HttpServerSectionViewModel.ObsUrl),
        nameof(ObsIntegrationSectionViewModel.IsBusy),
        nameof(ObsIntegrationSectionViewModel.StatusText),
        nameof(ObsIntegrationSectionViewModel.StatusSeverity),
        nameof(ObsIntegrationSectionViewModel.OverlayUrl),
        nameof(ObsIntegrationSectionViewModel.HasMissingDashboardSources),
        nameof(ObsIntegrationSectionViewModel.MissingDashboardSourcesWarning),
        nameof(ObsIntegrationSectionViewModel.HasMissingChatRefreshSources),
        nameof(ObsIntegrationSectionViewModel.MissingChatRefreshSourcesWarning),
        nameof(ObsChatSettingsSectionViewModel.IsFlowTheme),
        "HasErrors",
    };

    private readonly ObsIntegrationSectionViewModel _obsIntegration;
    private readonly ObsChatSettingsSectionViewModel _obsChat;
    private readonly HttpServerSectionViewModel _httpServer;
    private readonly ObsIntegrationStore _obsIntegrationStore;
    private readonly ObsChatStore _obsChatStore;
    private readonly IDialogService _dialogService;

    private ObsIntegrationSettings _obsIntegrationDraft = new();
    private ObsChatSettings _obsChatDraft = new();
    private string _obsChatBaselineJson = string.Empty;
    private bool _suppressReloadDirty;

    public ObsSettingsSectionContributor(
        ObsIntegrationSectionViewModel obsIntegration,
        ObsChatSettingsSectionViewModel obsChat,
        HttpServerSectionViewModel httpServer,
        ObsIntegrationStore obsIntegrationStore,
        ObsChatStore obsChatStore,
        IDialogService dialogService)
    {
        ArgumentNullException.ThrowIfNull(obsIntegration);
        ArgumentNullException.ThrowIfNull(obsChat);
        ArgumentNullException.ThrowIfNull(httpServer);
        ArgumentNullException.ThrowIfNull(obsIntegrationStore);
        ArgumentNullException.ThrowIfNull(obsChatStore);
        ArgumentNullException.ThrowIfNull(dialogService);

        _obsIntegration = obsIntegration;
        _obsChat = obsChat;
        _httpServer = httpServer;
        _obsIntegrationStore = obsIntegrationStore;
        _obsChatStore = obsChatStore;
        _dialogService = dialogService;
    }

    public int Order => 4;

    public string SectionKey => "obs";

    public SettingsSection CreateSection()
    {
        return new(
            SectionKey,
            "OBS",
            PackIconLucideKind.MonitorPlay,
            "websocket подключение хост пароль сцена браузер источник оверлей чат тема flow обтекаемый цвета шрифт анимация http сервер порт",
            [
                new SettingsSubsection("connection", "Подключение к OBS"),
                new SettingsSubsection("overlay", "Оформление чат-оверлея"),
                new SettingsSubsection("server", "HTTP сервер оверлея"),
            ]);
    }

    public IReadOnlyList<SettingsSectionBlockDescriptor> GetBlocks()
    {
        return
        [
            new(
                "obs/connection",
                "Подключение к OBS",
                "Адрес и пароль WebSocket, через который приложение управляет OBS.",
                _obsIntegration),
            new(
                "obs/overlay",
                "Оформление чат-оверлея",
                "Как чат выглядит в браузерном источнике на трансляции.",
                _obsChat),
            new(
                "obs/server",
                "HTTP сервер оверлея",
                "Порт и адреса страниц, которые открывает браузерный источник.",
                _httpServer),
        ];
    }

    public void Load(SettingsSessionState session)
    {
        ArgumentNullException.ThrowIfNull(session);

        _obsChatDraft = JsonStoreClone.DeepClone(_obsChatStore.Load());
        _obsChatBaselineJson = SerializeObsChat(_obsChatDraft);
        _obsIntegrationDraft = JsonStoreClone.DeepClone(_obsIntegrationStore.Load());

        _httpServer.LoadSettings(session.Settings.Twitch);
        _obsChat.LoadFrom(_obsChatDraft);
        _obsIntegration.Load(_obsIntegrationDraft);
    }

    public IDisposable SubscribeDirty(SettingsSessionState session, Action markDirty, Action notifyChangesState)
    {
        ArgumentNullException.ThrowIfNull(markDirty);

        var subscription = new CompositeSettingsSubscription();

        void OnPropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (_suppressReloadDirty)
            {
                return;
            }

            if (e.PropertyName is { } name && NonDirtyProperties.Contains(name))
            {
                return;
            }

            if (_obsIntegration.IsAutoPopulating
                && (ReferenceEquals(sender, _obsIntegration) || sender is ObsSourceItemViewModel))
            {
                return;
            }

            markDirty();
        }

        subscription.TrackPropertyChanged(_obsChat, OnPropertyChanged);
        subscription.TrackPropertyChanged(_obsIntegration, OnPropertyChanged);
        subscription.TrackCollectionItems(_obsIntegration.DashboardSources, OnPropertyChanged);
        subscription.TrackCollectionItems(_obsIntegration.ChatRefreshSources, OnPropertyChanged);

        return subscription;
    }

    public void BeginSave(AppSettings targetSettings)
    {
        _obsChat.ApplyTo(_obsChatDraft);
        _obsIntegration.SaveTo(_obsIntegrationDraft);
    }

    public Task<SettingsSectionSaveOutcome> SaveAsync(AppSettings targetSettings, bool appSettingsWritten)
    {
        SaveObsChatDraftWithConflictCheck();
        _obsIntegrationStore.Save(_obsIntegrationDraft);
        return Task.FromResult(SettingsSectionSaveOutcome.Clean);
    }

    public void OnSectionShown()
    {
        _obsIntegration.RunAutoConnectionCheck();
    }

    private void SaveObsChatDraftWithConflictCheck()
    {
        var current = _obsChatStore.Load();
        var currentJson = SerializeObsChat(current);

        if (!string.Equals(currentJson, _obsChatBaselineJson, StringComparison.Ordinal)
            && !_dialogService.ConfirmWarning(
                "Конфликт настроек чат-оверлея",
                "Настройки чат-оверлея были изменены извне (например, через демо-страницу) уже после открытия этой страницы.\n\nПерезаписать их значениями отсюда?\n\n«Да» – применить значения из этой страницы.\n«Нет» – оставить внешние изменения, не трогая раздел «Оформление чат-оверлея»."))
        {
            _obsChatDraft = JsonStoreClone.DeepClone(current);
            _suppressReloadDirty = true;
            try
            {
                _obsChat.LoadFrom(_obsChatDraft);
            }
            finally
            {
                _suppressReloadDirty = false;
            }

            _obsChatBaselineJson = currentJson;
            return;
        }

        _obsChatStore.Save(_obsChatDraft);
        _obsChatBaselineJson = SerializeObsChat(_obsChatDraft);
    }

    private static string SerializeObsChat(ObsChatSettings settings)
    {
        return JsonSerializer.Serialize(settings, JsonStoreOptions.Default);
    }
}
