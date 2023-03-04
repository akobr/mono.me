using System.IO;
using System.IO.Abstractions;

namespace _42.Testing.System.IO.Abstractions.Wrappers;

public class InMemoryStream : FileSystemStream
{
    public InMemoryStream(string? path = null)
        : base(new MemoryStream(), path, false)
    {
        // no operation
    }
}
