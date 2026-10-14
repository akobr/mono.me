using System;

namespace _42.Platform.Storyteller;

public static class AnnotationStatusExtensions
{
    /// <summary>
    /// The status at <paramref name="now"/>: disabled wins, then a <c>ValidFrom</c> after now (scheduled), then an
    /// <c>ExpiresAt</c> at or before now (expired); otherwise active. The dates are compared as instants, so neither
    /// their offset nor the annotation's <c>TimeZone</c> matters.
    /// </summary>
    public static AnnotationStatus GetStatus(bool? isDisabled, DateTimeOffset? validFrom, DateTimeOffset? expiresAt, DateTimeOffset now)
    {
        if (isDisabled == true)
        {
            return AnnotationStatus.Disabled;
        }

        if (validFrom > now)
        {
            return AnnotationStatus.Scheduled;
        }

        return expiresAt <= now ? AnnotationStatus.Expired : AnnotationStatus.Active;
    }

    public static AnnotationStatus GetStatus(this IAnnotation @this, DateTimeOffset now)
    {
        return GetStatus(@this.IsDisabled, @this.ValidFrom, @this.ExpiresAt, now);
    }
}
