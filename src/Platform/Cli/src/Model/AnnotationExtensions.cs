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
    // The body of a create or a replace: the input alone, so a write to an existing key replaces its content. The
    // relation arrays stay out: the server owns them.
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

        annotation.Values = @this.Values?.ToDictionary(pair => pair.Key, pair => pair.Value) ?? new Dictionary<string, object>();
        annotation.Labels = GetLabels(@this).ToList();

        return annotation;
    }

    // The merge into an existing key, as a JSON Patch of its editable document that the server applies to the current
    // state: each member the input has (not null) is set, each value of the input replaces the stored value of the same
    // name whole, and each label is appended (the server keeps a label once, so the stored ones stay first). A unit's
    // type and definition are set when the input has them. What the input does not hold is kept.
    public static Sdk.AnnotationJsonPatch ToJsonPatch(this CoreAnnotation @this)
    {
        var patch = new Sdk.AnnotationJsonPatch();

        SetWhenGiven(patch, nameof(CoreAnnotation.IsDisabled), @this.IsDisabled);
        SetWhenGiven(patch, nameof(CoreAnnotation.ValidFrom), @this.ValidFrom);
        SetWhenGiven(patch, nameof(CoreAnnotation.ExpiresAt), @this.ExpiresAt);
        SetWhenGiven(patch, nameof(CoreAnnotation.TimeZone), @this.TimeZone);
        SetWhenGiven(patch, nameof(CoreAnnotation.Title), @this.Title);
        SetWhenGiven(patch, nameof(CoreAnnotation.Description), @this.Description);
        SetWhenGiven(patch, nameof(CoreAnnotation.DocumentationLink), @this.DocumentationLink);

        if (@this is CoreUnit unit)
        {
            // null when the file does not name them, whatever the model declares
            SetWhenGiven(patch, nameof(CoreUnit.UnitType), unit.UnitType);
            SetWhenGiven(patch, nameof(CoreUnit.UnitDefinition), unit.UnitDefinition);
        }

        foreach (var (name, value) in @this.Values ?? new Dictionary<string, object>())
        {
            patch.SetValue(name, value);
        }

        foreach (var label in GetLabels(@this))
        {
            patch.AddLabel(label);
        }

        return patch;
    }

    private static void SetWhenGiven(Sdk.AnnotationJsonPatch patch, string member, object? value)
    {
        if (value is not null)
        {
            patch.Set(member, value);
        }
    }

    // Each label once, in the order of the input; a null in a file is no label.
    private static IEnumerable<string> GetLabels(CoreAnnotation annotation)
    {
        return (annotation.Labels ?? []).OfType<string>().Distinct(StringComparer.Ordinal);
    }

    // The model of the key's type, with its type-specific members. The API reads a body into that type, where they are
    // required, and answers 400 without them. Everything but a unit's type and definition comes from the key, as the
    // API's AnnotationFactory makes it; those come from the input, else the defaults of a create.
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
