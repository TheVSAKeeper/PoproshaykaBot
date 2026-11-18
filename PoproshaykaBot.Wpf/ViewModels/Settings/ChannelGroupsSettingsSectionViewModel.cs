using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PoproshaykaBot.Core.Platforms;
using PoproshaykaBot.Core.Settings;
using PoproshaykaBot.Core.Settings.Stores;
using PoproshaykaBot.Core.Statistics;
using PoproshaykaBot.Wpf.Infrastructure.Platforms;
using System.Collections.ObjectModel;

namespace PoproshaykaBot.Wpf.ViewModels.Settings;

public sealed partial class ChannelGroupsSettingsSectionViewModel : ObservableObject
{
    private readonly ChannelCatalogStore _catalogStore;
    private readonly IUserStatisticsRepository _statisticsRepository;
    private readonly BroadcastHistoryStore? _broadcastHistory;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSelectedGroup))]
    [NotifyCanExecuteChangedFor(nameof(RemoveSelectedGroupCommand))]
    private ChannelGroupDraftViewModel? _selectedGroup;

    [ObservableProperty]
    private string _newGroupName = string.Empty;

    public ChannelGroupsSettingsSectionViewModel(
        ChannelCatalogStore catalogStore,
        IUserStatisticsRepository statisticsRepository,
        BroadcastHistoryStore? broadcastHistory = null)
    {
        ArgumentNullException.ThrowIfNull(catalogStore);
        ArgumentNullException.ThrowIfNull(statisticsRepository);

        _catalogStore = catalogStore;
        _statisticsRepository = statisticsRepository;
        _broadcastHistory = broadcastHistory;
    }

    public event EventHandler? Edited;

    public ObservableCollection<ChannelGroupDraftViewModel> Groups { get; } = [];

    public bool HasGroups => Groups.Count > 0;

    public bool HasSelectedGroup => SelectedGroup is not null;

    public void Load()
    {
        var settings = _catalogStore.Load();
        var knownChannels = BuildKnownChannels(settings);

        Groups.Clear();
        foreach (var group in settings.Groups)
        {
            var members = ChannelCatalogStore.DeduplicateMembers(group.Channels);
            var draft = new ChannelGroupDraftViewModel(
                group.Id,
                group.Name,
                knownChannels,
                members,
                NotifyEdited);
            Groups.Add(draft);
        }

        SelectedGroup = Groups.FirstOrDefault();
        OnPropertyChanged(nameof(HasGroups));
    }

    public ChannelsSettings BuildSettings()
    {
        var current = _catalogStore.Load();
        ApplyTo(current);
        return current;
    }

    public void ApplyTo(ChannelsSettings target)
    {
        ArgumentNullException.ThrowIfNull(target);

        var groups = new List<ChannelGroupDefinition>(Groups.Count);

        foreach (var group in Groups)
        {
            if (string.IsNullOrWhiteSpace(group.Name))
            {
                continue;
            }

            groups.Add(new()
            {
                Id = group.Id,
                Name = group.Name.Trim(),
                Channels = group.Channels
                    .Where(static c => c.IsSelected)
                    .Select(static c => new ChannelGroupMember
                    {
                        Platform = c.Key.Platform,
                        ChannelId = c.Key.ChannelId,
                    })
                    .ToList(),
            });
        }

        target.Groups = groups;
    }

    public bool HasErrors => Groups.Any(static g => string.IsNullOrWhiteSpace(g.Name));

    [RelayCommand]
    private void AddGroup()
    {
        var baseName = string.IsNullOrWhiteSpace(NewGroupName)
            ? $"Группа {Groups.Count + 1}"
            : NewGroupName.Trim();

        var knownChannels = BuildKnownChannels(_catalogStore.Load());
        var draft = new ChannelGroupDraftViewModel(
            Guid.NewGuid().ToString("N"),
            baseName,
            knownChannels,
            new HashSet<ChannelKey>(),
            NotifyEdited);

        Groups.Add(draft);
        SelectedGroup = draft;
        NewGroupName = string.Empty;
        OnPropertyChanged(nameof(HasGroups));
        NotifyEdited();
    }

    [RelayCommand(CanExecute = nameof(HasSelectedGroup))]
    private void RemoveSelectedGroup()
    {
        if (SelectedGroup is null)
        {
            return;
        }

        Groups.Remove(SelectedGroup);
        SelectedGroup = Groups.FirstOrDefault();
        OnPropertyChanged(nameof(HasGroups));
        NotifyEdited();
    }

    private void NotifyEdited() => Edited?.Invoke(this, EventArgs.Empty);

    private List<ChannelCatalogChoice> BuildKnownChannels(ChannelsSettings settings)
    {
        var map = new Dictionary<ChannelKey, string>();

        foreach (var entry in settings.Channels)
        {
            if (!Enum.IsDefined(entry.Platform) || string.IsNullOrWhiteSpace(entry.ChannelId))
            {
                continue;
            }

            var key = new ChannelKey(entry.Platform, entry.ChannelId.Trim());
            map[key] = string.IsNullOrWhiteSpace(entry.DisplayName) ? key.ChannelId : entry.DisplayName.Trim();
        }

        foreach (var key in _statisticsRepository.GetKnownChannels())
        {
            map.TryAdd(key, key.ChannelId);
        }

        if (_broadcastHistory is not null)
        {
            foreach (var broadcast in _broadcastHistory.Load().Broadcasts)
            {
                foreach (var platform in broadcast.Platforms)
                {
                    if (!Enum.IsDefined(platform.Platform) || string.IsNullOrWhiteSpace(platform.ChannelId))
                    {
                        continue;
                    }

                    var key = new ChannelKey(platform.Platform, platform.ChannelId.Trim());
                    var label = string.IsNullOrWhiteSpace(platform.Channel) ? key.ChannelId : platform.Channel.Trim();
                    map.TryAdd(key, label);
                }
            }
        }

        foreach (var group in settings.Groups)
        {
            foreach (var member in group.Channels)
            {
                if (Enum.IsDefined(member.Platform) && !string.IsNullOrWhiteSpace(member.ChannelId))
                {
                    var key = new ChannelKey(member.Platform, member.ChannelId.Trim());
                    map.TryAdd(key, key.ChannelId);
                }
            }
        }

        return
        [
            .. map
                .OrderBy(static pair => pair.Key.Platform)
                .ThenBy(static pair => pair.Value, StringComparer.OrdinalIgnoreCase)
                .Select(static pair => new ChannelCatalogChoice(
                    pair.Key,
                    $"{PlatformVisuals.GetDisplayName(pair.Key.Platform)} · {pair.Value}")),
        ];
    }
}

