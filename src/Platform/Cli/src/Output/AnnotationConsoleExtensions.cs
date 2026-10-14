using System;
using _42.Platform.Storyteller;
using SdkAnnotation = _42.Platform.Storyteller.Sdk.Annotation;

namespace _42.Platform.Cli.Output;

public static class AnnotationConsoleExtensions
{
    /// <summary>
    /// The marker of an annotation that is not active at <paramref name="now"/>: <c>[disabled]</c>,
    /// <c>[scheduled]</c> or <c>[expired]</c>, and <c>[deleted]</c> for a deleted one whatever its status.
    /// Empty for an active annotation.
    /// </summary>
    public static string FormatStatusMarker(SdkAnnotation annotation, DateTimeOffset now)
    {
        if (annotation.Deletion is not null)
        {
            return "[deleted]";
        }

        var status = AnnotationStatusExtensions.GetStatus(annotation.IsDisabled, annotation.ValidFrom, annotation.ExpiresAt, now);
        return status == AnnotationStatus.Active ? string.Empty : $"[{status.ToString().ToLowerInvariant()}]";
    }
}
