using CommunityToolkit.Mvvm.ComponentModel;

namespace PoproshaykaBot.Wpf.ViewModels.Settings;

public sealed partial class ObsSourceItemViewModel : ObservableObject
{
    [ObservableProperty]
    private bool _isChecked;

    [ObservableProperty]
    private bool _isMissingInObs;

    public ObsSourceItemViewModel(string name, bool isChecked, bool isMissingInObs = false)
    {
        Name = name;
        _isChecked = isChecked;
        _isMissingInObs = isMissingInObs;
    }

    public string Name { get; }
}
