using System.Threading;
using System.Threading.Tasks;

namespace _42.Platform.Cli.Authentication;

public interface IAuthenticationConfigurationResolver
{
    // Throws AuthenticationException when no provider can be determined.
    Task<ResolvedAuthentication> ResolveAsync(CancellationToken cancellationToken = default);

    // Drops the cached discovery answer, so the next sign-in asks the server again.
    Task ForgetAsync(CancellationToken cancellationToken = default);
}
