using System.Globalization;

namespace _42.Platform.Storyteller;

// The operation needs a recent sign-in of the user (Auth:StepUp): the API answers 401 with the RFC 9470 challenge
// (WWW-Authenticate: Bearer error="insufficient_user_authentication", max_age) and the error code StepUpRequired.
// Not a SecurityTokenException, which ends in a bare 401: the client has to learn that a new sign-in helps.
public sealed class StepUpRequiredException : StorytellerException
{
    public StepUpRequiredException(TimeSpan maxAge)
        : base(
            $"This operation needs a recent sign-in: sign in again, then repeat it. A sign-in counts for {Describe(maxAge)}.",
            ErrorCodes.StepUpRequired)
    {
        MaxAge = maxAge;
    }

    public TimeSpan MaxAge { get; }

    // The max_age of the challenge: whole seconds.
    public long MaxAgeSeconds => (long)Math.Floor(MaxAge.TotalSeconds);

    // "10 minutes", "1 hour", "90 seconds": the largest unit that divides the duration.
    public static string Describe(TimeSpan duration)
    {
        var seconds = (long)Math.Floor(duration.TotalSeconds);

        if (seconds > 0 && seconds % 3600 == 0)
        {
            return Plural(seconds / 3600, "hour");
        }

        return seconds > 0 && seconds % 60 == 0
            ? Plural(seconds / 60, "minute")
            : Plural(seconds, "second");
    }

    private static string Plural(long count, string unit)
    {
        return string.Create(CultureInfo.InvariantCulture, $"{count} {unit}{(count == 1 ? string.Empty : "s")}");
    }
}
