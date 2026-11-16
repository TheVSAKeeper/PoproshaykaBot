using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PoproshaykaBot.Core.Settings.Chat;

namespace PoproshaykaBot.Wpf.ViewModels.Settings;

public sealed partial class ChatSettingsSectionViewModel : ObservableObject, IDisposable
{
    private const int HistoryMaxItemsStep = 100;

    [ObservableProperty]
    private bool _showHistoryInApp;

    [ObservableProperty]
    private bool _showHistoryInObs;

    [ObservableProperty]
    private int _historyMaxItems = ChatSettings.DefaultHistoryMaxItems;

    public void LoadSettings(ChatSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);

        ShowHistoryInApp = settings.ShowHistoryInApp;
        ShowHistoryInObs = settings.ShowHistoryInObs;
        HistoryMaxItems = ChatSettings.ClampHistoryMaxItems(settings.HistoryMaxItems);
    }

    public void SaveSettings(ChatSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);

        settings.ShowHistoryInApp = ShowHistoryInApp;
        settings.ShowHistoryInObs = ShowHistoryInObs;
        settings.HistoryMaxItems = ChatSettings.ClampHistoryMaxItems(HistoryMaxItems);
        HistoryMaxItems = settings.HistoryMaxItems;
    }

    [RelayCommand]
    private void IncreaseHistoryMaxItems()
    {
        HistoryMaxItems = ChatSettings.ClampHistoryMaxItems(HistoryMaxItems + HistoryMaxItemsStep);
    }

    [RelayCommand]
    private void DecreaseHistoryMaxItems()
    {
        HistoryMaxItems = ChatSettings.ClampHistoryMaxItems(HistoryMaxItems - HistoryMaxItemsStep);
    }

    public void Dispose()
    {
    }
}
