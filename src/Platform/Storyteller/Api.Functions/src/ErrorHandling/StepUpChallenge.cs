using System.Globalization;

namespace _42.Platform.Storyteller.Api.ErrorHandling;

// The challenge of the step-up 401 (RFC 9470, Figure 3), sent in WWW-Authenticate. Browsers cannot read it cross-origin,
// so the ErrorResponse repeats the max age in Details (StepUpDetails).
public static class StepUpChallenge
{
    public const string Error = "insufficient_user_authentication";

    public const string Description = "A recent sign-in is required";

    public static string Format(long maxAgeSeconds)
    {
        return string.Create(
            CultureInfo.InvariantCulture,
            $"Bearer error=\"{Error}\", error_description=\"{Description}\", max_age=\"{maxAgeSeconds}\"");
    }
}
