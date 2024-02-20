using System;

namespace _42.Platform.Cli.Output.Exceptions;

public class WrongInputException : OutputException
{
    public WrongInputException()
        : base(ExitCodes.ERROR_WRONG_INPUT)
    {
        // no operation
    }

    public WrongInputException(string message)
        : base(ExitCodes.ERROR_WRONG_INPUT, message)
    {
        // no operation
    }

    public WrongInputException(string message, Exception inner)
        : base(ExitCodes.ERROR_WRONG_INPUT, message, inner)
    {
        // no operation
    }
}
