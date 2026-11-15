using System;
using System.Collections.Generic;
using System.Text;

namespace _42.Platform.Cli.ErrorReporting;

// An exception as the local log may keep it: never thrown, only logged. ToString writes the chain as Exception.ToString
// does, the types and the stack traces as they are (method names and the source paths of the build, no values) and every
// message scrubbed of the values of the run and the shapes of secrets. The file sink renders {Exception} with ToString.
internal sealed class RedactedException : Exception
{
    private const string InnerArrow = " ---> ";
    private const string EndOfInner = "   --- End of inner exception stack trace ---";

    private readonly Exception _original;
    private readonly IReadOnlyList<string> _values;

    public RedactedException(Exception original, IReadOnlyList<string> values)
        : base(ReportScrubber.ScrubSecrets(original.Message, values))
    {
        _original = original;
        _values = values;
        HResult = original.HResult;
    }

    // The type of the original, for the log line.
    public Type OriginalType => _original.GetType();

    public override string? StackTrace => _original.StackTrace;

    public override string ToString()
    {
        var builder = new StringBuilder();
        Write(builder, _original);
        return builder.ToString();
    }

    private void Write(StringBuilder builder, Exception exception)
    {
        builder.Append(exception.GetType().FullName ?? exception.GetType().Name);

        var message = ReportScrubber.ScrubSecrets(exception.Message, _values);

        if (message.Length > 0)
        {
            builder.Append(": ").Append(message);
        }

        if (exception is AggregateException aggregate)
        {
            for (var index = 0; index < aggregate.InnerExceptions.Count; index++)
            {
                builder.AppendLine().Append(InnerArrow).Append($"(Inner Exception #{index}) ");
                Write(builder, aggregate.InnerExceptions[index]);
                builder.AppendLine().Append("<---");
            }
        }
        else if (exception.InnerException is { } inner)
        {
            builder.Append(InnerArrow);
            Write(builder, inner);
            builder.AppendLine().Append(EndOfInner);
        }

        if (exception.StackTrace is { } stackTrace)
        {
            builder.AppendLine().Append(stackTrace);
        }
    }
}
