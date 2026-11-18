using System;
using _42.Platform.Storyteller.Sdk;

namespace _42.Platform.Cli.Output;

// The step-up of the API (Auth:StepUp): a secret, a role change or a machine credential needs a sign-in within the
// window, and a refresh of the token keeps the time of the old one. sform signs in with the device code, which has no
// max_age, and logout clears only its own token store; a code approved in a browser that is still signed in may not count
// as a new sign-in. So the remedy ends the browser's session first.
internal static class StepUpRefusal
{
    public const string ErrorCode = StepUp.ErrorCode;

    // The steps of a new sign-in, as the middle of a sentence.
    public const string SignInSteps = "sign out in the browser first (the admin UI's Sign out ends the browser's session), run 'sform account logout' and 'sform account', approve the new code (in a private window if the browser is still signed in)";

    // The steps of a new sign-in and the repeat of a command that only reads or was refused, as the end of a sentence.
    public const string Remedy = SignInSteps + ", and repeat the command.";

    public const string Hint = "To sign in again, " + Remedy;

    public const string GeneralMessage = "This needs a recent sign-in.";

    // "This needs a recent sign-in: a sign-in counts for 10 minutes." with the window the API names, else the server's
    // message, else the sentence alone.
    public static string MessageOf(TimeSpan? maxAge, string? serverMessage)
    {
        if (maxAge is { } window && window > TimeSpan.Zero)
        {
            return $"This needs a recent sign-in: a sign-in counts for {Describe(window)}.";
        }

        return string.IsNullOrWhiteSpace(serverMessage) ? GeneralMessage : serverMessage;
    }

    // "10 minutes", "1 hour", "90 seconds": the largest unit that divides the window, as the API writes it.
    public static string Describe(TimeSpan duration)
    {
        return StepUp.DescribeWindow(duration);
    }
}
