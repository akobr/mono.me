namespace _42.Testing.System.IO.Abstractions.Tracers;

public abstract class BaseFileSystemTracer : IFileSystemTracer
{
    public bool MainOperationsOnly { get; set; }

    public void Process(object?[]? args, string operationName = "")
    {
        if (string.IsNullOrEmpty(operationName))
        {
            return;
        }

        if (MainOperationsOnly)
        {
            if (operationName.StartsWith("Write")
                || operationName.StartsWith("Append")
                || operationName.StartsWith("Create")
                || operationName.StartsWith("Delete")
                || operationName.StartsWith("Copy")
                || operationName.StartsWith("Move")
                || operationName.StartsWith("Open")
                || operationName.StartsWith("New"))
            {
                TraceInternal(operationName, args);
            }

            return;
        }

        TraceInternal(operationName, args);
    }

    protected abstract void TraceInternal(string operationName, object?[]? args);
}
