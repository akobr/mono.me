using System.Collections.Generic;
using System.Linq;
using CoreAnnotation = _42.Platform.Storyteller.Annotation;
using SdkAnnotation = _42.Platform.Storyteller.Sdk.Annotation;

namespace _42.Platform.Cli.Model;

public static class AnnotationExtensions
{
    public static SdkAnnotation ToSdkAnnotation(this CoreAnnotation @this)
    {
        var annotation = @this.AnnotationType switch
        {
            _42.Platform.Storyteller.AnnotationType.Responsibility => new _42.Platform.Storyteller.Sdk.Responsibility(),
            _42.Platform.Storyteller.AnnotationType.Subject => new _42.Platform.Storyteller.Sdk.Subject(),
            _42.Platform.Storyteller.AnnotationType.Usage => new _42.Platform.Storyteller.Sdk.Usage(),
            _42.Platform.Storyteller.AnnotationType.Context => new _42.Platform.Storyteller.Sdk.Context(),
            _42.Platform.Storyteller.AnnotationType.Execution => new _42.Platform.Storyteller.Sdk.Execution(),
            _42.Platform.Storyteller.AnnotationType.UnitOfExecution => new _42.Platform.Storyteller.Sdk.UnitOfExecution(),
            _42.Platform.Storyteller.AnnotationType.Unit => new _42.Platform.Storyteller.Sdk.Unit(),
            _ => new SdkAnnotation(),
        };

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
}