public sealed record ChannelCatalogChoice(ChannelKey Key, string DisplayLabel);

public sealed partial class ChannelGroupDraftViewModel : ObservableObject
{
    private readonly Action _onEdited;

    [ObservableProperty]
    private string _name;

    public ChannelGroupDraftViewModel(
        string id,
        string name,
        IReadOnlyList<ChannelCatalogChoice> knownChannels,
        IReadOnlySet<ChannelKey> selectedKeys,
        Action onEdited)
    {
        Id = id;
        _name = name;
        _onEdited = onEdited;
        Channels =
        [
            .. knownChannels.Select(c => new ChannelGroupMemberChoiceViewModel(
                c.Key,
                c.DisplayLabel,
                selectedKeys.Contains(c.Key),
                onEdited)),
        ];
    }

    public string Id { get; }

    public ObservableCollection<ChannelGroupMemberChoiceViewModel> Channels { get; }

    public bool HasKnownChannels => Channels.Count > 0;

    partial void OnNameChanged(string value) => _onEdited();
}

public sealed partial class ChannelGroupMemberChoiceViewModel : ObservableObject
{
    private readonly Action _onEdited;

    [ObservableProperty]
    private bool _isSelected;

    public ChannelGroupMemberChoiceViewModel(
        ChannelKey key,
        string label,
        bool isSelected,
        Action onEdited)
    {
        Key = key;
        Label = label;
        _isSelected = isSelected;
        _onEdited = onEdited;
    }

    public ChannelKey Key { get; }
    public string Label { get; }

    partial void OnIsSelectedChanged(bool value) => _onEdited();
}
