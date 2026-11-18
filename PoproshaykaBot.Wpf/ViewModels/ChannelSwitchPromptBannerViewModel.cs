using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using PoproshaykaBot.Core.Infrastructure.Events;
using PoproshaykaBot.Core.Infrastructure.Events.Streaming;
using PoproshaykaBot.Core.Platforms;
using PoproshaykaBot.Core.Streaming.Switching;
using PoproshaykaBot.Wpf.Bootstrap;
using PoproshaykaBot.Wpf.Infrastructure;
using PoproshaykaBot.Wpf.Infrastructure.Platforms;

namespace PoproshaykaBot.Wpf.ViewModels;

public sealed record AutoDiscoverCandidateOption(ChannelKey Channel, string DisplayName, string Label);

public sealed partial class ChannelSwitchPromptBannerViewModel : ObservableObject, IDisposable
{
    private readonly IChannelSwitchCoordinator _switchCoordinator;
    private readonly ILogger<ChannelSwitchPromptBannerViewModel> _logger;
    private readonly Dictionary<StreamingPlatform, AutoDiscoverPrompt> _prompts = [];
    private readonly IDisposable _subscription;

    private AutoDiscoverPrompt? _activePrompt;
    private bool _suppressed;

    [ObservableProperty]
    private bool _isVisible;

    [ObservableProperty]
    private string _text = string.Empty;

