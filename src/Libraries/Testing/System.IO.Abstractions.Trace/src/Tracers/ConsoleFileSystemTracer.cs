using System;

namespace _42.Testing.System.IO.Abstractions.Tracers;

public class ConsoleFileSystemTracer : BaseFileSystemTracer
{
    protected override void TraceInternal(string operationName, object?[]? args)
    {
        Console.Write(operationName);

        if (args is { Length: > 0 })
        {
            Console.Write("; ");
            Console.Write(string.Join("; ", args));
        }

        Console.WriteLine();
    }
}
