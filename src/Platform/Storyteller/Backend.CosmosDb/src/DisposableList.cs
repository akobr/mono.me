using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace _42.Platform.Storyteller;

public interface IDisposableList
{
    void Add(IDisposable item);

    void AddAsync(IAsyncDisposable item);
}

public sealed class DisposableList : IAsyncDisposable, IDisposable, IDisposableList
{
    private readonly List<IDisposable> _syncItems = [];
    private readonly List<IAsyncDisposable> _asyncItems = [];

    public void Add(IDisposable item)
    {
        ArgumentNullException.ThrowIfNull(item);
        _syncItems.Add(item);
    }

    public void AddAsync(IAsyncDisposable item)
    {
        ArgumentNullException.ThrowIfNull(item);
        _asyncItems.Add(item);
    }

    public void Dispose()
    {
        foreach (var item in _syncItems)
        {
            item.Dispose();
        }

        foreach (var item in _asyncItems)
        {
            item.DisposeAsync().AsTask().GetAwaiter().GetResult();
        }

        _syncItems.Clear();
        _asyncItems.Clear();
    }

    public async ValueTask DisposeAsync()
    {
        foreach (var item in _syncItems)
        {
            item.Dispose();
        }

        foreach (var item in _asyncItems)
        {
            await item.DisposeAsync();
        }

        _syncItems.Clear();
        _asyncItems.Clear();
    }
}
