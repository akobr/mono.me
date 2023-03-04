using Microsoft.Extensions.Logging;

namespace _42.Testing.System.IO.Abstractions.Tracers;

public class LoggingFileSystemTracer : BaseFileSystemTracer
{
    private readonly ILogger _logger;

    public LoggingFileSystemTracer(ILogger logger)
    {
        _logger = logger;
    }

    protected override void TraceInternal(string operationName, object?[]? args)
    {
        _logger.Log(LogLevel.Trace, operationName, args);
    }
}
