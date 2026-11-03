using _42.Testing.System.IO.Abstractions;

namespace _42.Platform.Storyteller.Sdk.Configuring.UnitTests;

// Records the operations of a DiagnosticFileSystem, and fails the ones a test picks, before they run.
internal sealed class FileSystemTracer : IFileSystemTracer
{
    private readonly List<(string Operation, object?[] Arguments)> _operations = [];
    private readonly Lock _lock = new();

    public Func<string, object?[], Exception?>? Fail { get; set; }

    public IReadOnlyList<(string Operation, object?[] Arguments)> Operations
    {
        get
        {
            lock (_lock)
            {
                return [.. _operations];
            }
        }
    }

    public void Process(object?[]? args = null, string operationName = "")
    {
        var arguments = args ?? [];

        lock (_lock)
        {
            _operations.Add((operationName, arguments));
        }

        if (Fail?.Invoke(operationName, arguments) is { } exception)
        {
            throw exception;
        }
    }
}
