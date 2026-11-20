using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using PoproshaykaBot.Core.Infrastructure.Events;
using PoproshaykaBot.Core.Platforms;
using PoproshaykaBot.Core.Server;
using PoproshaykaBot.Core.Settings;
using PoproshaykaBot.Core.Settings.Stores;
using PoproshaykaBot.Wpf.Bootstrap;
using PoproshaykaBot.Wpf.ViewModels.Settings;
using System.ComponentModel;

namespace PoproshaykaBot.Wpf.ViewModels;

public sealed partial class SettingsPageViewModel : ObservableObject, IPageHeader, IUnsavedChangesPage, IDisposable
{
    private const string NotWrittenNotice = "Настройки применены и работают до перезапуска. В файл они не записаны: туда только что перенесены данные предыдущей версии. Перезапустите приложение и сохраните настройки ещё раз.";

    private readonly IReadOnlyList<ISettingsSectionContributor> _contributors;
    private readonly Dictionary<string, ISettingsSectionContributor> _contributorsByKey;
    private readonly SettingsManager _settingsManager;
    private readonly SettingsWriteGate _settingsWriteGate;
    private readonly KestrelHttpServer _kestrelHttpServer;
    private readonly IEventBus _eventBus;
    private readonly ILogger<SettingsPageViewModel> _logger;
    private readonly IDialogService _dialogService;
    private readonly ISettingsStore _uiSettings;
    private readonly ChannelCatalogSettingsSectionViewModel? _channelCatalog;

