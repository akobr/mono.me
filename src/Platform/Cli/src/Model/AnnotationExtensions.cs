using System.Linq;
using CoreAnnotation = _42.Platform.Storyteller.Annotation;
using SdkAnnotation = ApiSdk.Models.Annotation;

namespace _42.Platform.Cli.Model;

public static class AnnotationExtensions
{
    public static SdkAnnotation ToSdkAnnotation(this CoreAnnotation @this)
    {
        return new SdkAnnotation
        {
            AnnotationType = (ApiSdk.Models.Annotation_AnnotationType)@this.AnnotationType,
            Name = @this.Name,

            Title = @this.Title,
            ValidFrom = @this.ValidFrom,
            ExpiresAt = @this.ExpiresAt,
            IsDisabled = @this.IsDisabled,
            TimeZone = @this.TimeZone,

            ViewName = @this.ViewName,
            ProjectName = @this.ProjectName,

            Description = @this.Description,
            DocumentationLink = @this.DocumentationLink,

            Values = new ApiSdk.Models.Annotation_Values
            {
                AdditionalData = @this.Values.ToDictionary(kvp => kvp.Key, kvp => kvp.Value),
            },
            Labels = @this.Labels.ToList(),
        };
    }
}
