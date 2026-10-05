using System.Security.Claims;

using _42.Platform.Storyteller.Accessing;

using Microsoft.Extensions.Options;

namespace _42.Platform.Storyteller.Access.AuthKit.UnitTests;

public class AuthKitMachineClaimsTests
{
    private readonly AuthKitClaimNormalizer _normalizer = new(Options.Create(new UserAuthenticationOptions
    {
        Provider = IdentityProviderKind.AuthKit,
        AuthKit = new AuthKitOptions
        {
            ClientId = "client_123",
            DefaultUserScopes = ["User.Impersonation"],
            PermissionMap = new Dictionary<string, string>
            {
                ["storyteller:annotation-read"] = "Annotation.Read",
                ["storyteller:configuration-write"] = "Configuration.Read Configuration.ReadWrite",
            },
        },
    }));

    [Fact]
    public void NormalizeMachine_MapsTheScopeClaimAndNeverAddsUserDefaults()
    {
        var result = _normalizer.NormalizeMachine(
            [
                new Claim("sub", "client_m2m01"),
                new Claim("client_id", "client_m2m01"),
                new Claim("scope", "storyteller:annotation-read storyteller:configuration-write unmapped"),
                new Claim("scp", "Configuration.Secrets"),
                new Claim("azp", "forged"),
            ],
            "client_m2m01");

        result.IsMachine.ShouldBeTrue();
        result.MachineId.ShouldBe("client_m2m01");
        Values(result, "scp").ShouldBe(["Annotation.Read Configuration.Read Configuration.ReadWrite"]);
        Values(result, "azp").ShouldBe(["client_m2m01"]);
        Values(result, "scope").ShouldBe(["storyteller:annotation-read storyteller:configuration-write unmapped"]);
    }

    [Fact]
    public void NormalizeMachine_WithoutScopes_AddsNoScp()
    {
        var result = _normalizer.NormalizeMachine([new Claim("sub", "client_m2m01"), new Claim("client_id", "client_m2m01")], "client_m2m01");

        Values(result, "scp").ShouldBeEmpty();
    }

    [Theory]
    [InlineData("client_m2m01", "client_m2m01", "client_m2m01")]
    [InlineData("user_01", "client_app", null)]
    [InlineData("client_m2m01", null, null)]
    [InlineData("client_m2m01", "client_other", null)]
    [InlineData("user_01", "user_01", null)]
    public void TryGetMachineClientId_RequiresAnApplicationSubject(string subject, string? clientId, string? expected)
    {
        List<Claim> claims = [new("sub", subject)];
        if (clientId is not null)
        {
            claims.Add(new Claim("client_id", clientId));
        }

        AuthKitClaimNormalizer.TryGetMachineClientId(claims).ShouldBe(expected);
    }

    [Fact]
    public void Normalize_DecodedM2MToken_IsTreatedAsAMachine()
    {
        var result = _normalizer.Normalize([new Claim("sub", "client_m2m01"), new Claim("client_id", "client_m2m01"), new Claim("scope", "storyteller:annotation-read")]);

        result.IsMachine.ShouldBeTrue();
        Values(result, "scp").ShouldBe(["Annotation.Read"]);
    }

    [Fact]
    public void Normalize_UserTokenWithClientId_StaysAUser()
    {
        var result = _normalizer.Normalize([new Claim("sub", "user_01"), new Claim("client_id", "client_123"), new Claim("email", "ada@example.com")]);

        result.IsMachine.ShouldBeFalse();
        Values(result, "scp").ShouldBe(["User.Impersonation"]);
        Values(result, "preferred_username").ShouldBe(["ada@example.com"]);
    }

    private static string[] Values(BearerValidationResult result, string type)
    {
        return result.Claims.Where(claim => claim.Type == type).Select(claim => claim.Value).ToArray();
    }
}
