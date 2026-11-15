using System;
using System.Collections.Generic;
using System.Text;

namespace _42.Platform.Cli.ErrorReporting;

// An exception as the local log may keep it: never thrown, only logged. Its text is the chain as Exception.ToString
// writes it, the types and the stack traces as they are (method names and the source paths of the build, no values)
// and every message scrubbed of the values of the run and the shapes of secrets. The file sink renders {Exception} with
// ToString, so the text is built once, here, and no scrubbing runs inside the sink. When a scrubbing rule times out,
// the chain is written without any message.
internal sealed class RedactedException : Exception
{
    private const string LeftOut = "(the message is left out)";
    private const string InnerArrow = " ---> ";
    private const string EndOfInner = "   --- End of inner exception stack trace ---";

    private readonly string _text;
    private readonly string? _stackTrace;

    private RedactedException(string message, string text, string? stackTrace, int hResult)
        : base(message)
    {
        _text = text;
        _stackTrace = stackTrace;
        HResult = hResult;
    }

    public override string? StackTrace => _stackTrace;

    public static RedactedException Of(Exception original, IReadOnlyList<string> values)
    {
        try
        {
            return new RedactedException(
                ReportScrubber.ScrubSecrets(original.Message, values),
                Write(original, exception => ReportScrubber.ScrubSecrets(exception.Message, values)),
                original.StackTrace,
                original.HResult);
        }
        catch (Exception)
        {
            return new RedactedException(LeftOut, Write(original, _ => LeftOut), original.StackTrace, original.HResult);
        }
    }

    public override string ToString()
    {
        return _text;
    }

    private static string Write(Exception exception, Func<Exception, string> messageOf)
    {
        var builder = new StringBuilder();
        Write(builder, exception, messageOf);
        return builder.ToString();
    }

    private static void Write(StringBuilder builder, Exception exception, Func<Exception, string> messageOf)
    {
        builder.Append(exception.GetType().FullName ?? exception.GetType().Name);

        var message = messageOf(exception);

        if (message.Length > 0)
        {
            builder.Append(": ").Append(message);
        }

        if (exception is AggregateException aggregate)
        {
            for (var index = 0; index < aggregate.InnerExceptions.Count; index++)
            {
                builder.AppendLine().Append(InnerArrow).Append($"(Inner Exception #{index}) ");
                Write(builder, aggregate.InnerExceptions[index], messageOf);
                builder.AppendLine().Append("<---");
            }
        }
        else if (exception.InnerException is { } inner)
        {
            builder.Append(InnerArrow);
            Write(builder, inner, messageOf);
            builder.AppendLine().Append(EndOfInner);
        }

        if (exception.StackTrace is { } stackTrace)
        {
            builder.AppendLine().Append(stackTrace);
        }
    }
}
