using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PoproshaykaBot.Core.Platforms;
using PoproshaykaBot.Core.Statistics;
using PoproshaykaBot.Wpf.Infrastructure.Platforms;
using System.Collections.ObjectModel;

namespace PoproshaykaBot.Wpf.ViewModels.Dialogs;

public sealed partial class LinkAccountDialogViewModel : ObservableObject, IDialogViewModel, IAcceptableDialog
{
    private readonly IReadOnlyList<LinkableAccountCandidate> _allCandidates;

    [ObservableProperty]
    private string _filterText = string.Empty;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(AcceptCommand))]
    private LinkableAccountCandidate? _selectedCandidate;

    public LinkAccountDialogViewModel(
        string primaryDisplayName,
        IReadOnlyList<ViewerAccountRecord> knownAccounts,
        IReadOnlySet<PlatformUserKey> excludedKeys)
    {
        Title = $"Привязать аккаунт к {primaryDisplayName}";

        _allCandidates = knownAccounts
            .Where(account => !excludedKeys.Contains(account.Key))
            .OrderByDescending(static a => a.LastSeen)
            .ThenBy(static a => a.Name, StringComparer.OrdinalIgnoreCase)
            .Select(static a => new LinkableAccountCandidate(
                a.Key,
                PlatformVisuals.GetDisplayName(a.Platform),
                string.IsNullOrWhiteSpace(a.Name) ? a.UserId : a.Name))
            .ToList();

        ApplyFilter();
    }

    public event EventHandler<bool>? RequestClose;

    public string Title { get; }

    public ObservableCollection<LinkableAccountCandidate> FilteredCandidates { get; } = [];

    public bool HasCandidates => FilteredCandidates.Count > 0;

    public PlatformUserKey? Result => SelectedCandidate?.Key;

    partial void OnFilterTextChanged(string value) => ApplyFilter();

    public bool TryAccept()
    {
        if (SelectedCandidate is null)
        {
            return false;
        }

        RequestClose?.Invoke(this, true);
        return true;
    }

    [RelayCommand(CanExecute = nameof(CanAccept))]
    private void Accept() => TryAccept();

    private bool CanAccept() => SelectedCandidate is not null;

    [RelayCommand]
    private void Cancel() => RequestClose?.Invoke(this, false);

    private void ApplyFilter()
    {
        var query = FilterText.Trim();
        FilteredCandidates.Clear();

        foreach (var item in _allCandidates)
        {
            if (query.Length == 0
                || item.Name.Contains(query, StringComparison.OrdinalIgnoreCase)
                || item.Key.UserId.Contains(query, StringComparison.OrdinalIgnoreCase)
                || item.PlatformLabel.Contains(query, StringComparison.OrdinalIgnoreCase))
            {
                FilteredCandidates.Add(item);
            }
        }

        if (SelectedCandidate is null || !FilteredCandidates.Contains(SelectedCandidate))
        {
            SelectedCandidate = FilteredCandidates.FirstOrDefault();
        }

        OnPropertyChanged(nameof(HasCandidates));
    }
}

public sealed record LinkableAccountCandidate(
    PlatformUserKey Key,
    string PlatformLabel,
    string Name)
{
    public StreamingPlatform Platform => Key.Platform;
    public string UserId => Key.UserId;
}
