using System.Security.Claims;

using _42.Platform.Storyteller.Mcp.Authentication;
using _42.Platform.Storyteller.Mcp.Hosting;
using _42.Platform.Storyteller.Mcp.UnitTests.Support;

using Microsoft.IdentityModel.JsonWebTokens;

namespace _42.Platform.Storyteller.Mcp.UnitTests;

public class McpTokenRulesTests
{
    private static readonly McpAuthSettings Settings = McpAuthSettings.From(new McpHostOptions
    {
        ResourceUrl = TestTokens.ResourceUrl,
        Authentication = new McpAuthenticationOptions
        {
            TenantId = TestTokens.TenantId,
            ClientId = TestTokens.McpClientId,
            AllowedClientIds = [TestTokens.ClientAppId.ToUpperInvariant()],
        },
    });

    private static readonly McpAuthSettings AuthKit = IssuerSettings(McpIdentityProvider.AuthKit, TestTokens.AuthKitIssuer, TestTokens.AuthKitClientId);

    private static readonly McpAuthSettings AuthKitAnyClient = IssuerSettings(McpIdentityProvider.AuthKit, TestTokens.AuthKitIssuer);

    private static readonly McpAuthSettings Keycloak = IssuerSettings(McpIdentityProvider.Keycloak, TestTokens.KeycloakIssuer, TestTokens.KeycloakClientId);

    private static readonly McpAuthSettings KeycloakAnyClient = IssuerSettings(McpIdentityProvider.Keycloak, TestTokens.KeycloakIssuer);

    [Fact]
    public void Check_NoPrincipal_Fails()
    {
        McpTokenRules.Check(null, Settings).ShouldBe("The token has no claims.");
    }

    [Fact]
    public void Check_DelegatedTokenOfAnAllowedClient_Passes()
    {
        McpTokenRules.Check(Principal(("tid", TestTokens.TenantId), ("azp", TestTokens.ClientAppId), ("scp", "Mcp.Access")), Settings).ShouldBeNull();
    }

    [Fact]
    public void Check_TenantIgnoresCase()
    {
        McpTokenRules.Check(Principal(("tid", TestTokens.TenantId.ToUpperInvariant()), ("azp", TestTokens.ClientAppId), ("scp", "Mcp.Access")), Settings).ShouldBeNull();
    }

    [Fact]
    public void Check_OtherTenant_Fails()
    {
        McpTokenRules.Check(Principal(("tid", TestTokens.OtherTenantId), ("azp", TestTokens.ClientAppId), ("scp", "Mcp.Access")), Settings)
            .ShouldBe("The token is not from the tenant of the MCP server.");
    }

    [Fact]
    public void Check_UnknownClient_Fails()
    {
        McpTokenRules.Check(Principal(("tid", TestTokens.TenantId), ("azp", TestTokens.OtherClientAppId), ("scp", "Mcp.Access")), Settings)
            .ShouldBe(McpTokenRules.ClientNotAcceptedMessage);
    }

    [Theory]
    [InlineData("idtyp", "app")]
    [InlineData("roles", "Mcp.Access")]
    [InlineData("scope", "Mcp.Access")]
    public void Check_AppOnlyToken_Fails(string claim, string value)
    {
        McpTokenRules.Check(Principal(("tid", TestTokens.TenantId), ("appid", TestTokens.ClientAppId), (claim, value)), Settings)
            .ShouldBe(McpTokenRules.NotAUserMessage);
    }

    [Fact]
    public void Check_AuthKitUserOfAnAllowedClient_Passes()
    {
        McpTokenRules.Check(Principal(("sub", TestTokens.AuthKitUserId), ("client_id", TestTokens.AuthKitClientId)), AuthKit).ShouldBeNull();
    }

    [Fact]
    public void Check_AuthKitClientInAzp_Passes()
    {
        McpTokenRules.Check(Principal(("sub", TestTokens.AuthKitUserId), ("azp", TestTokens.AuthKitClientId)), AuthKit).ShouldBeNull();
    }

