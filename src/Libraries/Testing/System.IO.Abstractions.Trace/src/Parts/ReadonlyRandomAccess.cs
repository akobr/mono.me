using System;
using System.Collections.Generic;
using System.IO.Abstractions;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Win32.SafeHandles;

namespace _42.Testing.System.IO.Abstractions.Parts;

// Reads go to the executing file system; writes, length changes and flushes are dropped, as the writes of ReadonlyFile are.
public class ReadonlyRandomAccess : IRandomAccess
{
    private readonly IRandomAccess _executingRandomAccess;

    public ReadonlyRandomAccess(IRandomAccess executingRandomAccess, IFileSystem fileSystem)
    {
        _executingRandomAccess = executingRandomAccess;
        FileSystem = fileSystem;
    }

    public IFileSystem FileSystem { get; }

    public void FlushToDisk(SafeFileHandle handle)
    {
        // no operation
    }

    public long GetLength(SafeFileHandle handle)
    {
        return _executingRandomAccess.GetLength(handle);
    }

    public int Read(SafeFileHandle handle, Span<byte> buffer, long fileOffset)
    {
        return _executingRandomAccess.Read(handle, buffer, fileOffset);
    }

    public long Read(SafeFileHandle handle, IReadOnlyList<Memory<byte>> buffers, long fileOffset)
    {
        return _executingRandomAccess.Read(handle, buffers, fileOffset);
    }

    public ValueTask<int> ReadAsync(
        SafeFileHandle handle,
        Memory<byte> buffer,
        long fileOffset,
        CancellationToken cancellationToken = default)
    {
        return _executingRandomAccess.ReadAsync(handle, buffer, fileOffset, cancellationToken);
    }

    public ValueTask<long> ReadAsync(
        SafeFileHandle handle,
        IReadOnlyList<Memory<byte>> buffers,
        long fileOffset,
        CancellationToken cancellationToken = default)
    {
        return _executingRandomAccess.ReadAsync(handle, buffers, fileOffset, cancellationToken);
    }

    public void SetLength(SafeFileHandle handle, long length)
    {
        // no operation
    }

    public void Write(SafeFileHandle handle, ReadOnlySpan<byte> buffer, long fileOffset)
    {
        // no operation
    }

    public void Write(SafeFileHandle handle, IReadOnlyList<ReadOnlyMemory<byte>> buffers, long fileOffset)
    {
        // no operation
    }

    public ValueTask WriteAsync(
        SafeFileHandle handle,
        ReadOnlyMemory<byte> buffer,
        long fileOffset,
        CancellationToken cancellationToken = default)
    {
        return ValueTask.CompletedTask;
    }

    public ValueTask WriteAsync(
        SafeFileHandle handle,
        IReadOnlyList<ReadOnlyMemory<byte>> buffers,
        long fileOffset,
        CancellationToken cancellationToken = default)
    {
        return ValueTask.CompletedTask;
    }
}
