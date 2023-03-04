using System.IO;
using System.IO.Abstractions;

namespace _42.Testing.System.IO.Abstractions.Wrappers;

public class FileSystemStreamWrapper : FileSystemStream
{
    public FileSystemStreamWrapper(FileStream stream)
        : this(stream, stream.Name, stream.IsAsync)
    {
        // no operation
    }

    public FileSystemStreamWrapper(Stream stream, string? path, bool isAsync)
        : base(stream, path, isAsync)
    {
        // no operation
    }
}
