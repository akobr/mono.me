using System.Security.Claims;

using _42.Platform.Storyteller.Mcp.Authentication;
using _42.Platform.Storyteller.Mcp.Hosting;
using _42.Platform.Storyteller.Mcp.UnitTests.Support;

namespace _42.Platform.Storyteller.Mcp.UnitTests;

public class McpTokenRulesTests
{
    private static readonly McpAuthSettings Settings = McpAuthSettings.From(new McpHostOptions
    {
        ResourceUrl = TestTokens.ResourceUrl,
        EntraId = new McpEntraIdOptions
        {
            TenantId = TestTokens.TenantId,
            ClientId = TestTokens.McpClientId,
            AllowedClientIds = [TestTokens.ClientAppId.ToUpperInvariant()],
            ClientSecret = "secret",
        },
    });

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
            .ShouldBe("The token was issued to a client app that the MCP server does not accept.");
    }

    [Theory]
    [InlineData("idtyp", "app")]
    [InlineData("roles", "Mcp.Access")]
    public void Check_AppOnlyToken_Fails(string claim, string value)
    {
        McpTokenRules.Check(Principal(("tid", TestTokens.TenantId), ("appid", TestTokens.ClientAppId), (claim, value)), Settings)
            .ShouldBe("The token does not act for a user.");
    }

    [Fact]
    public void Scopes_AreSplitOnSpaces()
    {
        var principal = Principal(("scp", "Mcp.Access  Other"));

        McpTokenRules.HasScope(principal, "Mcp.Access").ShouldBeTrue();
        McpTokenRules.HasScope(principal, "Other").ShouldBeTrue();
        McpTokenRules.HasScope(principal, "mcp.access").ShouldBeFalse();
    }

    private static ClaimsPrincipal Principal(params (string Type, string Value)[] claims)
    {
        return new ClaimsPrincipal(new ClaimsIdentity(claims.Select(claim => new Claim(claim.Type, claim.Value)), "test"));
    }
}
