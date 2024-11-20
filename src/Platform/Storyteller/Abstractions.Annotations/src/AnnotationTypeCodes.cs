using System;
using System.Collections.Generic;

namespace _42.Platform.Storyteller;

public static class AnnotationTypeCodes
{
    public const string Responsibility = "rst";
    public const string Subject = "sbt";
    public const string Usage = "usg";
    public const string Context = "cnt";
    public const string Execution = "exe";
    public const string Unit = "unt";
    public const string UnitOfExecution = "uxe";

    public static readonly IReadOnlyDictionary<string, AnnotationType> ValidCodes
        = new Dictionary<string, AnnotationType>(StringComparer.OrdinalIgnoreCase)
        {
            { Responsibility, AnnotationType.Responsibility },
            { Subject, AnnotationType.Subject },
            { Usage, AnnotationType.Usage },
            { Context, AnnotationType.Context },
            { Execution, AnnotationType.Execution },
            { Unit, AnnotationType.Unit },
        };
}
