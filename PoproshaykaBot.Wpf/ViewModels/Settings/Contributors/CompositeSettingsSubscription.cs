using System.ComponentModel;

namespace PoproshaykaBot.Wpf.ViewModels.Settings.Contributors;

internal sealed class CompositeSettingsSubscription : IDisposable
{
    private readonly List<Action> _unsubscribeActions = [];

    public void Add(Action unsubscribe)
    {
        _unsubscribeActions.Add(unsubscribe);
    }

    public void TrackPropertyChanged(INotifyPropertyChanged source, PropertyChangedEventHandler handler)
    {
        source.PropertyChanged += handler;
        _unsubscribeActions.Add(() => source.PropertyChanged -= handler);
    }

    public void Dispose()
    {
        for (var i = _unsubscribeActions.Count - 1; i >= 0; i--)
        {
            _unsubscribeActions[i]();
        }

        _unsubscribeActions.Clear();
    }
}
