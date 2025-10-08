using System.Collections;

namespace _42.Crumble;

public class ObservableDictionary<TKey, TValue> : IDictionary<TKey, TValue>
{
    private readonly Dictionary<TKey, TValue> _dict;

    public event Action<TKey, TValue>? ItemAdded;
    public event Action<TKey, TValue>? ItemRemoved;
    public event Action<TKey, TValue, TValue>? ItemUpdated;

    public ObservableDictionary(IEqualityComparer<TKey> comparer)
    {
        _dict = new Dictionary<TKey, TValue>(comparer);
    }

    public ObservableDictionary(IEnumerable<KeyValuePair<TKey, TValue>> collection, IEqualityComparer<TKey> comparer)
    {
        _dict = new Dictionary<TKey, TValue>(collection, comparer);
    }

    public TValue this[TKey key]
    {
        get => _dict[key];
        set
        {
            if (_dict.TryGetValue(key, out var oldValue))
            {
                _dict[key] = value;
                ItemUpdated?.Invoke(key, oldValue, value);
            }
            else
            {
                _dict[key] = value;
                ItemAdded?.Invoke(key, value);
            }
        }
    }

    public ICollection<TKey> Keys => _dict.Keys;
    public ICollection<TValue> Values => _dict.Values;
    public int Count => _dict.Count;
    public bool IsReadOnly => false;

    public void Add(TKey key, TValue value)
    {
        _dict.Add(key, value);
        ItemAdded?.Invoke(key, value);
    }

    public bool Remove(TKey key)
    {
        if (_dict.TryGetValue(key, out var value) && _dict.Remove(key))
        {
            ItemRemoved?.Invoke(key, value);
            return true;
        }
        return false;
    }

    public bool ContainsKey(TKey key) => _dict.ContainsKey(key);

    public bool TryGetValue(TKey key, out TValue value) => _dict.TryGetValue(key, out value);

    public void Clear()
    {
        foreach (var kvp in _dict)
            ItemRemoved?.Invoke(kvp.Key, kvp.Value);
        _dict.Clear();
    }

    public void ClearWithoutObserve() => _dict.Clear();

    public void Add(KeyValuePair<TKey, TValue> item) => Add(item.Key, item.Value);

    public bool Contains(KeyValuePair<TKey, TValue> item) => ((IDictionary<TKey,TValue>)_dict).Contains(item);

    public void CopyTo(KeyValuePair<TKey, TValue>[] array, int arrayIndex) => ((IDictionary<TKey,TValue>)_dict).CopyTo(array, arrayIndex);

    public bool Remove(KeyValuePair<TKey, TValue> item) => Remove(item.Key);

    public IEnumerator<KeyValuePair<TKey, TValue>> GetEnumerator() => _dict.GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => _dict.GetEnumerator();
}
