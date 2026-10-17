namespace _42.Platform.Cli.Authentication;

public enum AuthenticationFailureReason
{
    ServiceError = 0,
    Cancelled,
    Expired,
    Denied,

    // The sign-in settings are missing or invalid, e.g. the STORYTELLER_* variables of a machine.
    Configuration,
}
