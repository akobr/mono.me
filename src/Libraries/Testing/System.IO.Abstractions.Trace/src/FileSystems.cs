using System.IO.Abstractions;
using _42.Testing.System.IO.Abstractions.Tracers;
using Microsoft.Extensions.Logging;

namespace _42.Testing.System.IO.Abstractions;

public static class FileSystems
{
    public static IFileSystem CreateLoggerAroundOsReadonlySystem(ILogger logger)
    {
        return new DiagnosticFileSystem(
            new ReadonlyFileSystem(new FileSystem()),
            new LoggingFileSystemTracer(logger));
    }

    public static IFileSystem CreateLoggerAroundOsSystem(ILogger logger)
    {
        return new DiagnosticFileSystem(new FileSystem(), new LoggingFileSystemTracer(logger));
    }

    public static IFileSystem CreateConsoleTracerAroundOsSystem()
    {
        return new DiagnosticFileSystem(new FileSystem(), new ConsoleFileSystemTracer());
    }
}
