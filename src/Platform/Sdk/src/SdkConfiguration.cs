using System;
using _42.Platform.Sdk.Client;

namespace _42.Platform.Sdk;

public class SdkConfiguration : Configuration
{
    public SdkConfiguration()
    {
        BasePath = "https://api.42.com"; // TODO
        UserAgent = $"42.sform.sdk/{ThisAssembly.AssemblyInformationalVersion} ({Environment.OSVersion.Platform:G}; {Environment.OSVersion.VersionString})";
    }

    public Func<string> AccessTokenFactory { get; set; }

    public override string AccessToken
    {
        get => AccessTokenFactory();
        set => throw new NotSupportedException();
    }

    public static readonly ExceptionFactory DefaultExceptionFactory = (methodName, response) =>
    {
        var status = (int)response.StatusCode;

        return status switch
        {
            404 => null,

            401 => new UnauthorizedAccessException(
                "Unauthorized access.",
                new ApiException(
                    status,
                    $"Error calling {methodName}: {response.RawContent}",
                    response.RawContent,
                    response.Headers)),

            >= 400 => new ApiException(
                status,
                $"Error calling {methodName}: {response.RawContent}",
                response.RawContent,
                response.Headers),

            0 => new ApiException(
                status,
                $"Error calling {methodName}: {response.ErrorText}",
                response.ErrorText),

            _ => null
        };
    };
}
