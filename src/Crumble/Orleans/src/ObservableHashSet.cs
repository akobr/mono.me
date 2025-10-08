using System.Collections;

namespace _42.Crumble;

public class ObservableHashSet<T> : ISet<T>
{
    private readonly HashSet<T> _set;

    public event Action<T>? ItemAdded;
    public event Action<T>? ItemRemoved;

    public ObservableHashSet(IEqualityComparer<T> comparer)
    {
        _set = new HashSet<T>(comparer);
    }

    public ObservableHashSet(IEnumerable<T> collection, IEqualityComparer<T> comparer)
    {
        _set = new HashSet<T>(collection, comparer);
    }

    public bool Add(T item)
    {
        if (_set.Add(item))
        {
            ItemAdded?.Invoke(item);
            return true;
        }

        return false;
    }

    void ICollection<T>.Add(T item) => Add(item);

    public bool Remove(T item)
    {
        if (_set.Remove(item))
        {
            ItemRemoved?.Invoke(item);
            return true;
        }

        return false;
    }

    public int Count => _set.Count;

    public bool IsReadOnly => ((ICollection<T>)_set).IsReadOnly;

    public void Clear() { foreach (var i in _set) ItemRemoved?.Invoke(i); _set.Clear(); }

    public void ClearWithoutObserve() => _set.Clear();

    public bool Contains(T item) => _set.Contains(item);

    public void CopyTo(T[] array, int arrayIndex) => _set.CopyTo(array, arrayIndex);

    public IEnumerator<T> GetEnumerator() => _set.GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => _set.GetEnumerator();

    public void UnionWith(IEnumerable<T> other) => _set.UnionWith(other);

    public void IntersectWith(IEnumerable<T> other) => _set.IntersectWith(other);

    public void ExceptWith(IEnumerable<T> other) => _set.ExceptWith(other);

    public void SymmetricExceptWith(IEnumerable<T> other) => _set.SymmetricExceptWith(other);

    public bool IsSubsetOf(IEnumerable<T> other) => _set.IsSubsetOf(other);

    public bool IsSupersetOf(IEnumerable<T> other) => _set.IsSupersetOf(other);

    public bool IsProperSupersetOf(IEnumerable<T> other) => _set.IsProperSupersetOf(other);

    public bool IsProperSubsetOf(IEnumerable<T> other) => _set.IsProperSubsetOf(other);

    public bool Overlaps(IEnumerable<T> other) => _set.Overlaps(other);

    public bool SetEquals(IEnumerable<T> other) => _set.SetEquals(other);
}
