using System;
using System.Collections.Generic;
using System.IO.Abstractions;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Win32.SafeHandles;

namespace _42.Testing.System.IO.Abstractions.Parts;

public class DiagnosticRandomAccess : IRandomAccess
{
    private readonly IRandomAccess _executingRandomAccess;
    private readonly IFileSystemTracer _processor;

    public DiagnosticRandomAccess(
        IRandomAccess executingRandomAccess,
        IFileSystem diagnosticSystem,
        IFileSystemTracer processor)
    {
        _executingRandomAccess = executingRandomAccess;
        _processor = processor;
        FileSystem = diagnosticSystem;
    }

    public IFileSystem FileSystem { get; }

    public void FlushToDisk(SafeFileHandle handle)
    {
        _processor.Process(new object?[] { handle });
        _executingRandomAccess.FlushToDisk(handle);
    }

    public long GetLength(SafeFileHandle handle)
    {
        _processor.Process(new object?[] { handle });
        return _executingRandomAccess.GetLength(handle);
    }

    public int Read(SafeFileHandle handle, Span<byte> buffer, long fileOffset)
    {
        // a span cannot be boxed, so its length is traced instead
        _processor.Process(new object?[] { handle, buffer.Length, fileOffset });
        return _executingRandomAccess.Read(handle, buffer, fileOffset);
    }

    public long Read(SafeFileHandle handle, IReadOnlyList<Memory<byte>> buffers, long fileOffset)
    {
        _processor.Process(new object?[] { handle, buffers, fileOffset });
        return _executingRandomAccess.Read(handle, buffers, fileOffset);
    }

    public ValueTask<int> ReadAsync(
        SafeFileHandle handle,
        Memory<byte> buffer,
        long fileOffset,
        CancellationToken cancellationToken = default)
    {
        _processor.Process(new object?[] { handle, buffer, fileOffset });
        return _executingRandomAccess.ReadAsync(handle, buffer, fileOffset, cancellationToken);
    }

    public ValueTask<long> ReadAsync(
        SafeFileHandle handle,
        IReadOnlyList<Memory<byte>> buffers,
        long fileOffset,
        CancellationToken cancellationToken = default)
    {
        _processor.Process(new object?[] { handle, buffers, fileOffset });
        return _executingRandomAccess.ReadAsync(handle, buffers, fileOffset, cancellationToken);
    }

    public void SetLength(SafeFileHandle handle, long length)
    {
        _processor.Process(new object?[] { handle, length });
        _executingRandomAccess.SetLength(handle, length);
    }

    public void Write(SafeFileHandle handle, ReadOnlySpan<byte> buffer, long fileOffset)
    {
        // a span cannot be boxed, so its content is traced as an array, as DiagnosticFile does
        _processor.Process(new object?[] { handle, buffer.ToArray(), fileOffset });
        _executingRandomAccess.Write(handle, buffer, fileOffset);
    }

    public void Write(SafeFileHandle handle, IReadOnlyList<ReadOnlyMemory<byte>> buffers, long fileOffset)
    {
        _processor.Process(new object?[] { handle, buffers, fileOffset });
        _executingRandomAccess.Write(handle, buffers, fileOffset);
    }

    public ValueTask WriteAsync(
        SafeFileHandle handle,
        ReadOnlyMemory<byte> buffer,
        long fileOffset,
        CancellationToken cancellationToken = default)
    {
        _processor.Process(new object?[] { handle, buffer, fileOffset });
        return _executingRandomAccess.WriteAsync(handle, buffer, fileOffset, cancellationToken);
    }

    public ValueTask WriteAsync(
        SafeFileHandle handle,
        IReadOnlyList<ReadOnlyMemory<byte>> buffers,
        long fileOffset,
        CancellationToken cancellationToken = default)
    {
        _processor.Process(new object?[] { handle, buffers, fileOffset });
        return _executingRandomAccess.WriteAsync(handle, buffers, fileOffset, cancellationToken);
    }
}
