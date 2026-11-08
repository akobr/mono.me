using _42.CLI.Toolkit.Output;
using _42.Platform.Storyteller.Sdk;

namespace _42.Platform.Cli.Output;

public static class SchemaValidationConsole
{
    /// <summary>
    /// Prints a failed write of a configuration, a template or a schema, and returns the exit code of the command: the
    /// failing configurations of a schema violation (exit code 2), else the server's message and error code with the
    /// exit code of the status (<see cref="KeyValueConsoleExtensions.WriteApiError"/>).
    /// </summary>
    public static int WriteWriteError(IExtendedConsole console, ApiException exception)
    {
        return TryWrite(console, exception)
            ? ExitCodes.ERROR_WRONG_INPUT
            : console.WriteApiError(exception);
    }

    /// <summary>
    /// Prints a rejected schema or configuration write: each failing annotation key, its view, and the messages.
    /// </summary>
    /// <returns><c>true</c> when the exception carried compliance details.</returns>
    public static bool TryWrite(IExtendedConsole console, ApiException exception)
    {
        if (exception is not ApiException<SchemaValidationErrorResponse> schemaError
            || schemaError.Result?.Errors is not { Count: > 0 } errors)
        {
            return false;
        }

        if (!string.IsNullOrWhiteSpace(schemaError.Result.Message))
        {
            console.WriteImportant(schemaError.Result.Message);
        }

        foreach (var detail in errors)
        {
            console.WriteLine($"{detail.AnnotationKey} ({detail.ViewName})");

            if (detail.Errors is null)
            {
                continue;
            }

            foreach (var message in detail.Errors)
            {
                console.WriteLine($"  {message}");
            }
        }

        return true;
    }
}
