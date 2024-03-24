using System.Threading.Tasks;
using Microsoft.Identity.Client;

namespace _42.Platform.Cli.Authentication;

public interface IAuthenticationService
{
    string[] Scopes { get; }

    Task<IPublicClientApplication> GetPublicClientApplicationAsync();

    Task<AuthenticationResult?> GetAuthenticationAsync();

    Task ClearAuthenticationAsync();
}