    private readonly List<IDisposable> _dirtySubscriptions = [];
    private SettingsSessionState _session = new() { Settings = new() };
    private string _rememberedSection = string.Empty;
    private bool _suppressDirty;
    private bool _disposed;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SaveCommand), nameof(RevertCommand))]
    private bool _isSaving;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasChanges))]
    [NotifyPropertyChangedFor(nameof(HasDraft))]
    [NotifyCanExecuteChangedFor(nameof(SaveCommand))]
    private bool _dirty;

    public SettingsPageViewModel(
        IEnumerable<ISettingsSectionContributor> contributors,
        SettingsManager settingsManager,
        SettingsWriteGate settingsWriteGate,
        KestrelHttpServer kestrelHttpServer,
        IEventBus eventBus,
        ILogger<SettingsPageViewModel> logger,
        IDialogService dialogService,
        ISettingsStore uiSettings,
        BroadcastProfilesSettingsSectionViewModel? broadcastProfiles = null,
        IChatPlatformRegistry? platformRegistry = null)
    {
        ArgumentNullException.ThrowIfNull(contributors);
        ArgumentNullException.ThrowIfNull(settingsManager);
        ArgumentNullException.ThrowIfNull(settingsWriteGate);
        ArgumentNullException.ThrowIfNull(kestrelHttpServer);
        ArgumentNullException.ThrowIfNull(eventBus);
        ArgumentNullException.ThrowIfNull(logger);
        ArgumentNullException.ThrowIfNull(dialogService);
        ArgumentNullException.ThrowIfNull(uiSettings);

        _contributors = [.. contributors.OrderBy(c => c.Order)];
        _contributorsByKey = _contributors.ToDictionary(c => c.SectionKey, StringComparer.Ordinal);
        _settingsManager = settingsManager;
        _settingsWriteGate = settingsWriteGate;
        _kestrelHttpServer = kestrelHttpServer;
        _eventBus = eventBus;
        _logger = logger;
        _dialogService = dialogService;
        _uiSettings = uiSettings;

        var presentations = _contributors.Select(c =>
            new SettingsSectionPresentation(c.CreateSection(), c.GetBlocks(), c.IsFullWidth)).ToArray();
        var contents = presentations.SelectMany(page => page.Blocks).Select(block => block.Content).ToArray();
        _channelCatalog = contents.OfType<ChannelCatalogSettingsSectionViewModel>().FirstOrDefault();
        _channelCatalog?.SetCredentialDrafts(
            contents.OfType<OAuthSettingsViewModel>().FirstOrDefault(),
            contents.OfType<YouTubeSettingsSectionViewModel>().FirstOrDefault());
        var extracted = PlatformSettingsPages.Create(presentations, broadcastProfiles, platformRegistry);
        SectionPresentations = extracted.SettingsPresentations;
        PlatformPresentations = extracted.PlatformPresentations;
        PlatformLinkTargets = extracted.LinkTargets;
        Sections = new([.. SectionPresentations.Select(p => p.Section).Distinct()]);

        _rememberedSection = uiSettings.GetStringValue(SettingsKeys.SettingsSection) ?? string.Empty;
        Sections.Restore(PlatformSettingsPages.RestorePath(_rememberedSection));
        Sections.PropertyChanged += OnSectionsPropertyChanged;

        OnEnter();
    }

    public event EventHandler? SettingsSaved;
    public event EventHandler<StreamingPlatform?>? MultistreamRequested;

    public string PageTitle => "Настройки";

    public string? PageDescription => "Параметры бота, чата OBS, авторизации и обновлений.";

    public SettingsSectionList Sections { get; }

    public IReadOnlyList<SettingsSectionPresentation> SectionPresentations { get; }

    internal IReadOnlyList<SettingsSectionPresentation> PlatformPresentations { get; }

    public IReadOnlyList<PlatformLinkTarget> PlatformLinkTargets { get; }

    public string PlatformsCardTitle => PlatformSettingsPages.CardTitle;

    public bool HasChanges => Dirty || _contributors.Any(c => c.HasExternalChanges);

    public bool HasDraft => HasChanges
        || (Sections.Selected?.Key != PlatformSettingsPages.SectionKey
            && (Sections.Selected is not { } selected
                || !_contributorsByKey.TryGetValue(selected.Key, out var contributor)
                || !contributor.IsDraftless));

    public bool HasUnsavedChanges => HasChanges;

    public string UnsavedChangesSubject => "На странице настроек есть несохранённые изменения.";

    public T GetSectionContent<T>() where T : class
    {
        return SectionPresentations
            .Concat(PlatformPresentations)
            .SelectMany(p => p.Blocks)
            .Select(b => b.Content)
            .OfType<T>()
            .First();
    }

    public async Task<bool> TrySaveUnsavedChangesAsync()
    {
        if (IsSaving || _disposed)
        {
            return false;
        }

        await SaveCommand.ExecuteAsync(null);
        return !HasChanges;
    }

    public void DiscardUnsavedChanges()
    {
        RevertCommand.Execute(null);
    }

    public void OnEnter()
    {
        if (!CanReloadDraft())
        {
            return;
        }

        ReloadDraft();
        RunSelectedSectionEntryChecks();
    }

    internal void ReloadDraft()
    {
        if (!CanReloadDraft())
        {
            return;
        }

        _suppressDirty = true;

        try
        {
            _session = new()
            {
                Settings = JsonStoreClone.DeepClone(_settingsManager.Current),
            };

            DisposeDirtySubscriptions();

            foreach (var contributor in _contributors)
            {
                contributor.Load(_session);
                _dirtySubscriptions.Add(contributor.SubscribeDirty(_session, MarkDirty, NotifyChangesState));
            }
        }
        finally
        {
            _suppressDirty = false;
        }

        ResetDirty();
    }

    public void Dispose()
    {
        _disposed = true;
        Sections.PropertyChanged -= OnSectionsPropertyChanged;
        DisposeDirtySubscriptions();

        foreach (var contributor in _contributors)
        {
            (contributor as IDisposable)?.Dispose();
        }
    }

    private void DisposeDirtySubscriptions()
    {
        foreach (var subscription in _dirtySubscriptions)
        {
            subscription.Dispose();
        }

        _dirtySubscriptions.Clear();
    }

    private void MarkDirty()
    {
        if (!_suppressDirty)
        {
            Dirty = true;
        }
    }

    private void NotifyChangesState()
    {
        OnPropertyChanged(nameof(HasChanges));
        OnPropertyChanged(nameof(HasDraft));
        SaveCommand.NotifyCanExecuteChanged();
    }

    private void ResetDirty()
    {
        Dirty = false;
        NotifyChangesState();
    }

    private void OnSectionsPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (string.Equals(e.PropertyName, nameof(SettingsSectionList.SelectedPath), StringComparison.Ordinal))
        {
            RememberSelection();
            return;
        }

        if (string.Equals(e.PropertyName, nameof(SettingsSectionList.Selected), StringComparison.Ordinal)
            && Sections.Selected is not null)
        {
            OnPropertyChanged(nameof(HasDraft));
            RunSelectedSectionEntryChecks();
        }
    }

    [RelayCommand]
    private void NavigateToMultistream(PlatformLinkTarget? target) =>
        MultistreamRequested?.Invoke(this, target?.Platform);

    private void RememberSelection()
    {
        if (Sections.SelectedPath is { Length: > 0 } path
            && !string.Equals(path, _rememberedSection, StringComparison.Ordinal))
        {
            _rememberedSection = path;
            _uiSettings.SetValue(SettingsKeys.SettingsSection, path);
        }
    }

    private void RunSelectedSectionEntryChecks()
    {
        if (Sections.Selected is { } selected
            && _contributorsByKey.TryGetValue(selected.Key, out var contributor))
        {
            contributor.OnSectionShown();
        }
    }

    private bool CanReloadDraft() => !IsSaving && !_disposed;

    private bool CanSave() => CanReloadDraft() && HasChanges;

    [RelayCommand(CanExecute = nameof(CanSave))]
    private async Task SaveAsync()
    {
        if (!CanReloadDraft())
        {
            return;
        }

        IsSaving = true;
        try
        {
            await SaveDraftAsync();
        }
        finally
        {
            IsSaving = false;
        }
    }

    private async Task SaveDraftAsync()
    {
        foreach (var contributor in _contributors)
        {
            if (contributor.ValidateBeforeSave() is { } validationError)
            {
                _dialogService.Warning("Настройки", validationError);
                return;
            }
        }

        try
        {
            var prevPort = _settingsManager.Current.Twitch.HttpServerPort;

            foreach (var contributor in _contributors)
            {
                contributor.BeginSave(_session.Settings);
            }

            var newPort = _session.Settings.Twitch.HttpServerPort;
            var appSettingsWritten = _settingsManager.SaveSettings(_session.Settings);
            var written = appSettingsWritten;
            var portChanged = newPort != prevPort;
            List<string> failures = [];

            foreach (var contributor in _contributors)
            {
                var immediateEvents = await contributor.OnAppSettingsSavedAsync(appSettingsWritten);
                foreach (var @event in immediateEvents)
                {
                    await @event.PublishAsync(_eventBus);
                }
            }

            foreach (var contributor in _contributors)
            {
                var outcome = await contributor.SaveAsync(_session.Settings, appSettingsWritten);
                written &= outcome.Written;
                portChanged |= outcome.RequiresHttpServerRestart;
                if (outcome.FailureMessage is { } failure)
                {
                    failures.Add(failure);
                }

                foreach (var @event in outcome.Events)
                {
                    await @event.PublishAsync(_eventBus);
                }
            }

            foreach (var contributor in _contributors)
            {
                foreach (var @event in contributor.CollectPostSaveEvents(_session.Settings))
                {
                    await @event.PublishAsync(_eventBus);
                }
            }

            var restartFailed = false;
            if (portChanged && _kestrelHttpServer.IsRunning)
            {
                restartFailed = !await TryRestartHttpServerAsync(prevPort, newPort);
            }

            if (_disposed)
            {
                return;
            }

            if (failures.Count > 0)
            {
                Dirty = true;
                _dialogService.Warning("Настройки", string.Join(Environment.NewLine, failures));
                return;
            }

            ResetDirty();
            SettingsSaved?.Invoke(this, EventArgs.Empty);
            _logger.SettingsSavedByUser();

            if (restartFailed)
            {
                return;
            }

            var saved = !written || _settingsWriteGate.HasRevoked
                ? NotWrittenNotice
                : "Настройки успешно сохранены.";

            var info = portChanged
                ? $"{saved} HTTP сервер перезапущен на порту {newPort}."
                : saved;

            _dialogService.Info("Настройки", info);
        }
        catch (Exception exception)
        {
            _logger.SettingsSaveFailed(exception);
            _dialogService.Error("Ошибка", $"Ошибка сохранения настроек: {exception.Message}");
        }
    }

    [RelayCommand(CanExecute = nameof(CanReloadDraft))]
    private void Revert()
    {
        OnEnter();
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
}
