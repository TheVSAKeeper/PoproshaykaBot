using CommunityToolkit.Mvvm.ComponentModel;

namespace PoproshaykaBot.Wpf.ViewModels.Settings;

public sealed partial class ObsSourceItemViewModel : ObservableObject
{
    [ObservableProperty]
    private bool _isChecked;

    public ObsSourceItemViewModel(string name, bool isChecked, bool isMissingInObs = false)
    {
        Name = name;
        _isChecked = isChecked;
        IsMissingInObs = isMissingInObs;
    }

    public string Name { get; }

    public bool IsMissingInObs { get; }
}
