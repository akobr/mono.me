using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;

namespace _42.Platform.Storyteller;

public class AnnotationKey
{
    private readonly IReadOnlyList<string> _segments;

    internal AnnotationKey(ArraySegment<string> segments)
    {
        _segments = segments;
        Type = AnnotationTypes.GetType(TypeCode);
    }

    internal AnnotationKey(params string[] segments)
        : this(new ArraySegment<string>(segments))
    {
        // no operation
    }

    public string TypeCode => _segments[0];

    public AnnotationType Type { get; }

    public string SubjectName =>
        Type >= AnnotationType.Subject
            ? _segments[1]
            : string.Empty;

    public string ResponsibilityName =>
        Type switch
        {
            AnnotationType.Responsibility or AnnotationType.Unit => _segments[1],
            AnnotationType.Usage or AnnotationType.Execution => _segments[2],
            _ => string.Empty,
        };

    public string ContextName =>
        Type switch
        {
            AnnotationType.Context => _segments[2],
            AnnotationType.Execution => _segments[3],
            _ => string.Empty,
        };

    public string JobName =>
        Type == AnnotationType.Unit
            ? _segments[2]
            : string.Empty;

    public string Name => _segments[^1];

    internal IReadOnlyList<string> Segments => _segments;

    public static implicit operator string(AnnotationKey key)
    {
        return key.ToString();
    }

    public override string ToString()
    {
        return string.Join(Constants.DefaultKeySeparator, Segments);
    }

    public static bool TryParse(string text, [MaybeNullWhen(false)] out AnnotationKey key)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            key = null;
            return false;
        }

        var segments = text.Split(Constants.KeySeparators, StringSplitOptions.RemoveEmptyEntries);

        if (segments.Length is < 2 or > 4
            || !AnnotationTypeCodes.ValidCodes.ContainsKey(segments[0]))
        {
            key = null;
            return false;
        }

        key = new AnnotationKey(segments);
        return key.IsValid();
    }

    public static AnnotationKey Parse(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            throw new ArgumentNullException(nameof(text));
        }

        var segments = text.Split(Constants.KeySeparators, StringSplitOptions.RemoveEmptyEntries);
        var key = new AnnotationKey(segments);

        if (segments.Length is < 2 or > 4
            || !key.IsValid())
        {
            throw new ArgumentOutOfRangeException(nameof(text), "Invalid annotation key.");
        }

        return key;
    }

    public static AnnotationKey CreateResponsibility(string responsibilityName)
    {
        return new AnnotationKey(AnnotationTypeCodes.Responsibility, responsibilityName);
    }

    public static AnnotationKey CreateJob(string responsibilityName, string jobName)
    {
        return new AnnotationKey(AnnotationTypeCodes.Unit, responsibilityName, jobName);
    }

    public static AnnotationKey CreateSubject(string subjectName)
    {
        return new AnnotationKey(AnnotationTypeCodes.Subject, subjectName);
    }

    public static AnnotationKey CreateUsage(string subjectName, string responsibilityName)
    {
        return new AnnotationKey(AnnotationTypeCodes.Usage, subjectName, responsibilityName);
    }

    public static AnnotationKey CreateContext(string subjectName, string contextName)
    {
        return new AnnotationKey(AnnotationTypeCodes.Context, subjectName, contextName);
    }

    public static AnnotationKey CreateExecution(string subjectName, string responsibilityName, string contextName)
    {
        return new AnnotationKey(AnnotationTypeCodes.Execution, subjectName, responsibilityName, contextName);
    }
}
