using System.Collections.ObjectModel;
using System.Collections.Specialized;
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

    public void TrackCollectionItems<T>(ObservableCollection<T> collection, PropertyChangedEventHandler itemHandler)
        where T : class, INotifyPropertyChanged
    {
        ArgumentNullException.ThrowIfNull(collection);
        ArgumentNullException.ThrowIfNull(itemHandler);

        Dictionary<T, int> refCounts = new(ReferenceEqualityComparer.Instance);

        void Subscribe(T item)
        {
            if (refCounts.TryGetValue(item, out var count))
            {
                refCounts[item] = count + 1;
                return;
            }

            refCounts[item] = 1;
            item.PropertyChanged += itemHandler;
        }

        void Unsubscribe(T item)
        {
            if (!refCounts.TryGetValue(item, out var count))
            {
                return;
            }

            if (count > 1)
            {
                refCounts[item] = count - 1;
                return;
            }

            refCounts.Remove(item);
            item.PropertyChanged -= itemHandler;
        }

        void ClearTracked()
        {
            foreach (var item in refCounts.Keys)
            {
                item.PropertyChanged -= itemHandler;
            }

            refCounts.Clear();
        }

        foreach (var item in collection)
        {
            Subscribe(item);
        }

        void OnCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
        {
            switch (e.Action)
            {
                case NotifyCollectionChangedAction.Reset:
                    ClearTracked();
                    foreach (var item in collection)
                    {
                        Subscribe(item);
                    }
                    break;

                case NotifyCollectionChangedAction.Add:
                    if (e.NewItems is not null)
                    {
                        foreach (T item in e.NewItems)
                        {
                            Subscribe(item);
                        }
                    }
                    break;

                case NotifyCollectionChangedAction.Remove:
                    if (e.OldItems is not null)
                    {
                        foreach (T item in e.OldItems)
                        {
                            Unsubscribe(item);
                        }
                    }
                    break;

                case NotifyCollectionChangedAction.Replace:
                    if (e.OldItems is not null)
                    {
                        foreach (T item in e.OldItems)
                        {
                            Unsubscribe(item);
                        }
                    }
                    if (e.NewItems is not null)
                    {
                        foreach (T item in e.NewItems)
                        {
                            Subscribe(item);
                        }
                    }
                    break;
            }
        }

        collection.CollectionChanged += OnCollectionChanged;
        _unsubscribeActions.Add(() =>
        {
            collection.CollectionChanged -= OnCollectionChanged;
            ClearTracked();
        });
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
