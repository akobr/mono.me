using System.Runtime.CompilerServices;

namespace _42.Testing.System.IO.Abstractions;

public interface IFileSystemTracer
{
    void Process(object?[]? args = null, [CallerMemberName]string operationName = "");
}
