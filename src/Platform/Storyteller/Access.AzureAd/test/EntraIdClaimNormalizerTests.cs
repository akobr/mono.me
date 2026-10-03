using System.Security.Claims;

using _42.Platform.Storyteller.Accessing;

using Microsoft.Extensions.Options;

namespace _42.Platform.Storyteller.Access.AzureAd.UnitTests;

public class EntraIdClaimNormalizerTests
{
    private readonly EntraIdClaimNormalizer _normalizer = new(Options.Create(new UserAuthenticationOptions
    {
        Provider = IdentityProviderKind.EntraId,
        TenantId = "tenant-id",
        ClientId = "client-id",
    }));

    [Fact]
    public void Normalize_AzpMatchingTheApiClient_IsAUserWithTheSameClaims()
    {
        List<Claim> claims = [new("sub", "user-1"), new("azp", "CLIENT-ID")];

        var result = _normalizer.Normalize(claims);

        result.Claims.ShouldBeSameAs(claims);
        result.IsMachine.ShouldBeFalse();
        result.MachineId.ShouldBeNull();
    }

    [Fact]
    public void Normalize_OtherAzp_IsAMachine()
    {
        var result = _normalizer.Normalize([new Claim("sub", "machine-app"), new Claim("azp", "machine-app")]);

        result.IsMachine.ShouldBeTrue();
        result.MachineId.ShouldBe("machine-app");
    }
}
