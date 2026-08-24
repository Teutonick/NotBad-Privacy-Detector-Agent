using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;

namespace PrivacyAudit.Core;

public sealed class ObservableRangeCollection<T> : ObservableCollection<T>
{
    public void ReplaceRange(IEnumerable<T> items)
    {
        Items.Clear();
        foreach (var item in items) Items.Add(item);
        NotifyReset();
    }

    public void AddRange(IEnumerable<T> items)
    {
        foreach (var item in items) Items.Add(item);
        NotifyReset();
    }

    public void InsertRange(int index, IEnumerable<T> items)
    {
        foreach (var item in items.Reverse()) Items.Insert(index, item);
        NotifyReset();
    }

    public void RemoveRange(int index, int count)
    {
        for (var i = 0; i < count && index < Items.Count; i++) Items.RemoveAt(index);
        NotifyReset();
    }

    void NotifyReset()
    {
        OnPropertyChanged(new PropertyChangedEventArgs(nameof(Count)));
        OnPropertyChanged(new PropertyChangedEventArgs("Item[]"));
        OnCollectionChanged(new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Reset));
    }
}
