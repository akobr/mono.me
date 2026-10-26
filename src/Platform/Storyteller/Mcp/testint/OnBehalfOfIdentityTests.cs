using System.IdentityModel.Tokens.Jwt;

using _42.Platform.Storyteller.Mcp.Authentication;
using _42.Platform.Storyteller.Mcp.Hosting;

using Microsoft.Extensions.Options;

using Xunit.Abstractions;

namespace _42.Platform.Storyteller.Mcp.IntegrationTests;

// The API keys Entra accounts by {tid}:{oid} (UserIdentity.GetAccountId), not by sub, which Entra documents as pairwise
// per application. This test tells whether, in a tenant, the on-behalf-of token of the MCP server names the same
// account as the person's sform token: the same tid and oid. The subs are only printed, they may differ.
public class OnBehalfOfIdentityTests(ITestOutputHelper output)
{
    [Fact]
    public async Task OnBehalfOfToken_NamesTheAccountOfTheSformToken()
    {
        var settings = EntraTestSettings.TryRead();

        if (settings is null)
        {
            output.WriteLine($"Skipped: set {EntraTestSettings.Variables} to run against a tenant.");
            return;
        }

        var host = new McpHostOptions
        {
            ResourceUrl = "https://localhost/mcp",
            EntraId = new McpEntraIdOptions
            {
                TenantId = settings.TenantId,
                ClientId = settings.ClientId,
                ClientSecret = settings.ClientSecret,
                AllowedClientIds = [settings.ClientId],
            },
        };
        var provider = new EntraOnBehalfOfTokenProvider(
            Options.Create(host),
            Options.Create(new StorytellerApiOptions { BaseUrl = "https://localhost/api", Scopes = [settings.StorytellerScope] }),
            McpAuthSettings.From(host));

        var onBehalfOf = new JwtSecurityToken(await provider.GetTokenAsync(settings.UserToken, CancellationToken.None));
        var sform = new JwtSecurityToken(settings.SformToken);

        output.WriteLine($"OBO:   sub={Claim(onBehalfOf, "sub")} oid={Claim(onBehalfOf, "oid")} tid={Claim(onBehalfOf, "tid")} azp={Claim(onBehalfOf, "azp")}");
        output.WriteLine($"sform: sub={Claim(sform, "sub")} oid={Claim(sform, "oid")} tid={Claim(sform, "tid")} azp={Claim(sform, "azp")}");

        // Two tokens that both lack a claim would compare equal, while the API answers 401 to a token without tid or oid.
        Claim(onBehalfOf, "tid").ShouldNotBeNull("The on-behalf-of token has no tid: the API cannot name the account.");
        Claim(onBehalfOf, "tid").ShouldBe(Claim(sform, "tid"), "The two tokens are not of the same tenant.");
        Claim(onBehalfOf, "oid").ShouldNotBeNull("The on-behalf-of token has no oid: the API cannot name the account.");
        Claim(onBehalfOf, "oid").ShouldBe(Claim(sform, "oid"), "The two tokens are not of the same person.");
    }

    private static string? Claim(JwtSecurityToken token, string type)
    {
        return token.Claims.FirstOrDefault(claim => claim.Type == type)?.Value;
    }
}
