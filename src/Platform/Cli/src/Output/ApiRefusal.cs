using System.Net;
using _42.Platform.Storyteller.Sdk;

namespace _42.Platform.Cli.Output;

/// <summary>
/// What <c>Program.Main</c> prints for a <c>401</c> or <c>403</c> of the API that the command left to it.
/// </summary>
/// <param name="Message">The server's message of a <c>403</c>, or the general sentence.</param>
/// <param name="ErrorCode">The server's error code of a <c>403</c>, when it sent one.</param>
/// <param name="MayBeTheCredentials">The credentials may be the cause, so the hint of a machine sign-in helps.</param>
internal sealed record ApiRefusal(string Message, string? ErrorCode, bool MayBeTheCredentials)
{
    public const string GeneralMessage = "You are not authorized to execute the command or you don't have access to requested resources.";

    public static ApiRefusal Of(ApiException exception)
    {
        // A 403 with the server's message names what is missing, for example a management scope the machine was not
        // created with: the credentials were accepted. A 401, or a 403 without a message, says nothing more.
        // An operation that does not declare 403 leaves the body as text, so read both shapes.
        if (exception.StatusCode == (int)HttpStatusCode.Forbidden)
        {
            var (message, errorCode) = KeyValueConsoleExtensions.ReadError(exception);

            if (!string.IsNullOrWhiteSpace(message))
            {
                return new ApiRefusal(
                    message,
                    string.IsNullOrWhiteSpace(errorCode) ? null : errorCode,
                    MayBeTheCredentials: false);
            }
        }

        return new ApiRefusal(GeneralMessage, null, MayBeTheCredentials: true);
    }
}
