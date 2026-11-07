using System;
using System.Collections.Generic;
using System.Linq;
using _42.Platform.Storyteller;
using CoreAnnotation = _42.Platform.Storyteller.Annotation;
using CoreUnit = _42.Platform.Storyteller.Unit;
using Sdk = _42.Platform.Storyteller.Sdk;
using SdkAnnotation = _42.Platform.Storyteller.Sdk.Annotation;

namespace _42.Platform.Cli.Model;

public static class AnnotationExtensions
{
    // The body of a write. The relation arrays stay out: the server owns them.
    public static SdkAnnotation ToSdkAnnotation(this CoreAnnotation @this)
    {
        var annotation = CreateForKey(AnnotationKey.Parse(@this.AnnotationKey), @this);

        annotation.AnnotationKey = @this.AnnotationKey;
        annotation.Name = @this.Name;
        annotation.Title = @this.Title;
        annotation.ValidFrom = @this.ValidFrom;
        annotation.ExpiresAt = @this.ExpiresAt;
        annotation.IsDisabled = @this.IsDisabled;
        annotation.TimeZone = @this.TimeZone;

        annotation.ViewName = @this.ViewName;
        annotation.ProjectName = @this.ProjectName;

        annotation.Description = @this.Description;
        annotation.DocumentationLink = @this.DocumentationLink;

        annotation.Values = @this.Values?.ToDictionary(k => k.Key, v => v.Value) ?? new Dictionary<string, object>();
        annotation.Labels = @this.Labels?.ToList() ?? new List<string>();

        return annotation;
    }

    // The model of the key's type, with its type-specific members. The API reads a body into that type, where they are
    // required, and answers 400 without them. Everything but a unit's type and definition comes from the key, as the
    // API's AnnotationFactory makes it.
    private static SdkAnnotation CreateForKey(AnnotationKey key, CoreAnnotation source)
    {
        return key.Type switch
        {
            AnnotationType.Responsibility => new Sdk.Responsibility(),
            AnnotationType.Subject => new Sdk.Subject(),
            AnnotationType.Unit => new Sdk.Unit
            {
                ResponsibilityKey = key.GetResponsibilityKey(),
                ResponsibilityName = key.GetResponsibilityName(),
                UnitType = (source as CoreUnit)?.UnitType ?? CoreUnit.DefaultUnitType,
                UnitDefinition = (source as CoreUnit)?.UnitDefinition ?? CoreUnit.DefaultUnitDefinition,
            },
            AnnotationType.Usage => new Sdk.Usage
            {
                SubjectKey = key.GetSubjectKey(),
                SubjectName = key.GetSubjectName(),
                ResponsibilityKey = key.GetResponsibilityKey(),
                ResponsibilityName = key.GetResponsibilityName(),
            },
            AnnotationType.Context => new Sdk.Context
            {
                SubjectKey = key.GetSubjectKey(),
                SubjectName = key.GetSubjectName(),
            },
            AnnotationType.Execution => new Sdk.Execution
            {
                SubjectKey = key.GetSubjectKey(),
                SubjectName = key.GetSubjectName(),
                ResponsibilityKey = key.GetResponsibilityKey(),
                ResponsibilityName = key.GetResponsibilityName(),
                ContextKey = key.GetContextKey(),
                ContextName = key.GetContextName(),
                UsageKey = key.GetUsageKey(),
            },
            AnnotationType.UnitOfExecution => new Sdk.UnitOfExecution
            {
                SubjectKey = key.GetSubjectKey(),
                SubjectName = key.GetSubjectName(),
                ResponsibilityKey = key.GetResponsibilityKey(),
                ResponsibilityName = key.GetResponsibilityName(),
                ContextKey = key.GetContextKey(),
                ContextName = key.GetContextName(),
                UsageKey = key.GetUsageKey(),
                UnitKey = key.GetUnitKey(),
                UnitName = key.GetUnitName(),
                ExecutionKey = key.GetExecutionKey(),
            },
            _ => throw new ArgumentOutOfRangeException(nameof(key), key.Type, "Unknown annotation type."),
        };
    }
}
