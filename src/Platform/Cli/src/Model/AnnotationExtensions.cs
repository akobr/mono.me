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
    // The body of a write. Without a stored annotation it holds the input alone, and the write replaces the content of an
    // existing key. With one, the input is merged into it: a member the input has wins, the labels are joined, and each
    // value of the input is written over the one of the same name. The relation arrays stay out: the server owns them.
    public static SdkAnnotation ToSdkAnnotation(this CoreAnnotation @this, SdkAnnotation? stored = null)
    {
        var annotation = CreateForKey(AnnotationKey.Parse(@this.AnnotationKey), @this, stored);

        annotation.AnnotationKey = @this.AnnotationKey;
        annotation.Name = @this.Name;
        annotation.Title = @this.Title ?? stored?.Title;
        annotation.ValidFrom = @this.ValidFrom ?? stored?.ValidFrom;
        annotation.ExpiresAt = @this.ExpiresAt ?? stored?.ExpiresAt;
        annotation.IsDisabled = @this.IsDisabled ?? stored?.IsDisabled;
        annotation.TimeZone = @this.TimeZone ?? stored?.TimeZone;

        annotation.ViewName = @this.ViewName;
        annotation.ProjectName = @this.ProjectName;

        annotation.Description = @this.Description ?? stored?.Description;
        annotation.DocumentationLink = @this.DocumentationLink ?? stored?.DocumentationLink;

        var values = stored?.Values?.ToDictionary(pair => pair.Key, pair => pair.Value) ?? new Dictionary<string, object>();

        foreach (var (name, value) in @this.Values ?? new Dictionary<string, object>())
        {
            values[name] = value;
        }

        annotation.Values = values;
        annotation.Labels = (stored?.Labels ?? []).Concat(@this.Labels ?? []).Distinct(StringComparer.Ordinal).ToList();

        return annotation;
    }

    // The model of the key's type, with its type-specific members. The API reads a body into that type, where they are
    // required, and answers 400 without them. Everything but a unit's type and definition comes from the key, as the
    // API's AnnotationFactory makes it; those come from the input, else from the stored unit, else the defaults of a create.
    private static SdkAnnotation CreateForKey(AnnotationKey key, CoreAnnotation source, SdkAnnotation? stored)
    {
        return key.Type switch
        {
            AnnotationType.Responsibility => new Sdk.Responsibility(),
            AnnotationType.Subject => new Sdk.Subject(),
            AnnotationType.Unit => new Sdk.Unit
            {
                ResponsibilityKey = key.GetResponsibilityKey(),
                ResponsibilityName = key.GetResponsibilityName(),
                UnitType = (source as CoreUnit)?.UnitType ?? (stored as Sdk.Unit)?.UnitType ?? CoreUnit.DefaultUnitType,
                UnitDefinition = (source as CoreUnit)?.UnitDefinition ?? (stored as Sdk.Unit)?.UnitDefinition ?? CoreUnit.DefaultUnitDefinition,
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
