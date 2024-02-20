using System;

namespace _42.Platform.Cli.Output.Exceptions;

public class OutputException : Exception
{
    public OutputException(int exitCode)
    {
        ExitCode = exitCode;
    }

    public OutputException(int exitCode, string message)
        : base(message)
    {
        ExitCode = exitCode;
    }

    public OutputException(int exitCode, string message, Exception inner)
        : base(message, inner)
    {
        ExitCode = exitCode;
    }

    public int ExitCode { get; }
}
