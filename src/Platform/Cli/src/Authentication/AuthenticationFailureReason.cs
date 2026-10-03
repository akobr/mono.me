namespace _42.Platform.Cli.Authentication;

public enum AuthenticationFailureReason
{
    ServiceError = 0,
    Cancelled,
    Expired,
    Denied,
}
