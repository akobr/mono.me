using System.Diagnostics;

namespace _42.Crumble;

public class DebugCrumbTracer : ICrumbTracer
{
    /*private readonly Stopwatch _stopwatch = new();*/

    public void LogBeforeCrumbExecution(ICrumbInnerExecutionContext context)
    {
        /*Debug.WriteLine("Started", context.CrumbKey);
        Debug.WriteLine($"Elapsed: {_stopwatch.ElapsedMilliseconds} ms", context.CrumbKey);
        Debug.WriteLineIf(context.Input != null, $"Input: {context.Input}", context.CrumbKey);*/
    }

    public void LogAfterCrumbExecution(ICrumbInnerExecutionContext context)
    {
        /*Debug.WriteLine("Finished", context.CrumbKey);
        Debug.WriteLine($"Elapsed: {_stopwatch.ElapsedMilliseconds} ms", context.CrumbKey);
        Debug.WriteLineIf(context.Output != null, $"Output: {context.Output}", context.CrumbKey);*/
    }

    public void LogBeforeMiddlewares(ICrumbInnerExecutionContext context)
    {
        /*Debug.WriteLine("Middlewares started", context.CrumbKey);
        _stopwatch.Start();*/
    }

    public void LogAfterMiddlewares(ICrumbInnerExecutionContext context)
    {
        /*Debug.WriteLine("Middlewares finished", context.CrumbKey);
        Debug.WriteLine($"Elapsed: {_stopwatch.ElapsedMilliseconds} ms", context.CrumbKey)*/;
    }

    public void LogException(Exception exception, ICrumbInnerExecutionContext context)
    {
        /*Debug.WriteLine($"Error: {exception.Message}", context.CrumbKey);*/
    }

    public void Dispose()
    {
        // no operation
    }
}