    [ObservableProperty]
    private bool _hasMultipleCandidates;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ConfirmCommand))]
    private AutoDiscoverCandidateOption? _selectedCandidate;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ConfirmCommand))]
    [NotifyCanExecuteChangedFor(nameof(DismissCommand))]
    private bool _isBusy;

    public ChannelSwitchPromptBannerViewModel(
        IChannelSwitchCoordinator switchCoordinator,
        IEventBus eventBus,
        ILogger<ChannelSwitchPromptBannerViewModel> logger)
    {
        ArgumentNullException.ThrowIfNull(switchCoordinator);
        ArgumentNullException.ThrowIfNull(eventBus);
        ArgumentNullException.ThrowIfNull(logger);

        _switchCoordinator = switchCoordinator;
        _logger = logger;

        foreach (var prompt in _switchCoordinator.GetActivePrompts())
        {
            if (prompt.Candidates.Count > 0)
            {
                _prompts[prompt.Platform] = prompt;
            }
        }

        _subscription = eventBus.SubscribeOnUi<AutoDiscoverPromptChanged>(OnPromptChanged);
        Refresh();
    }

    public ObservableCollection<AutoDiscoverCandidateOption> Candidates { get; } = [];

    public event Action<string, StatusSeverity>? StatusReported;

    public void Suppress()
    {
        _suppressed = true;
        IsVisible = false;
    }

    [RelayCommand(CanExecute = nameof(CanConfirm))]
    private async Task ConfirmAsync()
    {
        if (_activePrompt is null || SelectedCandidate is null)
        {
            return;
        }

        var platform = _activePrompt.Platform;
        var candidate = SelectedCandidate;
        var platformName = PlatformVisuals.GetDisplayName(platform);
        IsBusy = true;

        try
        {
            var result = await _switchCoordinator.ConfirmAutoDiscoverCandidateAsync(platform, candidate.Channel).ConfigureAwait(true);

            if (result.Succeeded)
            {
                _logger.AutoDiscoverConfirmedByUser(platformName, candidate.DisplayName);
                StatusReported?.Invoke($"Активный чат {platformName} переключён на «{candidate.DisplayName}»", StatusSeverity.Info);
            }
            else
            {
                _logger.ChannelSwitchOperationFailed(null, platformName);
                var message = result.ErrorMessage ?? $"Не удалось переключить чат {platformName} на «{candidate.DisplayName}»";
                var severity = result.Outcome == ChannelSwitchOutcome.FailedNoActiveChat
                    ? StatusSeverity.Error
                    : StatusSeverity.Warning;
                StatusReported?.Invoke(message, severity);
            }
        }
        catch (Exception ex)
        {
            _logger.ChannelSwitchOperationFailed(ex, platformName);
            StatusReported?.Invoke($"Ошибка переключения канала {platformName}: {ex.Message}", StatusSeverity.Error);
        }
        finally
        {
            IsBusy = false;
            Refresh();
        }
    }

    private bool CanConfirm() => !IsBusy && _activePrompt is not null && SelectedCandidate is not null;

    [RelayCommand(CanExecute = nameof(CanDismiss))]
    private async Task DismissAsync()
    {
        if (_activePrompt is null)
        {
            return;
        }

        var platform = _activePrompt.Platform;
        var platformName = PlatformVisuals.GetDisplayName(platform);
        IsBusy = true;

        try
        {
            await _switchCoordinator.DismissAutoDiscoverPromptAsync(platform).ConfigureAwait(true);
            _logger.AutoDiscoverDismissedByUser(platformName);
        }
        catch (Exception ex)
        {
            _logger.ChannelSwitchOperationFailed(ex, platformName);
            StatusReported?.Invoke($"Ошибка отклонения автопоиска {platformName}: {ex.Message}", StatusSeverity.Warning);
        }
        finally
        {
            IsBusy = false;
            Refresh();
        }
    }

    private bool CanDismiss() => !IsBusy && _activePrompt is not null;

    private void OnPromptChanged(AutoDiscoverPromptChanged evt)
    {
        if (evt.Prompt is { Candidates.Count: > 0 } prompt)
        {
            _prompts[evt.Platform] = prompt;
        }
        else
        {
            _prompts.Remove(evt.Platform);
        }

        Refresh();
    }

    private void Refresh()
    {
        if (_suppressed)
        {
            _activePrompt = null;
            Candidates.Clear();
            SelectedCandidate = null;
            HasMultipleCandidates = false;
            IsVisible = false;
            return;
        }

        AutoDiscoverPrompt? selectedPrompt = null;
        if (_activePrompt is not null && _prompts.TryGetValue(_activePrompt.Platform, out var samePlatform))
        {
            selectedPrompt = samePlatform;
        }
        else if (_prompts.Count > 0)
        {
            selectedPrompt = _prompts.Values.OrderBy(static p => (int)p.Platform).First();
        }

        _activePrompt = selectedPrompt;
        Candidates.Clear();

        if (_activePrompt is null || _activePrompt.Candidates.Count == 0)
        {
            SelectedCandidate = null;
            HasMultipleCandidates = false;
            Text = string.Empty;
            IsVisible = false;
            return;
        }

        foreach (var candidate in _activePrompt.Candidates)
        {
            var label = string.IsNullOrWhiteSpace(candidate.StreamTitle)
                ? candidate.ChannelDisplayName
                : $"{candidate.ChannelDisplayName} – {candidate.StreamTitle}";
            Candidates.Add(new AutoDiscoverCandidateOption(candidate.Channel, candidate.ChannelDisplayName, label));
        }

        SelectedCandidate = Candidates[0];
        HasMultipleCandidates = Candidates.Count > 1;
        var platformName = PlatformVisuals.GetDisplayName(_activePrompt.Platform);

        if (Candidates.Count == 1)
        {
            var candidate = Candidates[0];
            var streamTitle = _activePrompt.Candidates[0].StreamTitle;
            var titleSuffix = string.IsNullOrWhiteSpace(streamTitle) ? string.Empty : $" («{streamTitle}»)";
            var currentSuffix = string.IsNullOrWhiteSpace(_activePrompt.CurrentActiveDisplayName)
                ? string.Empty
                : $" Текущий чат: «{_activePrompt.CurrentActiveDisplayName}».";
            Text = $"Обнаружена активная трансляция {platformName}: «{candidate.DisplayName}»{titleSuffix}.{currentSuffix} Переключить активный чат?";
        }
        else
        {
            var currentSuffix = string.IsNullOrWhiteSpace(_activePrompt.CurrentActiveDisplayName)
                ? string.Empty
                : $" Текущий чат: «{_activePrompt.CurrentActiveDisplayName}».";
            Text = $"Обнаружено активных трансляций {platformName}: {Candidates.Count}.{currentSuffix} Выберите канал для переключения активного чата:";
        }

        IsVisible = true;
        ConfirmCommand.NotifyCanExecuteChanged();
        DismissCommand.NotifyCanExecuteChanged();
    }

    public void Dispose()
    {
        _subscription.Dispose();
    }
}
