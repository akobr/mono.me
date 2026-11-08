using System;
using System.IO;
using System.IO.Abstractions;
using System.IO.Abstractions.TestingHelpers;
using System.Threading.Tasks;
using _42.Platform.Cli.Authentication;
using Shouldly;
using Xunit;

namespace _42.Platform.Cli.UnitTests.Authentication;

public sealed class EntraIdAuthenticationServiceTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "sform-entra-" + Guid.NewGuid().ToString("N"));

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

    // Entra ID has a fixed tenant: -o|--organization is a mistake, reported before MSAL or any request.
    [Fact]
    public async Task Login_WithAnOrganization_IsAConfigurationFailure()
    {
        var service = new EntraIdAuthenticationService(new MockFileSystem(), Settings(), new TokenStorage(_directory, IsUnprotected: false));

        var exception = await Should.ThrowAsync<AuthenticationException>(
            () => service.LoginWithDeviceCodeAsync(_ => Task.CompletedTask, organizationId: "org_01"));

        exception.Reason.ShouldBe(AuthenticationFailureReason.Configuration);
        exception.Message.ShouldContain("-o|--organization");
        Directory.Exists(_directory).ShouldBeFalse();
    }

    // SFORM_TOKEN_STORE=file: the MSAL cache is the unprotected file of the token storage, whose directory (SFORM_HOME)
    // is created first; an empty cache means no signed-in user.
    [Fact]
    public async Task FileStore_UsesTheDirectoryOfTheTokenStorage()
    {
        var service = new EntraIdAuthenticationService(new FileSystem(), Settings(), new TokenStorage(_directory, IsUnprotected: true));

        (await service.GetSignedInUserAsync()).ShouldBeNull();

        Directory.Exists(_directory).ShouldBeTrue();
    }

    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }

    private static ResolvedAuthentication Settings()
    {
        return new ResolvedAuthentication
        {
            Provider = AuthenticationProvider.EntraId,
            ClientId = "00000000-0000-0000-0000-000000000001",
            TenantId = "00000000-0000-0000-0000-000000000002",
        };
    }
}
