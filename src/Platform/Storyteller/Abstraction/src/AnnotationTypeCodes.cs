using System;
using System.Collections.Generic;
using _42.Platform.Storyteller.Entities;

namespace _42.Platform.Storyteller;

public class AnnotationTypeCodes
{
    public const string Responsibility = "rst";
    public const string Subject = "sbt";
    public const string Usage = "usg";
    public const string Context = "cnt";
    public const string Execution = "exe";
    public const string Job = "job";

    public static readonly IReadOnlyDictionary<string, AnnotationType> ValidCodes
        = new Dictionary<string, AnnotationType>(StringComparer.OrdinalIgnoreCase)
        {
            { Responsibility, AnnotationType.Responsibility },
            { Subject, AnnotationType.Subject },
            { Usage, AnnotationType.Usage },
            { Context, AnnotationType.Context },
            { Execution, AnnotationType.Execution },
            { Job, AnnotationType.Job },
        };

    public static AnnotationType GetType(string code)
    {
        if (!ValidCodes.TryGetValue(code, out var type))
        {
            throw new ArgumentOutOfRangeException(nameof(code));
        }

        return type;
    }

    public static Type GetRealType(AnnotationType type)
    {
        return type switch
        {
            AnnotationType.Responsibility => typeof(Responsibility),
            AnnotationType.Subject => typeof(Subject),
            AnnotationType.Usage => typeof(Usage),
            AnnotationType.Context => typeof(Context),
            AnnotationType.Execution => typeof(Execution),
            AnnotationType.Job => typeof(Unit),
            _ => throw new ArgumentOutOfRangeException(nameof(type)),
        };
    }
}
