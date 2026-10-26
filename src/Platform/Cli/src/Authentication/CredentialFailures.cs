using System;
using System.Threading.Tasks;
using _42.Platform.Storyteller.Sdk.Credentials;

namespace _42.Platform.Cli.Authentication;

// Every call into the credential contract of the SDK goes through here, so that only an AuthenticationException leaves
// the machine sign-in: Startup's token factory turns any other exception into an empty token, and the renewal handler
// would end the command with it. The message and the inner exception are kept.
internal static class CredentialFailures
{
    public static AuthenticationException Map(StorytellerAuthenticationException exception)
    {
        var reason = exception.Reason switch
        {
            StorytellerAuthenticationFailureReason.Configuration => AuthenticationFailureReason.Configuration,
            StorytellerAuthenticationFailureReason.Rejected => AuthenticationFailureReason.Denied,
            _ => AuthenticationFailureReason.ServiceError,
        };

        return new AuthenticationException(reason, exception.Message, exception.InnerException);
    }

    // The argument checks of the SDK's factories: a value the variables let through, but the credential refuses.
    public static AuthenticationException Map(ArgumentException exception)
    {
        return new AuthenticationException(AuthenticationFailureReason.Configuration, exception.Message, exception);
    }

    public static T Guard<T>(Func<T> call)
    {
        try
        {
            return call();
        }
        catch (StorytellerAuthenticationException exception)
        {
            throw Map(exception);
        }
        catch (ArgumentException exception)
        {
            throw Map(exception);
        }
    }

    public static async Task<T> GuardAsync<T>(Func<ValueTask<T>> call)
    {
        try
        {
            return await call();
        }
        catch (StorytellerAuthenticationException exception)
        {
            throw Map(exception);
        }
        catch (ArgumentException exception)
        {
            throw Map(exception);
        }
    }
}