    // An M2M token names its application in sub.
    [Theory]
    [InlineData(TestTokens.AuthKitClientId)]
    [InlineData("User_01JMCPTESTUSER0000000000")]
    [InlineData("")]
    public void Check_AuthKitSubjectThatIsNotAUser_Fails(string subject)
    {
        McpTokenRules.Check(Principal(("sub", subject), ("client_id", TestTokens.AuthKitClientId)), AuthKit).ShouldBe(McpTokenRules.NotAUserMessage);
    }

    [Fact]
    public void Check_AuthKitWithoutASubject_Fails()
    {
        McpTokenRules.Check(Principal(("client_id", TestTokens.AuthKitClientId)), AuthKit).ShouldBe(McpTokenRules.NotAUserMessage);
    }

    [Theory]
    [InlineData(TestTokens.OtherAuthKitClientId)]
    [InlineData("CLIENT_01JMCPTESTCLIENT00000000")]
    public void Check_AuthKitClientNotAllowed_Fails(string clientId)
    {
        McpTokenRules.Check(Principal(("sub", TestTokens.AuthKitUserId), ("client_id", clientId)), AuthKit).ShouldBe(McpTokenRules.ClientNotAcceptedMessage);
    }

    [Fact]
    public void Check_AuthKitWithoutAClientWhenClientsAreListed_Fails()
    {
        McpTokenRules.Check(Principal(("sub", TestTokens.AuthKitUserId)), AuthKit).ShouldBe(McpTokenRules.ClientNotAcceptedMessage);
    }

    [Fact]
    public void Check_AuthKitWithoutListedClients_AcceptsAnyClient()
    {
        McpTokenRules.Check(Principal(("sub", TestTokens.AuthKitUserId), ("client_id", TestTokens.OtherAuthKitClientId)), AuthKitAnyClient).ShouldBeNull();
        McpTokenRules.Check(Principal(("sub", TestTokens.AuthKitUserId)), AuthKitAnyClient).ShouldBeNull();
    }

    [Fact]
    public void Check_KeycloakPersonOfAnAllowedClient_Passes()
    {
        McpTokenRules.Check(Principal(("sub", TestTokens.KeycloakUserId), ("typ", "Bearer"), ("azp", TestTokens.KeycloakClientId)), Keycloak).ShouldBeNull();
    }

    // Keycloak puts typ Bearer on every access token, so a token without typ is refused.
    [Fact]
    public void Check_KeycloakWithoutATokenType_Fails()
    {
        McpTokenRules.Check(Principal(("sub", TestTokens.KeycloakUserId), ("azp", TestTokens.KeycloakClientId)), Keycloak).ShouldBe("The token is not an access token.");
    }

    [Theory]
    [InlineData("client_id", TestTokens.KeycloakClientId)]
    [InlineData("clientId", TestTokens.KeycloakClientId)]
    [InlineData("preferred_username", "service-account-mcp-client")]
    [InlineData("preferred_username", "Service-Account-mcp-client")]
    public void Check_KeycloakServiceAccount_Fails(string claim, string value)
    {
        McpTokenRules.Check(Principal(("sub", TestTokens.KeycloakUserId), ("typ", "Bearer"), ("azp", TestTokens.KeycloakClientId), (claim, value)), Keycloak)
            .ShouldBe(McpTokenRules.NotAUserMessage);
    }

    [Fact]
    public void Check_KeycloakWithoutASubject_Fails()
    {
        McpTokenRules.Check(Principal(("azp", TestTokens.KeycloakClientId)), Keycloak).ShouldBe(McpTokenRules.NotAUserMessage);
    }

    [Theory]
    [InlineData("ID")]
    [InlineData("Refresh")]
    public void Check_KeycloakTokenThatIsNotAnAccessToken_Fails(string tokenType)
    {
        McpTokenRules.Check(Principal(("sub", TestTokens.KeycloakUserId), ("typ", tokenType), ("azp", TestTokens.KeycloakClientId)), Keycloak)
            .ShouldBe("The token is not an access token.");
    }

