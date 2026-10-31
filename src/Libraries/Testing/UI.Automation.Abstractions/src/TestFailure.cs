using System.Diagnostics;

namespace _42.Testing.UI.Automation;

// Why a test failed. SourceFile and SourceLine point to the first frame with a file, when the PDBs are there; frames of
// packages built deterministically (paths under "/_/", e.g. Playwright's assertions) come only when no other has one.
public sealed record TestFailure(string Message, string? ExceptionType, string? StackTrace, string? SourceFile, int? SourceLine)
{
    // StackTrace is the ToString form, so inner exceptions are part of it. An AggregateException with one inner
    // exception is unwrapped.
    public static TestFailure FromException(Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);

        while (exception is AggregateException { InnerExceptions.Count: 1 } aggregate)
        {
            exception = aggregate.InnerExceptions[0];
        }

        var (file, line) = FindSource(exception);
        return new TestFailure(exception.Message, exception.GetType().FullName, exception.ToString(), file, line);
    }

    private static (string? File, int? Line) FindSource(Exception exception)
    {
        (string? File, int? Line) package = (null, null);

        try
        {
            foreach (var frame in new StackTrace(exception, fNeedFileInfo: true).GetFrames())
            {
                var file = frame.GetFileName();
                var line = frame.GetFileLineNumber();
                if (string.IsNullOrEmpty(file) || line <= 0)
                {
                    continue;
                }

                if (!file.StartsWith("/_/", StringComparison.Ordinal))
                {
                    return (file, line);
                }

                if (package.File is null)
                {
                    package = (file, line);
                }
            }
        }
        catch (Exception)
        {
            // Reading the symbols is best effort; the stack trace text stays.
        }

        return package;
    }
}
