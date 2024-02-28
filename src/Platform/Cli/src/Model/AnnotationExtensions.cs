using System.Linq;
using CoreAnnotation = _42.Platform.Storyteller.Entities.Annotation;
using SdkAnnotation = _42.Platform.Sdk.Model.Annotation;

namespace _42.Platform.Cli.Model;

public static class AnnotationExtensions
{
    public static SdkAnnotation ToSdkAnnotation(this CoreAnnotation @this)
    {
        return new SdkAnnotation
        {
            AnnotationType = (SdkAnnotation.AnnotationTypeEnum)@this.AnnotationType,
            Name = @this.Name,
            PartitionKey = @this.PartitionKey,

            Title = @this.Title,
            ValidFrom = @this.ValidFrom?.UtcDateTime,
            ExpiresAt = @this.ExpiresAt?.UtcDateTime,
            IsDisabled = @this.IsDisabled,
            VarTimeZone = @this.TimeZone,

            ViewName = @this.ViewName,
            ProjectName = @this.ProjectName,

            Description = @this.Description,
            DocumentationLink = @this.DocumentationLink,

            Values = @this.Values.ToDictionary(),
            Labels = @this.Labels.ToList(),
        };
    }
}