    [Fact]
    public void Check_KeycloakClientNotAllowed_Fails()
    {
        McpTokenRules.Check(Principal(("sub", TestTokens.KeycloakUserId), ("typ", "Bearer"), ("azp", TestTokens.OtherKeycloakClientId)), Keycloak)
            .ShouldBe(McpTokenRules.ClientNotAcceptedMessage);
    }

    [Fact]
    public void Check_KeycloakWithoutListedClients_AcceptsAnyClient()
    {
        McpTokenRules.Check(Principal(("sub", TestTokens.KeycloakUserId), ("typ", "Bearer"), ("azp", TestTokens.OtherKeycloakClientId)), KeycloakAnyClient).ShouldBeNull();
    }

    // The API's exchange refuses a subject token that expires within 5 seconds; inbound, 10 seconds are needed.
    [Theory]
    [InlineData(600_000, true)]
    [InlineData(10_000, true)]
    [InlineData(9_999, false)]
    [InlineData(0, false)]
    [InlineData(-30_000, false)]
    public void CheckLifetime_RequiresTheMinimumRemainingLifetime(int remainingMilliseconds, bool accepted)
    {
        var expires = new DateTime(2026, 10, 26, 8, 0, 0, DateTimeKind.Utc);
        var token = new JsonWebToken(TestTokens.Create(expires: expires));
        var now = new DateTimeOffset(expires) - TimeSpan.FromMilliseconds(remainingMilliseconds);

        McpTokenRules.MinRemainingLifetime.ShouldBe(TimeSpan.FromSeconds(10));
        McpTokenRules.CheckLifetime(token, now).ShouldBe(accepted ? null : McpTokenRules.ExpiresTooSoonMessage);
    }

    [Theory]
    [InlineData(McpIdentityProvider.EntraId, "scp")]
    [InlineData(McpIdentityProvider.Keycloak, "scope")]
    [InlineData(McpIdentityProvider.AuthKit, "scope")]
    public void Scopes_AreSplitOnSpaces(McpIdentityProvider provider, string claim)
    {
        var principal = Principal((claim, "Mcp.Access  Other"));

        McpTokenRules.HasScope(principal, provider, "Mcp.Access").ShouldBeTrue();
        McpTokenRules.HasScope(principal, provider, "Other").ShouldBeTrue();
        McpTokenRules.HasScope(principal, provider, "mcp.access").ShouldBeFalse();
    }

    [Fact]
    public void Scopes_UnderAuthKit_IncludeThePermissions()
    {
        var principal = Principal(("permissions", "Mcp.Access"), ("permissions", "widgets:read"));

        McpTokenRules.GetScopes(principal, McpIdentityProvider.AuthKit).ShouldBe(["Mcp.Access", "widgets:read"], ignoreOrder: true);
    }

    [Theory]
    [InlineData(McpIdentityProvider.EntraId)]
    [InlineData(McpIdentityProvider.Keycloak)]
    public void Scopes_UnderOtherProviders_IgnoreThePermissions(McpIdentityProvider provider)
    {
        McpTokenRules.HasScope(Principal(("permissions", "Mcp.Access")), provider, "Mcp.Access").ShouldBeFalse();
    }

    private static McpAuthSettings IssuerSettings(McpIdentityProvider provider, string issuer, params string[] allowedClientIds)
    {
        return McpAuthSettings.From(new McpHostOptions
        {
            Provider = provider,
            ResourceUrl = TestTokens.ResourceUrl,
            Authentication = new McpAuthenticationOptions { Issuer = issuer, AllowedClientIds = [.. allowedClientIds] },
        });
    }

    private static ClaimsPrincipal Principal(params (string Type, string Value)[] claims)
    {
        return new ClaimsPrincipal(new ClaimsIdentity(claims.Select(claim => new Claim(claim.Type, claim.Value)), "test"));
    }
}
