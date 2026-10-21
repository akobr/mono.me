using _42.Platform.Cli.Authentication;
using Shouldly;
using Xunit;

namespace _42.Platform.Cli.UnitTests.Authentication;

public class EntraIdAuthenticationServiceTests
{
    [Fact]
    public void WithoutDiscoveredScopes_AsksForUserImpersonationOnly()
    {
        var settings = new ResolvedAuthentication { Provider = AuthenticationProvider.EntraId, ClientId = "app-id" };

        EntraIdAuthenticationService.GetScopes(settings).ShouldBe(["api://app-id/User.Impersonation"]);
    }

    [Fact]
    public void WithDiscoveredScopes_AsksForThem()
    {
        var settings = new ResolvedAuthentication
        {
            Provider = AuthenticationProvider.EntraId,
            ClientId = "app-id",
            Scopes = ["api://other-app/User.Impersonation", "api://other-app/Custom.Scope"],
        };

        EntraIdAuthenticationService.GetScopes(settings).ShouldBe(["api://other-app/User.Impersonation", "api://other-app/Custom.Scope"]);
    }
}
