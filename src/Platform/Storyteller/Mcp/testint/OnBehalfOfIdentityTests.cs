using System.IdentityModel.Tokens.Jwt;

using _42.Platform.Storyteller.Mcp.Authentication;
using _42.Platform.Storyteller.Mcp.Hosting;

using Microsoft.Extensions.Options;

using Xunit.Abstractions;

namespace _42.Platform.Storyteller.Mcp.IntegrationTests;

// The API finds accounts by sub, and Entra documents sub as pairwise per application. This test tells whether, in a
// tenant, the on-behalf-of token of the MCP server carries the same sub as the person's sform token: if not, the MCP
// server cannot act for existing accounts there (docs/Platform/Storyteller/mcp.md, "Identity").
public class OnBehalfOfIdentityTests(ITestOutputHelper output)
{
    [Fact]
    public async Task OnBehalfOfToken_CarriesTheSubjectOfTheSformToken()
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

        Claim(onBehalfOf, "oid").ShouldBe(Claim(sform, "oid"), "The two tokens are not of the same person.");
        Claim(onBehalfOf, "sub").ShouldBe(Claim(sform, "sub"), "The sub differs: the MCP server cannot find the person's account.");
    }

    private static string? Claim(JwtSecurityToken token, string type)
    {
        return token.Claims.FirstOrDefault(claim => claim.Type == type)?.Value;
    }
}
