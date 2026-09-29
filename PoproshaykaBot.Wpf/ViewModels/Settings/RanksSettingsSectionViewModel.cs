using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PoproshaykaBot.Core.Users;
using System.Collections.ObjectModel;
using System.ComponentModel;

namespace PoproshaykaBot.Wpf.ViewModels.Settings;

public sealed partial class RanksSettingsSectionViewModel : ObservableObject, IDisposable
{
    private const ulong NewRankStep = 1000;

    private static readonly HashSet<string> EditableProperties = new(StringComparer.Ordinal)
    {
        nameof(RankEntryViewModel.Emoji),
        nameof(RankEntryViewModel.Name),
        nameof(RankEntryViewModel.Tier),
        nameof(RankEntryViewModel.ThresholdText),
    };

    private bool _loading;

    public event EventHandler? Edited;

    public ObservableCollection<RankEntryViewModel> Entries { get; } = [];

    public bool HasErrors => Entries.Any(entry => entry.HasError);

    public void Load(IEnumerable<UserRank> ranks)
    {
        _loading = true;

        try
        {
            Replace(ranks);
        }
        finally
        {
            _loading = false;
        }
    }

    public List<UserRank> BuildRanks()
    {
        return [.. Entries.Select(entry => entry.ToRank()).OrderByDescending(rank => rank.MinMessages)];
    }

    public void Dispose()
    {
        foreach (var entry in Entries)
        {
            entry.PropertyChanged -= OnEntryPropertyChanged;
        }
    }

    [RelayCommand]
    private void Add()
    {
        ulong top = 0;
        var hasTop = false;

        foreach (var entry in Entries)
        {
            if (RankEntryViewModel.ParseThreshold(entry.ThresholdText, out var threshold) is null && (!hasTop || threshold > top))
            {
                top = threshold;
                hasTop = true;
            }
        }

        var added = new RankEntryViewModel(new("★", "НОВЫЙ РАНГ", hasTop ? top + NewRankStep : 0));
        added.PropertyChanged += OnEntryPropertyChanged;
        Entries.Insert(0, added);
        OnStructureChanged();
    }

    [RelayCommand(CanExecute = nameof(CanRemove))]
    private void Remove(RankEntryViewModel? entry)
    {
        if (entry is null || !Entries.Remove(entry))
        {
            return;
        }

        entry.PropertyChanged -= OnEntryPropertyChanged;
        OnStructureChanged();
    }

    private bool CanRemove(RankEntryViewModel? entry)
    {
        return entry is not null && Entries.Count > 1;
    }

    [RelayCommand]
    private void ResetToDefaults()
    {
        Replace(new RanksSettings().Ranks);
        Edited?.Invoke(this, EventArgs.Empty);
    }

    private void Replace(IEnumerable<UserRank> ranks)
    {
        foreach (var entry in Entries)
        {
            entry.PropertyChanged -= OnEntryPropertyChanged;
        }

        Entries.Clear();

        foreach (var rank in ranks.OrderByDescending(rank => rank.MinMessages))
        {
            var entry = new RankEntryViewModel(rank);
            entry.PropertyChanged += OnEntryPropertyChanged;
            Entries.Add(entry);
        }

        Validate();
        RemoveCommand.NotifyCanExecuteChanged();
    }

    private void OnStructureChanged()
    {
        Validate();
        RemoveCommand.NotifyCanExecuteChanged();
        Edited?.Invoke(this, EventArgs.Empty);
    }

    private void OnEntryPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is not { } name || !EditableProperties.Contains(name))
        {
            return;
        }

        Validate();

        if (!_loading)
        {
            Edited?.Invoke(this, EventArgs.Empty);
        }
    }

    private void Validate()
    {
        var owners = new Dictionary<ulong, RankEntryViewModel>();
        var thresholds = new Dictionary<RankEntryViewModel, ulong>();

        foreach (var entry in Entries)
        {
            entry.EmojiError = UserRankRules.DescribeEmojiError(entry.Emoji);
            entry.NameError = UserRankRules.DescribeNameError(entry.Name);
            entry.ThresholdError = RankEntryViewModel.ParseThreshold(entry.ThresholdText, out var threshold);

            if (entry.ThresholdError is null)
            {
                thresholds[entry] = threshold;
                owners.TryAdd(threshold, entry);
            }
        }

        foreach (var (entry, threshold) in thresholds)
        {
            var owner = owners[threshold];

            if (!ReferenceEquals(owner, entry))
            {
                entry.ThresholdError = $"Такой порог уже у «{owner.DisplayName}»";
            }
        }

        OnPropertyChanged(nameof(HasErrors));
    }
}
